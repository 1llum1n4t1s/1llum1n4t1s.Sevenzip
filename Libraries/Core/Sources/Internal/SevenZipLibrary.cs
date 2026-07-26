/* ------------------------------------------------------------------------- */
//
// Copyright (c) 2010 CubeSoft, Inc.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as
// published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
/* ------------------------------------------------------------------------- */
using Cube.FileSystem.SevenZip.Kernel32;
using Cube.Reflection.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;
namespace Cube.FileSystem.SevenZip;

/// <summary>
/// 7z.dll の機能を提供するクラス。
/// </summary>
/// <remarks>
/// <para>
/// 参照カウント付きの共有シングルトンとして実装されている。
/// <see cref="Acquire"/> が借用ハンドル (<see cref="Lease"/>) を返して参照カウントを
/// インクリメントし、<see cref="Lease.Dispose"/> でデクリメントする。
/// カウントが 0 になった時点で DLL をアンロードする。
/// </para>
/// <para>
/// 参照カウント (<c>_refCount</c>) と追跡リスト (<c>_tracked</c>) はどちらもインスタンス
/// フィールドで、「世代」(= 1 回の LoadLibrary に対応する 1 インスタンス) 単位に閉じている。
/// 静的なのは現行世代を指す <c>_shared</c> と同期用の <c>_lock</c> だけである。
/// </para>
/// <para>
/// COM オブジェクトのライフタイムは <see cref="_tracked"/> <see cref="HashSet{T}"/> で管理する。
/// DLL アンロード前に全オブジェクトの FinalRelease を確実に実行する。
/// HashSet により <see cref="Lease.ReleaseComWrapper"/> の除去コストを O(1) に保つ。
/// </para>
/// <para>
/// <b>並列実行の制約:</b> 同一プロセス内で複数の
/// <see cref="ArchiveReader"/> / <see cref="ArchiveWriter"/> を並行して動作させることは
/// <b>現状サポートしていない</b>。理由:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="ArchiveReader"/> / <see cref="ArchiveWriter"/> の契約は「1 インスタンスを
/// 同時に触るスレッドは常に 1 つ」であり、スレッドアフィニティ (同一スレッドで居続けること) は
/// 要求しない。スレッドを跨ぐ受け渡しは、直列化プリミティブ
/// (<see cref="System.Threading.SemaphoreSlim"/> / <c>lock</c> / <c>await</c>) による
/// happens-before があれば安全である (thread-static / STA 依存の状態は持たない)。
/// ただしこの契約もコード上で強制されておらず、破ったときの症状はネイティブ側のクラッシュに
/// なるため、契約違反であることが分かりにくい。
/// </description></item>
/// <item><description>
/// COM コールバック (UpdateCallback / ExtractCallback) は 7z.dll のマルチスレッド圧縮からの
/// 並行呼び出しに対しては同期済みだが、これは「1 つの圧縮処理の内部」を守るためのもので、
/// 複数インスタンスを跨いだ利用の検証は行っていない。
/// </description></item>
/// </list>
/// <para>
/// なお <c>_refCount</c> / <c>_tracked</c> 自体は <c>_lock</c> で保護されており、
/// <c>_tracked.Remove</c> の成否で解放責任を一意化しているため二重 FinalRelease は起きない。
/// つまり禁止の根拠はこれらのフィールドではなく、上記の未検証・未強制な部分にある。
/// </para>
/// <para>
/// サーバーサイドや GUI アプリでは <see cref="System.Threading.SemaphoreSlim"/> や
/// <c>Task.Run</c> での 1 スロット直列化を行うこと。
/// </para>
/// </remarks>
internal sealed class SevenZipLibrary
{
    // StrategyBasedComWrappers はスレッドセーフかつシングルトンで問題ない
    private static readonly StrategyBasedComWrappers s_comWrappers = new();

    #region Constructors

