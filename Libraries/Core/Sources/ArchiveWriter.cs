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
namespace Cube.FileSystem.SevenZip;

/// <summary>
/// 新しいアーカイブを作成する機能を提供する。
/// </summary>
/// <remarks>
/// 同一スレッドでの生成から破棄まで実行する必要がある（非同期利用は Task.Run で一連の処理を包む）。
/// </remarks>
public sealed class ArchiveWriter : DisposableBase
{
    #region Constructors

    /// <summary>
    /// 指定したフォーマットで ArchiveWriter クラスの新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="format">アーカイブフォーマット。</param>
    public ArchiveWriter(Format format) : this(format, new()) { }

    /// <summary>
    /// 指定したフォーマットとオプションで ArchiveWriter クラスの新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="format">アーカイブフォーマット。</param>
    /// <param name="options">アーカイブ作成オプション。</param>
    public ArchiveWriter(Format format, CompressionOption options)
    {
        Format  = format;
        Options = options;
    }

    #endregion

    #region Properties

    /// <summary>
    /// アーカイブフォーマットを取得する。
    /// </summary>
    public Format Format { get; }

    /// <summary>
    /// アーカイブ作成時のオプションを取得する。
    /// </summary>
    public CompressionOption Options { get; }

    #endregion

    #region Methods

    /// <summary>
    /// 指定したファイルまたはディレクトリをアーカイブに追加する。
    /// </summary>
    /// <param name="src">ファイルまたはディレクトリのパス。</param>
    /// <remarks>
    /// アーカイブ内の相対パスにはファイル名（Io.GetFileName）を使用する。
    /// </remarks>
    public void Add(string src) => Add(src, Io.GetFileName(src));

    /// <summary>
    /// 指定したファイルまたはディレクトリをアーカイブに追加する。
    /// </summary>
    /// <param name="src">ファイルまたはディレクトリのパス。</param>
    /// <param name="name">アーカイブ内の相対パス。</param>
    public void Add(string src, string name)
    {
        var e = new RawEntity(src, name);
        // ファイル/ディレクトリが存在する場合のみ追加する
        if (e.Exists) AddRecursive(e);
        else throw new FileNotFoundException(e.FullName);
    }

    /// <summary>
    /// 追加済みの全ファイルおよびディレクトリをクリアする。
    /// </summary>
    /// <remarks>
    /// Remove で登録した削除対象パスもあわせてクリアする。
    /// </remarks>
    public void Clear()
    {
        _items.Clear();
        _removeNames.Clear();
    }

    /// <summary>
    /// 新しいアーカイブを作成して指定したパスに保存する。
    /// </summary>
    /// <param name="dest">アーカイブの保存先パス。</param>
    public void Save(string dest) => Save(dest, null);

    /// <summary>
    /// 新しいアーカイブを作成して指定したパスに保存する。
    /// </summary>
    /// <param name="dest">アーカイブの保存先パス。</param>
    /// <param name="progress">進捗を報告するオブジェクト。null の場合は報告しない。</param>
    public void Save(string dest, IProgress<Report> progress)
    {
        // フォーマットに応じた保存処理に振り分ける
        if (Format == Format.Tar) SaveAsTar(dest, _items, progress);
        else SaveAs(dest, _items, Format, progress);
    }

    /// <summary>
    /// 既存アーカイブから削除するアイテムの相対パスを登録する。
    /// </summary>
    /// <param name="relativeName">削除対象の相対パス。</param>
    /// <remarks>
    /// Update 呼び出し時に適用される。
    /// ディレクトリを指定した場合はその配下のファイルも全て削除される。
    /// </remarks>
    public void Remove(string relativeName) =>
        // パス区切り文字を正規化し、末尾の '\' を取り除いてディレクトリエントリと一致させる
        _removeNames.Add(relativeName.Replace('/', '\\').TrimEnd('\\'));

