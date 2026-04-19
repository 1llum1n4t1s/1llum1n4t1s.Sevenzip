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
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(Stream src, bool leaveOpen = true) :
        this(src, string.Empty, new(), leaveOpen) { }

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
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(Stream src, string password, bool leaveOpen = true) :
        this(src, password, new(), leaveOpen) { }

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
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(Stream src, string password, ArchiveOption options, bool leaveOpen = true) :
        this(FormatFactory.From(src), src, string.Empty, new(password), options, dispose: !leaveOpen) { }

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
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(Stream src, IQuery<string> password, ArchiveOption options, bool leaveOpen = true) :
        this(FormatFactory.From(src), src, string.Empty, new(password), options, dispose: !leaveOpen) { }

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
    ///
    /* --------------------------------------------------------------------- */
    public ArchiveReader(Format format, Stream src, IQuery<string> password,
        ArchiveOption options, bool leaveOpen = true) :
        this(format, src, string.Empty, new(password), options, dispose: !leaveOpen) { }

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
        if (format == Format.Unknown) throw new UnknownFormatException();

        Source  = sourceHint ?? string.Empty;
        Format  = format;
        Options = options;
        _password = password;
        var lib = Hook(SevenZipLibrary.Acquire());
        _core = lib.GetInArchive(format);

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
                SevenZipLibrary.ReleaseComWrapper(setProps);
            }
        }

        var cb = Hook(new OpenCallback(Source) { Password = _password });
        var ss = new ArchiveStreamReader(src, dispose);
        cb.Streams.Add(ss);

        // Keep managed references alive to prevent GC from collecting
        // objects whose CCWs are held by native 7-Zip.
        _openStream   = ss;
        _openCallback = cb;

        var code = _core.Open(ss, IntPtr.Zero, cb);
        GC.KeepAlive(cb);
        GC.KeepAlive(ss);
        if (code != 0) Logger.Warn($"[Open] Code:{code}");

        Items = new ArchiveCollection(_core, (int)_core.GetNumberOfItems(), Source, format);
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
    /// <param name="src">Source indices to extract.</param>
    /// <param name="progress">Progress object.</param>
    ///
    /* --------------------------------------------------------------------- */
    public unsafe void Save(string dest, uint[] src, IProgress<Report> progress)
    {
        try
        {
            using var cb = CreateCallback(dest, src, progress, null);
            var n    = (uint?)src?.Length ?? uint.MaxValue;
            var test = dest.HasValue() ? 0 : 1;

            int code;
            fixed (uint* p = src)
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
        if (outputs is null) throw new ArgumentNullException(nameof(outputs));
        if (outputs.Count == 0) return;

        // 展開対象の 7-zip インデックス配列を構築する（uint へ変換）
        var indices = new uint[outputs.Count];
        var i = 0;
        foreach (var key in outputs.Keys) indices[i++] = (uint)key;

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
        // P0-7: パスワードキャッシュをクリアしてヒープ上の平文を除去
        try { _password?.Reset(); } catch { /* reset 失敗は無視 */ }

        // P0-4: Items (ArchiveCollection) を先に Dispose して内部 _core 参照を null 化する。
        // これで Dispose 後に Items[i] がアクセスされても解放済み COM に触らない
        // (ArchiveCollection.Dispose が _core = null を行う)。
        (Items as IDisposable)?.Dispose();

        if (_core != null)
        {
            _core.Close();
            SevenZipLibrary.ReleaseComWrapper(_core);
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
    private readonly PasswordQuery _password;
    private readonly DisposableContainer _disposable = new();
    // Prevent GC from collecting stream/callback whose CCWs are held by native 7-Zip.
    private object _openStream;
    private object _openCallback;
    #endregion
}