    /// <summary>
    /// 共有インスタンスを借用し、その <see cref="Lease"/> を返す。
    /// </summary>
    /// <returns>借用ハンドル。利用者は必ず 1 回 Dispose する。</returns>
    /// <remarks>
    /// 初回呼び出し時 (および前世代がアンロード済みの場合) に 7z.dll をロードする。
    /// 返す値はシングルトン本体ではなく借用ハンドルであるため、
    /// 利用者側は「自分が借りた世代」だけを返却でき、他世代の参照カウントに触れない。
    /// </remarks>
    public static Lease Acquire()
    {
        lock (_lock)
        {
            // インスタンスが未生成またはハンドルが閉じられている場合は再作成する
            if (_shared is null || _shared._handle.IsClosed)
            {
                _shared = new SevenZipLibrary();
            }
            _shared._refCount++;
            return new Lease(_shared);
        }
    }

    /// <summary>
    /// SevenZipLibrary クラスの新しいインスタンスを初期化する。
    /// </summary>
    /// <remarks>
    /// アセンブリと同じディレクトリにある 7z.dll をロードする。
    /// CreateObject 関数ポインタを取得して COM オブジェクトの生成に使用する。
    /// </remarks>
    private SevenZipLibrary()
    {
        // 本ライブラリは win-x64 / win-arm64 の 7z.dll のみ同梱しているため、
        // 他のアーキテクチャでは明示的に PlatformNotSupportedException を投げる。
        // OS 縛りは TargetFramework=net10.0-windows8 により型読み込み時に強制されるため、
        // ここではアーキテクチャのみをチェックする。
        var arch = RuntimeInformation.ProcessArchitecture;
        if (arch != Architecture.X64 && arch != Architecture.Arm64)
        {
            throw new PlatformNotSupportedException(
                $"1llum1n4t1s.Sevenzip supports Windows x64 / arm64 only. Current process architecture: {arch}.");
        }

        // アセンブリと同じディレクトリから 7z.dll を探す。
        // publish 済みや RuntimeIdentifier 指定ビルドではアセンブリ直下に配置される。
        var dir = GetType().Assembly.GetDirectoryName();
        _handle = NativeMethods.LoadLibrary(Io.Combine(dir, "7z.dll"));

        // RID なしの dotnet build では NuGet の runtime asset resolution により
        // runtimes/{rid}/native/ サブディレクトリに配置される。フォールバックで探す。
        if (_handle.IsInvalid)
        {
            var rid = arch == Architecture.Arm64 ? "win-arm64" : "win-x64";
            _handle = NativeMethods.LoadLibrary(Io.Combine(dir, "runtimes", rid, "native", "7z.dll"));
        }
        if (_handle.IsInvalid) throw new Win32Exception("LoadLibrary");

        // "CreateObject" エクスポート関数のアドレスを取得する
        _createObjectPtr = NativeMethods.GetProcAddress(_handle, "CreateObject");
        if (_createObjectPtr == IntPtr.Zero) throw new Win32Exception("GetProcAddress");
    }

    #endregion

    #region Methods

    /// <summary>
    /// 指定したフォーマットの InArchive オブジェクトを取得する。
    /// </summary>
    /// <param name="format">アーカイブフォーマット。</param>
    /// <returns>IInArchive オブジェクト。</returns>
    public IInArchive GetInArchive(Format format) => GetArchive<IInArchive>(format.ToClassId());

    /// <summary>
    /// 指定したクラス ID の InArchive オブジェクトを取得する。
    /// </summary>
    /// <param name="clsid">クラス ID。</param>
    /// <returns>IInArchive オブジェクト。</returns>
    public IInArchive GetInArchive(Guid clsid) => GetArchive<IInArchive>(clsid);

    /// <summary>
    /// 指定したアーカイブフォーマットの OutArchive オブジェクトを取得する。
    /// </summary>
    /// <param name="format">アーカイブフォーマット。</param>
    /// <returns>IOutArchive オブジェクト。</returns>
    public IOutArchive GetOutArchive(Format format) => GetArchive<IOutArchive>(format.ToClassId());

    /// <summary>
    /// 指定したクラス ID の OutArchive オブジェクトを取得する。
    /// </summary>
    /// <param name="clsid">クラス ID。</param>
    /// <returns>IOutArchive オブジェクト。</returns>
    public IOutArchive GetOutArchive(Guid clsid) => GetArchive<IOutArchive>(clsid);