    /// <summary>
    /// 既存アーカイブを更新する。
    /// </summary>
    /// <param name="source">既存アーカイブのパス。</param>
    /// <param name="dest">保存先のパス。</param>
    /// <remarks>
    /// Add で追加したアイテムの追加・同名パスの置換、Remove で指定したアイテムの削除を行う。
    /// ソースアーカイブのパスワードは Options.Password を使用する。
    /// </remarks>
    public void Update(string source, string dest) => Update(source, dest, null, null);

    /// <summary>
    /// 既存アーカイブを更新する（進捗報告付き）。
    /// </summary>
    /// <param name="source">既存アーカイブのパス。</param>
    /// <param name="dest">保存先のパス。</param>
    /// <param name="progress">進捗を報告するオブジェクト。</param>
    /// <remarks>
    /// ソースアーカイブのパスワードは Options.Password を使用する。
    /// </remarks>
    public void Update(string source, string dest, IProgress<Report> progress) =>
        Update(source, dest, null, progress);

    /// <summary>
    /// 既存アーカイブを更新する（ソースパスワード指定・進捗報告付き）。
    /// </summary>
    /// <param name="source">既存アーカイブのパス。</param>
    /// <param name="dest">保存先のパス。</param>
    /// <param name="sourcePassword">
    /// ソースアーカイブの読み取りパスワード。
    /// null の場合は Options.Password を使用する。
    /// </param>
    /// <param name="progress">進捗を報告するオブジェクト。</param>
    /// <remarks>
    /// TAR フォーマットはインプレース更新をサポートしないため例外をスローする。
    /// source と dest が同じパスを指す場合は一時ファイルを経由して安全に更新する。
    /// </remarks>
    public void Update(string source, string dest, string sourcePassword, IProgress<Report> progress)
    {
        // 更新非対応フォーマットは早期に例外をスローする
        if (Format == Format.Tar)
            throw new NotSupportedException("Update is not supported for TAR format.");

        // 絶対パスに正規化してから同一ファイルかどうかを判定する
        var srcFull  = Path.GetFullPath(source);
        var destFull = Path.GetFullPath(dest);
        var sameFile = string.Equals(srcFull, destFull, StringComparison.OrdinalIgnoreCase);

        // 同一ファイルの場合は一時ファイルに書き出す（元ファイルを直接上書きできないため）
        var actualDest = sameFile
            ? Path.Combine(Path.GetDirectoryName(destFull), Guid.NewGuid().ToString("N") + Path.GetExtension(destFull))
            : dest;

        // 同一ファイルの場合はロールバック用バックアップパスも生成する
        var backup = sameFile
            ? Path.Combine(Path.GetDirectoryName(destFull), Guid.NewGuid().ToString("N") + ".bak")
            : null;

        try
        {
            // sourcePassword ?? Options.Password: ソース用パスワードが未指定の場合は出力用パスワードで代用する
            UpdateCore(source, actualDest, sourcePassword ?? Options.Password, progress);

            if (sameFile)
            {
                // 同一ファイルの更新: バックアップ → 新ファイル配置 → バックアップ削除 の順で行う
                // 元ファイルをバックアップにリネームする
                Io.Move(source, backup, false);
                try
                {
                    // 新しく生成した一時ファイルを最終的な保存先に移動する
                    Io.Move(actualDest, dest, false);
                }
                catch (Exception ex)
                {
                    // Move 失敗時はバックアップから元のファイルを復元する
                    try { Io.Move(backup, source, false); }
                    catch (Exception restoreEx)
                    {
                        // ロールバックも失敗した場合: バックアップのパスを通知して呼び出し元が対処できるようにする
                        throw new IOException(
                            $"Failed to restore original archive. Backup is at: {backup}",
                            new AggregateException(ex, restoreEx));
                    }
                    throw; // Move 失敗の元例外を再スローする
                }
                // 正常完了後はバックアップを削除する（失敗しても無視する）
                Logger.Try(() => Io.Delete(backup));
            }
        }
        catch
        {
            // 異常終了時は生成した一時ファイルを削除してクリーンアップする
            if (sameFile) Logger.Try(() => Io.Delete(actualDest));
            throw;
        }
    }

