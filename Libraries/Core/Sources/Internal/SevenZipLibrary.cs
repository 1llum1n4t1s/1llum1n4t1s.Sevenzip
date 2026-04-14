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
namespace Cube.FileSystem.SevenZip;

/// <summary>
/// 7z.dll の機能を提供するクラス。
/// </summary>
/// <remarks>
/// 参照カウント付きの共有シングルトンとして実装されている。
/// <see cref="Acquire"/> で参照カウントをインクリメントし、
/// <see cref="Dispose"/> でデクリメントする。
/// カウントが 0 になった時点で DLL をアンロードする。
///
/// COM オブジェクトのライフタイムは <see cref="_tracked"/> リストで管理する。
/// DLL アンロード前に全オブジェクトの FinalRelease を確実に実行する。
/// </remarks>
internal sealed class SevenZipLibrary : IDisposable
{
    // StrategyBasedComWrappers はスレッドセーフかつシングルトンで問題ない
    private static readonly StrategyBasedComWrappers s_comWrappers = new();

    #region Constructors

    /// <summary>
    /// 共有インスタンスの参照カウントをインクリメントして返す。
    /// </summary>
    /// <returns>共有 SevenZipLibrary インスタンス。</returns>
    /// <remarks>
    /// 初回呼び出し時に 7z.dll をロードする。
    /// ハンドルが無効になっている場合も再ロードする。
    /// </remarks>
    public static SevenZipLibrary Acquire()
    {
        lock (_lock)
        {
            // インスタンスが未生成またはハンドルが閉じられている場合は再作成する
            if (_shared is null || _shared._handle.IsClosed)
            {
                _shared = new SevenZipLibrary();
            }
            _refCount++;
            return _shared;
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
        // 本ライブラリは win-x64 の 7z.dll のみ同梱しているため、
        // 他のアーキテクチャでは明示的に PlatformNotSupportedException を投げる。
        // OS 縛りは TargetFramework=net10.0-windows8 により型読み込み時に強制されるため、
        // ここではアーキテクチャのみをチェックする。
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException(
                $"1llum1n4t1s.Sevenzip only supports Windows x64. Current process architecture: {RuntimeInformation.ProcessArchitecture}.");
        }

        // アセンブリと同じディレクトリから 7z.dll を探す
        var dll = Io.Combine(GetType().Assembly.GetDirectoryName(), "7z.dll");
        _handle = NativeMethods.LoadLibrary(dll);
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
    /// </remarks>
    public static void ReleaseComWrapper(object comWrapper)
    {
        // ComObject 型の場合は FinalRelease で COM ポインタをアトミックに解放する
        if (comWrapper is ComObject comObject) comObject.FinalRelease();

        // 共有シングルトンの追跡リストからも除去してメモリリークを防ぐ
        lock (_lock)
        {
            _shared?._tracked.Remove(comWrapper);
        }
    }

    /// <summary>
    /// 参照カウントをデクリメントし、0 になった場合はライブラリを解放する。
    /// </summary>
    /// <remarks>
    /// カウントが 0 になる際は、全追跡 COM オブジェクトの FinalRelease を
    /// DLL アンロード前に実行する。これにより GC が後で Release を呼ぶことを防ぐ。
    /// </remarks>
    public void Dispose()
    {
        lock (_lock)
        {
            // 参照カウントを減らし、まだ参照している呼び出し元がいる場合は何もしない
            if (--_refCount > 0) return;

            // DLL アンロード前に全追跡 COM ラッパーを解放する。
            // GC の Finalize がアンロード済み DLL の vtable を呼ぶことを防ぐ。
            // 注意: ReleaseComWrapper は _tracked.Remove を呼ぶため foreach 中に使用不可
            foreach (var obj in _tracked)
            {
                if (obj is ComObject co) co.FinalRelease();
            }
            _tracked.Clear();

            // ネイティブ DLL ハンドルを解放する
            if (_handle != null && !_handle.IsClosed) _handle.Close();
            // 共有インスタンスをクリアして次回 Acquire 時に再生成させる
            _shared = null;
        }
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
    // 共有シングルトンインスタンス（null = 未初期化またはアンロード済み）
    private static SevenZipLibrary _shared;
    // 共有インスタンスへの参照カウント
    private static int _refCount;
    // スレッドセーフな操作のための同期オブジェクト
    private static readonly object _lock = new();

    // LoadLibrary で取得した 7z.dll のネイティブハンドル
    private readonly SafeLibraryHandle _handle;
    // GetProcAddress で取得した CreateObject 関数のアドレス
    private readonly IntPtr _createObjectPtr;
    // FinalRelease を確実に呼び出すために生成した COM ラッパーを追跡するリスト
    private readonly List<object> _tracked = [];
    #endregion
}