    /// <summary>
    /// COM オブジェクトに対して特定のインターフェースを QueryInterface で問い合わせる。
    /// </summary>
    /// <typeparam name="T">取得するインターフェース型。</typeparam>
    /// <param name="comObject">ComWrappers でラップされたソース COM オブジェクト。</param>
    /// <returns>
    /// ラップされたインターフェース；インターフェースがサポートされない場合は null。
    /// </returns>
    /// <remarks>
    /// 取得したオブジェクトは <see cref="_tracked"/> に登録され、
    /// Dispose 時に FinalRelease が呼び出される。
    /// </remarks>
    public T QueryInterface<T>(object comObject) where T : class
    {
        // ComWrappers.TryGetComInstance で元の COM ポインタを取得する
        if (!ComWrappers.TryGetComInstance(comObject, out var unkPtr))
            return null;
        try
        {
            var iid = typeof(T).GUID;
            // IUnknown::QueryInterface を呼び出して目的のインターフェースポインタを取得する
            var hr = Marshal.QueryInterface(unkPtr, in iid, out var ptr);
            if (hr != 0 || ptr == IntPtr.Zero) return null;

            // UniqueInstance: このラッパーが COM 参照の所有権を持つ（QueryInterface の refcount を引き継ぐ）
            var obj = s_comWrappers
                .GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.UniqueInstance);
            // DLL アンロード前に FinalRelease を実行できるよう追跡リストに登録する
            Track(obj);
            return (T)obj;
        }
        finally
        {
            // TryGetComInstance で AddRef されたカウントをデクリメントする
            Marshal.Release(unkPtr);
        }
    }

    /// <summary>
    /// 共有の StrategyBasedComWrappers インスタンスを返す。
    /// </summary>
    /// <returns>共有 ComWrappers インスタンス。</returns>
    public static StrategyBasedComWrappers GetComWrappers() => s_comWrappers;

    /// <summary>
    /// UniqueInstance で生成した COM ラッパーを明示的に解放する。
    /// </summary>
    /// <param name="comWrapper">解放する COM ラッパーオブジェクト。</param>
    /// <remarks>
    /// GC の Finalize が DLL アンロード後に Release を呼ぶことを防ぐために使用する。
    /// ComObject.FinalRelease() はアトミックに COM ポインタを解放してファイナライズを抑制する。
    /// 借用元の世代 (<c>this</c>) の <see cref="_tracked"/> だけを見るため、世代を跨いだ
    /// 誤解放は起こらない。
    /// </remarks>
    private void ReleaseComWrapperCore(object comWrapper)
    {
        if (comWrapper is null) return;

        // 二重解放防止: lock 内で _tracked から Remove が成功した場合のみ FinalRelease する。
        // Release 側も同様に lock 内で _tracked.Clear() してからスナップショットを取るため、
        // 「どちらか片方が Remove 責任を持つ」形に収束して二重 FinalRelease を防ぐ。
        bool shouldRelease;
        lock (_lock) shouldRelease = _tracked.Remove(comWrapper);

        if (shouldRelease && comWrapper is ComObject comObject)
        {
            // lock 外で FinalRelease (ネイティブ遷移中の再入デッドロック回避)
            comObject.FinalRelease();
        }
        // shouldRelease=false の場合は Release 側が foreach で FinalRelease する責任を持つ。
    }

    /// <summary>
    /// 借用を 1 つ返却し、この世代の参照カウントが 0 になった場合はライブラリを解放する。
    /// </summary>
    /// <param name="fromFinalizer">finalizer スレッドからの返却の場合は true。</param>
    /// <remarks>
    /// <para>
    /// カウントが 0 になる際は、全追跡 COM オブジェクトの FinalRelease を
    /// DLL アンロード前に実行する。これにより GC が後で Release を呼ぶことを防ぐ。
    /// </para>
    /// <para>
    /// ただし finalizer スレッドでは次の 2 つが危険なため、この 2 つを行わない。
    /// (1) 追跡中の COM ラッパーが既に finalize 済みの可能性があり、<c>FinalRelease</c> を
    /// 呼ぶと未処理例外でプロセスごとクラッシュしうる。
    /// (2) DLL をアンロードすると、後から finalize される ComObject の Release が
    /// アンマップ領域へ到達して AccessViolation になる。
    /// このとき行うのは「参照カウントを戻し、追跡参照を手放し、共有インスタンスを切り離す」
    /// だけで、モジュールはプロセス終了まで残るが Release 先が生きているので安全側に倒れる。
    /// 次回 <see cref="Acquire"/> は新しい世代 (= 新規 LoadLibrary) を生成する。
    /// </para>
    /// <para>
    /// 一度でも finalizer 経路の返却が起きた世代は <see cref="_keepAlive"/> が立ち、
    /// 以降その世代のハンドルは Close しない。追跡参照を手放した後に残りの借用が
    /// 正常 Dispose されても、finalize 待ちの ComObject が生きているためである。
    /// </para>
    /// <para>
    /// 返却自体を省くと、利用者が Dispose を 1 回忘れるだけで <c>_refCount</c> が永久に 0 へ
    /// 戻らなくなり、以降に正しく Dispose された全インスタンスの解放処理まで無効化される
    /// (<c>_tracked</c> も無制限に伸び続ける)。
    /// </para>
    /// </remarks>
    private void Release(bool fromFinalizer)
    {
        // FinalRelease はロック外で呼ぶ。ロック保持のままネイティブへ遷移すると、
        // ネイティブ→マネージド再入時の `lock (_lock)` で別スレッドが解放待ちに入り
        // デッドロックが発生する可能性がある。
        List<object> toRelease = null;
        SafeHandle handleToClose = null;

        lock (_lock)
        {
            if (fromFinalizer) _keepAlive = true;

            // 参照カウントを減らし、まだ借用している呼び出し元がいる場合は何もしない
            if (--_refCount > 0) return;

            // 解放対象をローカルリストに取り出してから lock を抜ける。
            // finalizer 経路では FinalRelease を行わず、追跡参照を手放して
            // 各 ComObject 自身の finalizer に Release を委ねる。
            if (!fromFinalizer) toRelease = new List<object>(_tracked);
            _tracked.Clear();

            // ネイティブ DLL ハンドルも lock 外で Close する (_keepAlive の世代は Close しない)
            if (!_keepAlive && _handle != null && !_handle.IsClosed) handleToClose = _handle;

            // 自分が現行世代の場合のみ共有参照をクリアして次回 Acquire 時に再生成させる。
            // 旧世代の返却で現行世代を巻き添えにしない。
            if (ReferenceEquals(_shared, this)) _shared = null;
        }

        // lock を抜けた後に FinalRelease / Close を実行する
        if (toRelease is not null)
        {
            foreach (var obj in toRelease)
            {
                if (obj is ComObject co) co.FinalRelease();
            }
        }
        handleToClose?.Close();
    }

    /// <summary>
    /// finalizer 経路から借用の返却を安全に実行する。
    /// </summary>
    /// <param name="lib">返却する借用ハンドル。ctor が失敗している場合は null。</param>
    /// <param name="owner">呼び出し元のクラス名 (ログ出力用)。</param>
    /// <remarks>
    /// <para>
    /// .NET の finalizer スレッドで発生した未処理例外は catch できずプロセスごと即死するため、
    /// この経路で行う処理は全て try/catch で囲む必要がある。警告ログの出力もその対象に含める:
    /// この経路が走るのはプロセス終了間際が典型で、ログシンクが既に閉じている・ログファイルが
    /// ロックされている確率が高い。ログ出力が保護されていないと、後続の
    /// <see cref="Lease.ReleaseFromFinalizer"/> の try/catch より手前で死ぬ。
    /// </para>
    /// <para>
    /// <see cref="ArchiveReader"/> / <see cref="ArchiveWriter"/> の双方から呼ばれる共通処理。
    /// </para>
    /// </remarks>
    public static void ReleaseFromFinalizerSafe(Lease lib, string owner)
    {
        try
        {
            Logger.Warn($"[{owner}] Dispose が呼ばれずに finalize されました。" +
                        "7z.dll はプロセス終了まで解放されません。using / Dispose を使用してください。");
        }
        catch { /* finalizer で例外を漏らさない */ }

        try { lib?.ReleaseFromFinalizer(); } catch { /* 同上 */ }
    }

    #endregion

    #region Lease

    /// <summary>
    /// <see cref="SevenZipLibrary"/> の借用ハンドル。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Acquire"/> が返す軽量な <see cref="IDisposable"/>。借用ハンドル自身が
    /// 「返却済みか」を持つため <see cref="Dispose"/> は冪等であり、
    /// <see cref="IDisposable"/> の規約 (複数回の Dispose を許容する) を満たす。
    /// 呼び出し側の二重 Dispose ガードに依存しない。
    /// </para>
    /// <para>
    /// また借用時の世代 (<see cref="SevenZipLibrary"/> インスタンス) を保持するため、
    /// finalizer 経路の返却で共有インスタンスが差し替わった後に旧世代の借用が返却されても、
    /// 減るのは旧世代の参照カウントだけである。使用中の新世代を早期に解放してしまう
    /// 世代跨ぎの誤カウントは構造的に起こらない。
    /// </para>
    /// </remarks>
    public sealed class Lease : IDisposable
    {
        /// <summary>
        /// Lease クラスの新しいインスタンスを初期化する。
        /// </summary>
        /// <param name="library">借用元の世代。</param>
        internal Lease(SevenZipLibrary library) => _library = library;

        /// <summary>
        /// 指定したフォーマットの InArchive オブジェクトを取得する。
        /// </summary>
        /// <param name="format">アーカイブフォーマット。</param>
        /// <returns>IInArchive オブジェクト。</returns>
        public IInArchive GetInArchive(Format format) => Library.GetInArchive(format);

        /// <summary>
        /// 指定したクラス ID の InArchive オブジェクトを取得する。
        /// </summary>
        /// <param name="clsid">クラス ID。</param>
        /// <returns>IInArchive オブジェクト。</returns>
        public IInArchive GetInArchive(Guid clsid) => Library.GetInArchive(clsid);

        /// <summary>
        /// 指定したアーカイブフォーマットの OutArchive オブジェクトを取得する。
        /// </summary>
        /// <param name="format">アーカイブフォーマット。</param>
        /// <returns>IOutArchive オブジェクト。</returns>
        public IOutArchive GetOutArchive(Format format) => Library.GetOutArchive(format);

        /// <summary>
        /// 指定したクラス ID の OutArchive オブジェクトを取得する。
        /// </summary>
        /// <param name="clsid">クラス ID。</param>
        /// <returns>IOutArchive オブジェクト。</returns>
        public IOutArchive GetOutArchive(Guid clsid) => Library.GetOutArchive(clsid);

        /// <summary>
        /// COM オブジェクトに対して特定のインターフェースを QueryInterface で問い合わせる。
        /// </summary>
        /// <typeparam name="T">取得するインターフェース型。</typeparam>
        /// <param name="comObject">ComWrappers でラップされたソース COM オブジェクト。</param>
        /// <returns>
        /// ラップされたインターフェース；インターフェースがサポートされない場合は null。
        /// </returns>
        public T QueryInterface<T>(object comObject) where T : class =>
            Library.QueryInterface<T>(comObject);

        /// <summary>
        /// UniqueInstance で生成した COM ラッパーを明示的に解放する。
        /// </summary>
        /// <param name="comWrapper">解放する COM ラッパーオブジェクト。</param>
        /// <remarks>
        /// 返却後は追跡側が既に解放済みのため何も行わない。
        /// </remarks>
        public void ReleaseComWrapper(object comWrapper) =>
            _library?.ReleaseComWrapperCore(comWrapper);

        /// <summary>
        /// 借用を返却する。複数回呼び出しても 2 回目以降は何も行わない。
        /// </summary>
        public void Dispose() => Return(fromFinalizer: false);

        /// <summary>
        /// finalizer 経路から借用を返却する。
        /// </summary>
        /// <remarks>
        /// FinalRelease と DLL アンロードを行わない点だけが <see cref="Dispose"/> と異なる
        /// (詳細は <see cref="SevenZipLibrary.Release"/> の remarks)。
        /// </remarks>
        public void ReleaseFromFinalizer() => Return(fromFinalizer: true);

        /// <summary>
        /// 借用を 1 回だけ返却する。
        /// </summary>
        /// <param name="fromFinalizer">finalizer スレッドからの返却の場合は true。</param>
        private void Return(bool fromFinalizer)
        {
            // Interlocked で「最初の 1 回」を確定させ、二重返却による参照カウントの
            // 過剰デクリメントを防ぐ (複数スレッドからの同時 Dispose も含めて安全)。
            if (Interlocked.Exchange(ref _released, 1) != 0) return;

            var library = _library;
            _library = null;
            library?.Release(fromFinalizer);
        }

        /// <summary>
        /// 借用元の世代を取得する。返却済みの場合は例外を送出する。
        /// </summary>
        private SevenZipLibrary Library =>
            _library ?? throw new ObjectDisposedException(nameof(Lease));

        private SevenZipLibrary _library;
        private int _released;
    }

    #endregion

    #region Implementations

    /// <summary>
    /// COM ラッパーオブジェクトを追跡リストに登録する。
    /// </summary>
    /// <param name="comWrapper">追跡するオブジェクト。</param>
    private void Track(object comWrapper)
    {
        // 複数スレッドから同時に Track される可能性があるためロックする
        lock (_lock) _tracked.Add(comWrapper);
    }

    /// <summary>
    /// 指定したクラス ID の COM アーカイブオブジェクトを生成して指定インターフェース型でラップする。
    /// </summary>
    /// <typeparam name="T">ラップするインターフェース型（IInArchive または IOutArchive）。</typeparam>
    /// <param name="clsid">生成する COM クラスの GUID。</param>
    /// <returns>ラップされたインターフェースオブジェクト。</returns>
    private T GetArchive<T>(Guid clsid) where T : class
    {
        var iid = typeof(T).GUID;
        // 7z.dll の CreateObject 関数を呼び出して COM インターフェースポインタを取得する
        var ptr = CreateObject(ref clsid, ref iid);
        // UniqueInstance でラップして参照の所有権を ComWrappers に移譲する
        var obj = s_comWrappers
            .GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.UniqueInstance);
        // DLL アンロード前に解放できるよう追跡リストに登録する
        Track(obj);
        return (T)obj;
    }

    /// <summary>
    /// 7z.dll の CreateObject 関数を unsafe 関数ポインタ経由で呼び出し、
    /// 生の COM インターフェースポインタを返す。
    /// </summary>
    /// <param name="clsid">生成するクラスの GUID。</param>
    /// <param name="iid">取得するインターフェースの GUID。</param>
    /// <returns>COM インターフェースの生ポインタ。</returns>
    private unsafe nint CreateObject(ref Guid clsid, ref Guid iid)
    {
        // Stdcall 呼び出し規約の関数ポインタとしてキャストする（7z.dll の ABI に合わせる）
        var fn = (delegate* unmanaged[Stdcall]<Guid*, Guid*, nint*, int>)_createObjectPtr;
        nint result;
        int hr;

        // fixed でマネージドポインタを固定してアンマネージドコードに渡す
        fixed (Guid* pClsid = &clsid)
        fixed (Guid* pIid = &iid)
        {
            hr = fn(pClsid, pIid, &result);
        }
        // HRESULT が失敗コードの場合は対応する COM 例外をスローする
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        return result;
    }

    #endregion

    #region Fields
    // 現行世代の共有インスタンス（null = 未初期化またはアンロード済み）
    private static SevenZipLibrary _shared;
    // スレッドセーフな操作のための同期オブジェクト。
    // _shared と、全世代の _refCount / _tracked / _keepAlive を保護する。
    private static readonly object _lock = new();

    // この世代を借りている Lease の数（世代ごとに独立。static にすると世代跨ぎで誤カウントする）
    private int _refCount;
    // finalizer 経路の返却が一度でも起きた世代は DLL ハンドルを Close しない。
    // 追跡参照を手放した後は、まだ finalize されていない ComObject が後から Release を
    // 呼びうるため、ここで FreeLibrary するとアンマップ領域へ到達して AccessViolation になる。
    private bool _keepAlive;

    // LoadLibrary で取得した 7z.dll のネイティブハンドル
    private readonly SafeLibraryHandle _handle;
    // GetProcAddress で取得した CreateObject 関数のアドレス
    private readonly IntPtr _createObjectPtr;
    // FinalRelease を確実に呼び出すために生成した COM ラッパーを追跡する HashSet。
    // ReleaseComWrapper の Remove を O(1) にするため List から HashSet に変更。
    // 参照等価性 (ReferenceEqualityComparer) を使うことで COM ラッパーオブジェクトを
    // 同一インスタンスのみマッチさせる (オーバーロードされた Equals に依存しない)。
    private readonly HashSet<object> _tracked = new(ReferenceEqualityComparer.Instance);
    #endregion
}