    /// <summary>
    /// オブジェクトが使用するリソースを解放する。
    /// </summary>
    /// <param name="disposing">
    /// マネージドリソースとアンマネージドリソースの両方を解放する場合は true；
    /// アンマネージドリソースのみを解放する場合は false。
    /// </param>
    protected override void Dispose(bool disposing) => _lib.Dispose();

    #endregion

    #region Implementations

    /// <summary>
    /// 既存アーカイブを開き、UpdatePlan に基づいて更新を実行する。
    /// </summary>
    private void UpdateCore(string source, string dest, string sourcePassword, IProgress<Report> progress)
    {
        // 保存先ディレクトリを事前に作成する
        var dir = Io.GetDirectoryName(dest);
        Io.CreateDirectory(dir);

        // COM オブジェクトを後で確実に解放するために変数を宣言しておく
        var inArchive = _lib.GetInArchive(Format);
        object outArchive = null;
        object setProps = null;
        ArchiveStreamReader inStream = null;
        OpenCallback openCb = null;

        try
        {
            // ソースアーカイブをストリームとして開く
            inStream = new ArchiveStreamReader(Io.Open(source));
            // パスワードコールバックを生成する（暗号化アーカイブの読み取り用）
            openCb   = new OpenCallback(source) { Password = new PasswordQuery(sourcePassword) };
            var openCode = inArchive.Open(inStream, IntPtr.Zero, openCb);
            if (openCode != 0) throw new IOException($"Failed to open archive: {source} (code={openCode})");

            var existingCount = inArchive.GetNumberOfItems();

            // 削除対象の既存インデックスを収集する（ディレクトリ指定時は子孫エントリも含む）
            HashSet<uint> removeSet = null;
            if (_removeNames.Count > 0)
            {
                removeSet = new HashSet<uint>();
                for (uint i = 0; i < existingCount; i++)
                {
                    // GetString を使って BSTR バッファを確実に解放する
                    var path = inArchive.GetString((int)i, ItemPropId.Path)?.Replace('/', '\\');
                    if (path is null) continue;

                    // パス完全一致で削除対象に含める
                    if (_removeNames.Contains(path)) { removeSet.Add(i); continue; }

                    // ディレクトリ指定の場合、プレフィックスが一致する子孫エントリも削除する
                    foreach (var name in _removeNames)
                    {
                        // ディレクトリ区切り文字で終わっていない場合は '\' を追加してプレフィックスとして使う
                        var prefix = name.EndsWith('\\') || name.EndsWith('/')
                            ? name : name + "\\";
                        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            removeSet.Add(i);
                            break;
                        }
                    }
                }
            }

            // 既存アイテム・新規アイテム・削除セットから UpdatePlan を生成する
            var plan = new UpdatePlan(
                existingCount,
                // GetString を使って各インデックスのパスを取得する
                idx => inArchive.GetString((int)idx, ItemPropId.Path) ?? string.Empty,
                _items,
                removeSet
            );

            // 既に開いている IInArchive から QueryInterface で IOutArchive を取得する。
            // GetOutArchive() を使うと別のアーカイブハンドラが生成されてしまい、
            // 保持エントリのデータコピーができなくなるため、QueryInterface が必須。
            var outArc = _lib.QueryInterface<IOutArchive>(inArchive);
            outArchive = outArc;
            if (outArc is null) throw new InvalidOperationException("Failed to obtain IOutArchive from the opened archive.");

            // 圧縮オプション（レベル、メソッドなど）を設定する
            var setter = CompressionOptionSetter.From(Format, Options);
            var props = _lib.QueryInterface<ISetProperties>(outArc);
            setProps = props;
            setter?.Invoke(props);

            // 保持アイテムの合計バイト数を計算する（進捗報告の分母に含めるため）
            var existingBytes = 0L;
            foreach (var entry in plan.Entries)
            {
                if (!entry.IsNewOrReplaced)
                    existingBytes += (long)inArchive.GetUInt64((int)entry.OriginalIndex, ItemPropId.Size);
            }

            // UpdateCallback を更新モードで生成する
            using var cb = new UpdateCallback(_items, plan, existingBytes, progress)
            {
                Destination = dest,
                Password    = Options.Password,
            };

            // 出力ストリームを生成して UpdateItems を呼び出す
            using var outStream = new ArchiveStreamWriter(Io.Create(dest));
            var code = outArc.UpdateItems(outStream, (uint)plan.TotalCount, cb);

            // GC が inStream / openCb を UpdateItems 完了前に回収しないよう保持する
            GC.KeepAlive(cb);
            GC.KeepAlive(inStream);
            GC.KeepAlive(openCb);

            // エラーコードを確認して例外をスローする（キャンセルや圧縮エラーなど）
            cb.ThrowIfError(code);
        }
        finally
        {
            // COM オブジェクトを DLL アンロード前に確実に解放する
            SevenZipLibrary.ReleaseComWrapper(setProps);
            SevenZipLibrary.ReleaseComWrapper(outArchive);
            if (inArchive is not null)
            {
                inArchive.Close(); // アーカイブを閉じてからラッパーを解放する
                SevenZipLibrary.ReleaseComWrapper(inArchive);
            }
            inStream?.Dispose();
            openCb?.Dispose();
        }
    }

    /// <summary>
    /// 新しいアーカイブを作成して指定したパスに保存する。
    /// </summary>
    private void SaveAs(string dest, IList<RawEntity> src, Format fmt, IProgress<Report> progress)
    {
        // 保存先ディレクトリを事前に作成する
        var dir = Io.GetDirectoryName(dest);
        Io.CreateDirectory(dir);

        Invoke(cb =>
        {
            using var ss = new ArchiveStreamWriter(Io.Create(dest));
            // 新規アーカイブハンドラを取得する
            var archive = _lib.GetOutArchive(fmt);
            // 圧縮オプションを設定する
            var setter = CompressionOptionSetter.From(fmt, Options);
            var setProps = _lib.QueryInterface<ISetProperties>(archive);
            setter?.Invoke(setProps);
            try
            {
                return archive.UpdateItems(ss, (uint)src.Count, cb);
            }
            finally
            {
                // COM オブジェクトを DLL アンロード前に解放する
                SevenZipLibrary.ReleaseComWrapper(setProps);
                SevenZipLibrary.ReleaseComWrapper(archive);
            }
        }, src, dest, progress);
    }

    /// <summary>
    /// 新しい TAR アーカイブを作成して指定したパスに保存する。
    /// </summary>
    private void SaveAsTar(string dest, IList<RawEntity> src, IProgress<Report> progress)
    {
        // 一時ディレクトリに TAR を作成し、必要に応じて圧縮する
        var dir = Io.Combine(Io.GetDirectoryName(dest), Guid.NewGuid().ToString("N"));
        var tmp = Io.Combine(dir, GetTarName(dest));

        try
        {
            // まず TAR フォーマットで中間ファイルを生成する
            SaveAs(tmp, src, Format.Tar, progress);

            var m = Options.CompressionMethod;
            if (m == CompressionMethod.BZip2 || m == CompressionMethod.GZip || m == CompressionMethod.XZ)
            {
                // BZip2/GZip/XZ の場合は TAR を圧縮して最終出力を生成する
                var f = new List<RawEntity> { new(tmp, Io.GetFileName(tmp)) };
                SaveAs(dest, f, m.ToFormat(), progress);
            }
            else
            {
                // 圧縮なしの場合は TAR ファイルをそのまま移動する
                Io.Move(tmp, dest, true);
            }
        }
        finally
        {
            // 一時ディレクトリを削除する（失敗しても無視する）
            Logger.Try(() => Io.Delete(dir));
        }
    }

    /// <summary>
    /// 指定した情報から TAR アーカイブのファイル名を取得する。
    /// </summary>
    private static string GetTarName(string src)
    {
        var name = Io.GetBaseName(src);
        // 既に .tar で終わっている場合はそのまま使用する
        return name.EndsWith(".tar", StringComparison.InvariantCultureIgnoreCase) ? name : $"{name}.tar";
    }

    /// <summary>
    /// 指定したファイルまたはディレクトリをアーカイブに追加する。
    /// ディレクトリの場合は再帰的に内包するファイル/ディレクトリを追加する。
    /// </summary>
    private void AddRecursive(RawEntity src)
    {
        Logger.Trace($"[Add] {src.RawName.Quote()}");

        // フィルタ関数が true を返した場合はこのアイテムとその子孫をスキップする
        if (Options.Filter?.Invoke(src) ?? false) return;

        AddItem(src);
        // ファイルの場合は再帰不要
        if (!src.IsDirectory) return;

        // 相対パスを引き継いだ子エンティティを生成するローカル関数
        static RawEntity make(string s, RawEntity e) =>
            new(s, Io.Combine(e.RelativeName, Io.GetFileName(s)));

        // ディレクトリ直下のファイルを追加する
        foreach (var e in Io.GetFiles(src.FullName))
        {
            var entity = make(e, src);
            // フィルタに一致したファイルはスキップする
            if (Options.Filter?.Invoke(entity) ?? false) continue;
            AddItem(entity);
        }

        // サブディレクトリを再帰的に処理する
        foreach (var e in Io.GetDirectories(src.FullName)) AddRecursive(make(e, src));
    }

    /// <summary>
    /// 指定したファイルまたはディレクトリをアイテムリストに追加する。
    /// </summary>
    private void AddItem(RawEntity src)
    {
        try
        {
            if (!src.IsDirectory)
            {
                // ファイルが読み取り可能かどうかを事前に確認する（フェイルファスト）
                // アーカイブ作成時ではなく追加時にエラーを検出することで早期通知できる
                try
                {
                    using var stream = Io.Open(src.FullName);
                    if (stream is null) throw new ArgumentNullException(nameof(stream));
                }
                catch (IOException ex) when (IsFileLocked(ex))
                {
                    // ロック中 → FileShare.ReadWrite で読めることだけ確認する
                    // 実際のコピーは Save() 時に UpdateCallback.Open() 内で行う
                    using var stream = Io.Open(src.FullName,
                        FileShare.ReadWrite | FileShare.Delete);
                }
            }
            _items.Add(src);
        }
        catch (Exception e)
        {
            // アクセスエラーをログに記録してラップした例外をスローする
            Logger.Debug($"Path:{src.FullName.Quote()}, Error:{e.Message} ({e.GetType().Name})");
            throw new AccessException(src.RawName, e);
        }
    }

    /// <summary>
    /// 例外がファイルロック（共有違反）によるものかを判定する。
    /// </summary>
    private static bool IsFileLocked(IOException ex)
    {
        const int SharingViolation = unchecked((int)0x80070020);
        const int LockViolation = unchecked((int)0x80070021);
        return ex.HResult == SharingViolation || ex.HResult == LockViolation;
    }

    /// <summary>
    /// UpdateCallback のインスタンスを生成して指定したコールバック関数を実行する。
    /// </summary>
    private void Invoke(Func<UpdateCallback, int> func,
        IList<RawEntity> src, string dest, IProgress<Report> progress)
    {
        // 新規作成モードの UpdateCallback を生成する
        using var cb = new UpdateCallback(src, progress)
        {
            Destination = dest,
            Password    = Options.Password,
        };

        var code = func(cb);

        // GC が UpdateItems 完了前にコールバックを回収しないよう保持する
        GC.KeepAlive(cb);

        // エラーコードを確認して例外をスローする
        cb.ThrowIfError(code);
    }

    #endregion

    #region Fields
    // 7z.dll のラッパー（参照カウント付き共有シングルトン）
    private readonly SevenZipLibrary _lib = SevenZipLibrary.Acquire();
    // 追加するファイル/ディレクトリのリスト
    private readonly List<RawEntity> _items = [];
    // Update 時に削除するアイテムの相対パスセット（OrdinalIgnoreCase）
    private readonly HashSet<string> _removeNames = new(StringComparer.OrdinalIgnoreCase);
    #endregion
}
