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
using Cube.Text.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
namespace Cube.FileSystem.SevenZip;

/* ------------------------------------------------------------------------- */
///
/// ArchiveReader
///
/// <summary>
/// Provides functionality to extract an archived file.
/// </summary>
///
/* ------------------------------------------------------------------------- */
public sealed class ArchiveReader : DisposableBase
{
    #region Constructors

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Initializes a new instance of the ArchiveReader class with
    /// the specified path.
    /// </summary>
    ///
    /// <param name="src">Path of the archive.</param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(string src) : this(src, string.Empty) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Initializes a new instance of the ArchiveReader class with
    /// the specified arguments.
    /// </summary>
    ///
    /// <param name="src">Path of the archive.</param>
    /// <param name="password">Password of the archive.</param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(string src, string password) : this(src, password, new()) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Initializes a new instance of the ArchiveReader class with
    /// the specified arguments.
    /// </summary>
    ///
    /// <param name="src">Path of the archive.</param>
    /// <param name="password">Query object to get password.</param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(string src, IQuery<string> password) : this(src, password, new()) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Initializes a new instance of the ArchiveReader class with
    /// the specified arguments.
    /// </summary>
    ///
    /// <param name="src">Path of the archive.</param>
    /// <param name="options">Options to extract the archive.</param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(string src, ArchiveOption options) : this(src, string.Empty, options) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Initializes a new instance of the ArchiveReader class with
    /// the specified arguments.
    /// </summary>
    ///
    /// <param name="src">Path of the archive.</param>
    /// <param name="password">Password of the archive.</param>
    /// <param name="options">Options to extract the archive.</param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(string src, string password, ArchiveOption options) :
        this(FormatFactory.From(src), OpenPath(src), src, new(password), options, dispose: true) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Initializes a new instance of the ArchiveReader class with
    /// the specified arguments.
    /// </summary>
    ///
    /// <param name="src">Path of the archive.</param>
    /// <param name="password">Query object to get password.</param>
    /// <param name="options">Options to extract the archive.</param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(string src, IQuery<string> password, ArchiveOption options) :
        this(FormatFactory.From(src), OpenPath(src), src, new(password), options, dispose: true) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Stream ベースで ArchiveReader クラスの新しいインスタンスを初期化する。
    /// フォーマットはストリーム先頭のシグネチャから自動判定する。
    /// </summary>
    ///
    /// <param name="src">アーカイブを読み取るシーク可能な Stream。</param>
    /// <param name="leaveOpen">
    /// true の場合、オブジェクト破棄時に <paramref name="src"/> を Dispose しない
    /// （呼び出し側が所有権を保持する）。既定値は true。
    /// </param>
    /// <param name="sourceHint">
    /// <see cref="Source"/> プロパティに設定する任意のヒント文字列（通常は元のファイルパスや URL）。
    /// エラーログや再オープン時の識別用。省略時は空文字。
    /// </param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(Stream src, bool leaveOpen = true, string sourceHint = null) :
        this(src, string.Empty, new(), leaveOpen, sourceHint) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Stream ベースで ArchiveReader クラスの新しいインスタンスを初期化する。
    /// </summary>
    ///
    /// <param name="src">アーカイブを読み取るシーク可能な Stream。</param>
    /// <param name="password">アーカイブのパスワード。</param>
    /// <param name="leaveOpen">Stream の所有権を保持するかどうか。</param>
    /// <param name="sourceHint">
    /// <see cref="Source"/> プロパティに設定する任意のヒント文字列。省略時は空文字。
    /// </param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(Stream src, string password, bool leaveOpen = true, string sourceHint = null) :
        this(src, password, new(), leaveOpen, sourceHint) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Stream ベースで ArchiveReader クラスの新しいインスタンスを初期化する。
    /// </summary>
    ///
    /// <param name="src">アーカイブを読み取るシーク可能な Stream。</param>
    /// <param name="password">アーカイブのパスワード。</param>
    /// <param name="options">展開オプション。</param>
    /// <param name="leaveOpen">Stream の所有権を保持するかどうか。</param>
    /// <param name="sourceHint">
    /// <see cref="Source"/> プロパティに設定する任意のヒント文字列。省略時は空文字。
    /// </param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(Stream src, string password, ArchiveOption options, bool leaveOpen = true, string sourceHint = null) :
        this(FormatFactory.From(src), src, sourceHint, new(password), options, dispose: !leaveOpen) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// Stream ベースで ArchiveReader クラスの新しいインスタンスを初期化する。
    /// </summary>
    ///
    /// <param name="src">アーカイブを読み取るシーク可能な Stream。</param>
    /// <param name="password">パスワード問い合わせオブジェクト。</param>
    /// <param name="options">展開オプション。</param>
    /// <param name="leaveOpen">Stream の所有権を保持するかどうか。</param>
    /// <param name="sourceHint">
    /// <see cref="Source"/> プロパティに設定する任意のヒント文字列。省略時は空文字。
    /// </param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(Stream src, IQuery<string> password, ArchiveOption options, bool leaveOpen = true, string sourceHint = null) :
        this(FormatFactory.From(src), src, sourceHint, new(password), options, dispose: !leaveOpen) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// フォーマットを明示指定して Stream ベースで初期化する。
    /// シグネチャ判定が失敗する可能性のあるフォーマット（TAR など）向け。
    /// </summary>
    ///
    /// <param name="format">アーカイブフォーマット。</param>
    /// <param name="src">アーカイブを読み取るシーク可能な Stream。</param>
    /// <param name="password">パスワード問い合わせオブジェクト。</param>
    /// <param name="options">展開オプション。</param>
    /// <param name="leaveOpen">Stream の所有権を保持するかどうか。</param>
    /// <param name="sourceHint">
    /// <see cref="Source"/> プロパティに設定する任意のヒント文字列。省略時は空文字。
    /// </param>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(Format format, Stream src, IQuery<string> password,
        ArchiveOption options, bool leaveOpen = true, string sourceHint = null) :
        this(format, src, sourceHint, new(password), options, dispose: !leaveOpen) { }

    /* --------------------------------------------------------------------- */
    ///
    /// ArchiveReader
    ///
    /// <summary>
    /// コアのコンストラクタ。全ての public コンストラクタはこの経路に集約される。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private ArchiveReader(Format format, Stream src, string sourceHint,
        PasswordQuery password, ArchiveOption options, bool dispose)
    {
        if (src is null) throw new ArgumentNullException(nameof(src));
        if (format == Format.Unknown)
        {
            // ctor 失敗で this は孤児化する。所有権を受け取った Stream を閉じ、
            // finalizer 不要を明示する (ctor cleanup の方針は下の catch コメント参照)。
            if (dispose) { try { src.Dispose(); } catch { /* best effort */ } }
            GC.SuppressFinalize(this);
            throw new UnknownFormatException();
        }

        Source  = sourceHint ?? string.Empty;
        Format  = format;
        Options = options;
        _password = password;
        try
        {
            var lib = Hook(SevenZipLibrary.Acquire());
            _lib  = lib;
            _core = lib.GetInArchive(format);

            // Format.Zip 以外で Encoding/CodePage 非デフォルト指定の場合は警告
            if (format != Format.Zip && !options.IsDefaultCodePage())
                Logger.Warn(
                    $"[ArchiveReader] ArchiveOption.Encoding/CodePage is only honored for Format.Zip " +
                    $"and ignored for {format}.");

            // Open() 前にコードページを設定する（7z.dll が ZIP ファイル名のデコードに使用）
            // ZIP 形式かつデフォルト（Oem かつ Encoding 未指定）以外の場合のみ SetProperties を呼ぶ
            if (format == Format.Zip && !options.IsDefaultCodePage())
            {
                ISetProperties setProps = null;
                try
                {
                    setProps = lib.QueryInterface<ISetProperties>(_core);
                    if (setProps is not null)
                    {
                        var codePage = options.ResolveCodePage();
                        var keys = new[] { "cp" };
                        var vals = new[] { PropVariant.Create(codePage) };
                        var pin = GCHandle.Alloc(vals, GCHandleType.Pinned);
                        try
                        {
                            var hr = setProps.SetProperties(keys, pin.AddrOfPinnedObject(), (uint)keys.Length);
                            if (hr != 0) throw new IOException(
                                $"コードページ {codePage} の設定に失敗しました (HRESULT: 0x{hr:X8})");
                        }
                        finally { pin.Free(); }
                    }
                }
                finally
                {
                    lib.ReleaseComWrapper(setProps);
                }
            }

            var cb = Hook(new OpenCallback(Source) { Password = _password });
            var ss = new ArchiveStreamReader(src, dispose);
            cb.Streams.Add(ss);

            // Keep managed references alive to prevent GC from collecting
            // objects whose CCWs are held by native 7-Zip.
            // (_openStream の代入をもって src の所有権は cb → _disposable 系列に移る)
            _openStream   = ss;
            _openCallback = cb;

            var code = _core.Open(ss, IntPtr.Zero, cb);
            GC.KeepAlive(cb);
            GC.KeepAlive(ss);
            if (code != 0)
            {
                Logger.Warn($"[Open] Code:{code}");
                // Open が失敗 (非 S_OK) のまま続行すると、GetNumberOfItems が 0 を返して
                // Items が空になり「中身ゼロのアーカイブ」として静かに誤動作する
                // (壊れた書庫・非対応書庫・形式不一致の展開が 0 件成功扱いになる)。失敗を明示する。
                // ヘッダー暗号のパスワード不一致などコールバックが捕捉した例外があれば内包して投げる。
                if (cb.Exceptions.Count > 0)
                {
                    var inner = cb.Exceptions.Pop();
                    if (inner is EncryptionException) throw inner;
                    throw new SevenZipException(SevenZipCode.HeadersError, inner);
                }

                // S_FALSE (1) は「アーカイブとして解釈できない」を意味するので IsNotArc が正しい。
                // 一方で負の HRESULT はネットワーク断・アクセス拒否・共有違反などの障害であり、
                // IsNotArc へ潰すと利用者が「書庫が壊れている / 非対応形式」と誤診して
                // アーカイブ再生成に走る。実 HRESULT に対応する例外を inner として保持する。
                if (code > 0) throw new SevenZipException(SevenZipCode.IsNotArc);
                throw new SevenZipException(SevenZipCode.UnknownError,
                    Marshal.GetExceptionForHR(code) ??
                    new COMException($"IInArchive.Open failed. HRESULT: 0x{code:X8}", code));
            }

            Items = new ArchiveCollection(_core, (int)_core.GetNumberOfItems(), Source, format);
        }
        catch
        {
            // ctor 失敗時のクリーンアップ。throw すると this は誰にも参照されず孤児化し、
            // (1) 取得済みの COM ラッパー・ライブラリ参照カウント・Stream が GC まで
            //     リークする (パスワード無しでヘッダ暗号化書庫を開く失敗は正常系なので頻発)、
            // (2) 後に finalizer (~DisposableBase → Dispose(false)) が走った時点では
            //     _core の ComObject が先に finalize 済みのことがあり、旧実装では
            //     ObjectDisposedException が finalizer スレッドで発生してプロセスごと
            //     クラッシュしていた (Lhamiel テストスイートで実測・再現)。
            // ここで同期的に解放し、finalizer 自体を不要化する。
            var owned = _openStream is not null;
            try { Dispose(true); } catch { /* cleanup は best effort */ }
            // OpenCallback.Streams へ追加する前に失敗した場合、src の所有権は
            // まだ _disposable 系列に移っていないためここで直接閉じる。
            if (dispose && !owned) { try { src.Dispose(); } catch { /* best effort */ } }
            GC.SuppressFinalize(this);
            throw;
        }
    }

    #endregion

    #region Events

    /* --------------------------------------------------------------------- */
    ///
    /// FileExtracting
    ///
    /// <summary>
    /// 各ファイルの展開開始時に発火するイベント。
    /// </summary>
    /// <remarks>
    /// イベント引数の <see cref="ArchiveFileEventArgs.Cancel"/> を true にすると、
    /// そのファイル以降の処理がキャンセルされる。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    public event EventHandler<ArchiveFileEventArgs> FileExtracting;

    /* --------------------------------------------------------------------- */
    ///
    /// FileExtracted
    ///
    /// <summary>
    /// 各ファイルの展開終了時に発火するイベント（成功・失敗問わず）。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public event EventHandler<ArchiveFileEventArgs> FileExtracted;

    #endregion

    #region Properties

    /* --------------------------------------------------------------------- */
    ///
    /// Source
    ///
    /// <summary>
    /// Gets the archive path. Stream ベースで開いた場合は空文字を返す。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public string Source { get; }

    /* --------------------------------------------------------------------- */
    ///
    /// Format
    ///
    /// <summary>
    /// Gets the archive format.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public Format Format { get; }

    /* --------------------------------------------------------------------- */
    ///
    /// Items
    ///
    /// <summary>
    /// Gets the collection of archived items.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public IReadOnlyList<ArchiveEntity> Items { get; }

    /* --------------------------------------------------------------------- */
    ///
    /// Options
    ///
    /// <summary>
    /// Gets the options to extract the provided archive.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveOption Options { get; }

    #endregion

    #region Methods

    /* --------------------------------------------------------------------- */
    ///
    /// Save
    ///
    /// <summary>
    /// Extracts all files and saves them in the specified directory.
    /// </summary>
    ///
    /// <param name="dest">
    /// Path of the directory to save. If the parameter is set to null
    /// or empty, the method invokes as a test mode.
    /// </param>
    ///
    /* --------------------------------------------------------------------- */
    public void Save(string dest) => Save(dest, null);

    /* --------------------------------------------------------------------- */
    ///
    /// Save
    ///
    /// <summary>
    /// Extracts all files except those matching the specified filter
    /// function and saves them in the specified directory.
    /// </summary>
    ///
    /// <param name="dest">
    /// Path of the directory to save. If the parameter is set to null
    /// or empty, the method invokes as a test mode.
    /// </param>
    ///
    /// <param name="progress">Progress object.</param>
    ///
    /* --------------------------------------------------------------------- */
    public void Save(string dest, IProgress<Report> progress) => Save(dest, null, progress);

    /* --------------------------------------------------------------------- */
    ///
    /// Save
    ///
    /// <summary>
    /// Extracts the files corresponding to the specified indices except
    /// those matching the specified filter function, and saves them
    /// in the specified directory.
    /// </summary>
    ///
    /// <param name="dest">
    /// Path of the directory to save. If the parameter is set to null
    /// or empty, the method invokes as a test mode.
    /// </param>
    /// <param name="src">
    /// Source indices to extract. 順序は問わない（内部で昇順へ正規化し重複を除去する）。
    /// null を指定した場合は全エントリを対象とする。
    /// </param>
    /// <param name="progress">Progress object.</param>
    ///
    /* --------------------------------------------------------------------- */
    public unsafe void Save(string dest, uint[] src, IProgress<Report> progress)
    {
        ThrowIfDisposed();

        // IInArchive::Extract は昇順ソート済みのインデックス配列を要求し、ExtractCallback の
        // エントリ解決も前進専用の列挙で行うため、非昇順や重複を含む配列をそのまま渡すと
        // 対象を追い越して該当エントリが例外なく未展開のまま終わる。呼び出し側の配列を
        // 破壊しないようコピーしてから正規化する。
        var indices = Normalize(src);

        try
        {
            using var cb = CreateCallback(dest, indices, progress, null);
            var n    = (uint?)indices?.Length ?? uint.MaxValue;
            var test = dest.HasValue() ? 0 : 1;

            int code;
            fixed (uint* p = indices)
            {
                code = _core.Extract(p, n, test, cb);
            }
            GC.KeepAlive(cb);

            Logger.Debug($"Code:{code}");
            cb.ThrowIfError(code, checkPassword: true);
        }
        finally { _password.Reset(); }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Extract
    ///
    /// <summary>
    /// 指定したインデックスのエントリを指定 Stream に展開する。
    /// </summary>
    ///
    /// <param name="index">アーカイブ内のエントリインデックス。</param>
    /// <param name="output">展開結果の書き込み先 Stream（呼び出し側所有）。</param>
    /// <param name="progress">進捗を報告するオブジェクト。</param>
    ///
    /* --------------------------------------------------------------------- */
    public void Extract(int index, Stream output, IProgress<Report> progress = null)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        Extract(new Dictionary<int, Stream> { { index, output } }, progress);
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Extract
    ///
    /// <summary>
    /// 指定したインデックスのエントリを対応する Stream に展開する（複数同時展開）。
    /// </summary>
    ///
    /// <param name="outputs">インデックス → 書き込み先 Stream のマップ。</param>
    /// <param name="progress">進捗を報告するオブジェクト。</param>
    ///
    /* --------------------------------------------------------------------- */
    public unsafe void Extract(IReadOnlyDictionary<int, Stream> outputs, IProgress<Report> progress = null)
    {
        ThrowIfDisposed();
        if (outputs is null) throw new ArgumentNullException(nameof(outputs));
        if (outputs.Count == 0) return;

        // 展開対象の 7-zip インデックス配列を構築する（uint へ変換）。
        // IInArchive::Extract は昇順ソート済みのインデックス配列を要求し、ExtractCallback の
        // エントリ解決も前進専用の列挙で行うため、非昇順のまま渡すと対象を追い越して
        // 該当エントリが例外なく未展開のまま終わる。Dictionary のキー列挙順は仕様上不定なので
        // ここで必ず昇順へ正規化する。
        var indices = new uint[outputs.Count];
        var i = 0;
        foreach (var key in outputs.Keys) indices[i++] = (uint)key;
        Array.Sort(indices);

        try
        {
            using var cb = CreateCallback(string.Empty, indices, progress, outputs);
            var n = (uint)indices.Length;

            int code;
            fixed (uint* p = indices)
            {
                code = _core.Extract(p, n, 0, cb);
            }
            GC.KeepAlive(cb);

            Logger.Debug($"Code:{code}");
            cb.ThrowIfError(code, checkPassword: true);
        }
        finally { _password.Reset(); }
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Dispose
    ///
    /// <summary>
    /// Releases the unmanaged resources used by the ArchiveReader
    /// and optionally releases the managed resources.
    /// </summary>
    ///
    /// <param name="disposing">
    /// true to release both managed and unmanaged resources;
    /// false to release only unmanaged resources.
    /// </param>
    ///
    /* --------------------------------------------------------------------- */
    protected override void Dispose(bool disposing)
    {
        // パスワードキャッシュをクリアしてヒープ上の平文を除去
        try { _password?.Reset(); } catch { /* reset 失敗は無視 */ }

        // finalizer 経路 (disposing == false) では他のマネージドオブジェクトに触らない。
        // _core (source-generated ComObject)・Items・_disposable 内のオブジェクトはそれぞれ
        // 自身が finalizable で、finalize 順序は不定。先に finalize 済みの ComObject の
        // メソッド (Close 等) を呼ぶと ObjectDisposedException が finalizer スレッドで
        // 発生し、プロセスごとクラッシュする (.NET の finalizer 未処理例外は致死)。
        // ネイティブ側の解放は各 ComObject 自身の finalizer が担うため、ここでは参照を
        // 切るだけでよい。
        if (!disposing)
        {
            // ただし SevenZipLibrary の参照カウントだけは戻す。ここを省くと Dispose 漏れ
            // 1 回で参照カウントが永久に 0 へ戻らず、以降に正しく Dispose された全インスタンスの
            // 解放まで無効化される。ReleaseFromFinalizer は FinalRelease と DLL アンロードを
            // 行わないため finalizer スレッドでも安全 (詳細は同メソッドの remarks)。
            // 警告ログを含め全体を保護する (finalizer スレッドの未処理例外は致死)。
            SevenZipLibrary.ReleaseFromFinalizerSafe(_lib, nameof(ArchiveReader));

            _lib          = null;
            _core         = null;
            _openStream   = null;
            _openCallback = null;
            return;
        }

        // Items (ArchiveCollection) を先に Dispose して内部 _core 参照を null 化する。
        // これで Dispose 後に Items[i] がアクセスされても解放済み COM に触らない
        // (ArchiveCollection.Dispose が _core = null を行う)。
        (Items as IDisposable)?.Dispose();

        if (_core != null)
        {
            // Open に失敗した直後の Close は失敗しうるが、COM ラッパーと
            // ライブラリ参照カウントの解放は続行する (ctor 失敗クリーンアップ経路)。
            try { _core.Close(); }
            catch (Exception e) { Logger.Warn($"[Dispose] Close failed: {e.Message}"); }
            // _lib は ctor で Acquire に失敗した場合のみ null (その場合 _core も生成前)
            _lib?.ReleaseComWrapper(_core);
            _core = null;
        }
        _openStream   = null;
        _openCallback = null;
        _disposable.Dispose();
    }

    #endregion

    #region Implementations

    /* --------------------------------------------------------------------- */
    ///
    /// OpenPath
    ///
    /// <summary>
    /// パスを開いて Stream を返す（コンストラクタチェイン用のヘルパー）。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private static Stream OpenPath(string src) => Io.Open(src);

    /* --------------------------------------------------------------------- */
    ///
    /// Hook
    ///
    /// <summary>
    /// Attaches the specified object as disposable.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private T Hook<T>(T src) where T : IDisposable
    {
        _disposable.Add(src);
        return src;
    }

    /* --------------------------------------------------------------------- */
    ///
    /// ThrowIfDisposed
    ///
    /// <summary>
    /// 破棄済みの場合に <see cref="ObjectDisposedException"/> を投げる。
    /// </summary>
    ///
    /// <remarks>
    /// 破棄後は _core が null 化されるため、ガードが無いと原因の分からない
    /// NullReferenceException になる。ArchiveCollection と同じく明示的な例外で早期検出する。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    private void ThrowIfDisposed()
    {
        if (Disposed) throw new ObjectDisposedException(nameof(ArchiveReader));
    }

    /* --------------------------------------------------------------------- */
    ///
    /// Normalize
    ///
    /// <summary>
    /// 展開対象インデックス配列を昇順・重複なしへ正規化した新しい配列を返す。
    /// </summary>
    ///
    /// <remarks>
    /// IInArchive::Extract は昇順ソート済みの配列を要求し、ExtractCallback のエントリ解決も
    /// 前進専用の列挙で行うため、非昇順や重複があると対象を追い越して該当エントリが
    /// 例外なく未展開のまま終わる。呼び出し側の配列は破壊しない。
    /// </remarks>
    ///
    /* --------------------------------------------------------------------- */
    private static uint[] Normalize(uint[] src)
    {
        if (src is null || src.Length < 2) return src;

        var dest = (uint[])src.Clone();
        Array.Sort(dest);

        // 重複を前方へ詰めて切り詰める（ソート済みなので隣接比較で足りる）
        var n = 1;
        for (var i = 1; i < dest.Length; ++i)
        {
            if (dest[i] == dest[n - 1]) continue;
            dest[n++] = dest[i];
        }
        return n == dest.Length ? dest : dest[..n];
    }

    /* --------------------------------------------------------------------- */
    ///
    /// CreateCallback
    ///
    /// <summary>
    /// Creates a new instance of the ExtractCallback class with the
    /// specified arguments.
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private ExtractCallback CreateCallback(string dest, uint[] src, IProgress<Report> progress,
        IReadOnlyDictionary<int, Stream> streamOutputs)
    {
        var e = src is not null ?
                new ArchiveEnumerator(Items, src) :
                new ArchiveEnumerator(Items);

        return new(Source, e, progress)
        {
            Destination    = dest ?? string.Empty,
            Password       = _password,
            Filter         = Options.Filter,
            StreamOutputs  = streamOutputs,
            OnFileStarted  = RaiseFileExtracting,
            OnFileFinished = RaiseFileExtracted,
        };
    }

    /* --------------------------------------------------------------------- */
    ///
    /// RaiseFileExtracting / RaiseFileExtracted
    ///
    /// <summary>
    /// 外部 Action 経由で各イベントを発火するヘルパー。
    /// </summary>
    ///
    /* --------------------------------------------------------------------- */
    private void RaiseFileExtracting(ArchiveFileEventArgs args) =>
        FileExtracting?.Invoke(this, args);

    private void RaiseFileExtracted(ArchiveFileEventArgs args) =>
        FileExtracted?.Invoke(this, args);

    #endregion

    #region Fields
    private IInArchive _core;  // Dispose で null 化するため readonly ではない
    // finalizer 経路で参照カウントだけを戻すために保持する (_disposable にも入っているが、
    // finalizer では _disposable 全体を Dispose できないため個別に参照が必要)。
    private SevenZipLibrary.Lease _lib;
    private readonly PasswordQuery _password;
    private readonly DisposableContainer _disposable = new();
    // Prevent GC from collecting stream/callback whose CCWs are held by native 7-Zip.
    private object _openStream;
    private object _openCallback;
    #endregion
}
