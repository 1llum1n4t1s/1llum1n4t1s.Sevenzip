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
/// <para>
/// 同一スレッドでの生成から破棄まで実行する必要がある（非同期利用は Task.Run で一連の処理を包む）。
/// </para>
/// <para>
/// <b>スケーラビリティ注意</b>: 非常に大量のエントリ (10 万件超) を圧縮する場合、
/// 7z.dll 内部のマルチスレッド圧縮が並行的に <c>GetStream</c> を呼ぶため、
/// 同時オープンしている <see cref="System.IO.FileStream"/> ハンドル数が瞬間的に
/// 数百〜数千に達する可能性がある。Windows のデフォルトプロセスハンドル上限
/// (16,384) に近づく場合は <c>ThreadCount</c> を明示的に小さく設定するか、
/// アーカイブを複数に分割することを検討する。
/// </para>
/// <para>
/// <b>並列実行注意</b>: 同一プロセス内で複数の <see cref="ArchiveWriter"/> を
/// 同時に動作させることは現状サポートしていない (<c>SevenZipLibrary</c> の共有参照カウンタ
/// と COM オブジェクト追跡が競合する可能性)。サーバーサイドでは 1 度に 1 つの
/// 圧縮のみ実行するよう直列化すること。
/// </para>
/// <para>
/// <b>パスワード変更 + rename 併用の注意</b>:
/// <see cref="Update(System.IO.Stream, System.IO.Stream, System.Collections.Generic.IReadOnlyDictionary{int, string}, string, System.IProgress{Report}, bool, bool)"/>
/// で rename エントリを指定した場合、既存エントリのデータがそのまま再利用される (再圧縮なし)。
/// このとき <c>Options.Password</c> を変更しても、rename エントリ/保持エントリは
/// <b>元のパスワードで暗号化されたまま</b>保持される。新規追加エントリは新しいパスワードで
/// 暗号化されるため、結果としてハイブリッド暗号化アーカイブが生成される点に注意。
/// 意図しないハイブリッド化を避けたい場合は、全エントリを <see cref="Add(string, string)"/>
/// で明示的に再追加する (newdata=1 フラグで再圧縮されパスワードも統一される)。
/// </para>
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

    #region Events

    /// <summary>
    /// 各ファイルの圧縮開始時に発火するイベント。
    /// </summary>
    /// <remarks>
    /// イベント引数の <see cref="ArchiveFileEventArgs.Cancel"/> を true にすると、
    /// そのファイル以降の処理がキャンセルされる。
    /// </remarks>
    public event EventHandler<ArchiveFileEventArgs> FileCompressing;

    /// <summary>
    /// 各ファイルの圧縮終了時に発火するイベント（成功・失敗問わず）。
    /// </summary>
    public event EventHandler<ArchiveFileEventArgs> FileCompressed;

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

    /// <summary>
    /// 直近の <see cref="Save(string)"/> / <see cref="Update(string, string)"/> で生成された
    /// バックアップファイル (.bak) のパスを取得する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="CompressionOption.KeepBackupOnUpdate"/> が <c>true</c> の場合のみ値が設定される。
    /// <c>false</c> または .bak が生成されなかった場合 (新規 Save 等) は null。
    /// </para>
    /// <para>
    /// 呼び出し側はこのパスのファイルを任意のタイミングで削除する責任を持つ。
    /// 複数回 Save / Update を呼ぶと値は最後の操作のもので上書きされる。
    /// </para>
    /// </remarks>
    public string LastBackupPath { get; private set; }

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
        if (name is null) throw new ArgumentNullException(nameof(name));
        // アーカイブ内の相対パスをサニタイズ (Zip Slip 生成側対策)
        var safe = SanitizeRelativeName(name);
        if (!safe.HasValue())
            throw new ArgumentException(
                $"name '{name}' resolved to empty after sanitization.", nameof(name));

        var e = new RawEntity(src, safe);
        // ファイル/ディレクトリが存在する場合のみ追加する
        if (e.Exists) AddRecursive(e);
        else throw new FileNotFoundException(e.FullName);
    }

    /// <summary>
    /// 指定した Stream をアーカイブエントリとして追加する（一時ファイル経由）。
    /// </summary>
    /// <param name="src">読み取り可能な Stream。呼び出し時点で中身を読み切って一時ファイルに退避する。</param>
    /// <param name="name">アーカイブ内の相対パス（ファイル名）。</param>
    /// <remarks>
    /// 現在の実装は Stream を一時ファイル (%TEMP%) にコピーし、通常の path ベース追加として扱う。
    /// Save/Update 完了後の Dispose 時に一時ファイルは削除される。
    /// src は呼び出し後に呼び出し側が自由に Dispose してよい。
    /// </remarks>
    public void Add(Stream src, string name)
    {
        if (src is null) throw new ArgumentNullException(nameof(src));
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("name is required", nameof(name));

        // 共通サニタイズ (Zip Slip 生成側対策)
        var safe = SanitizeRelativeName(name);
        if (!safe.HasValue())
            throw new ArgumentException(
                $"name '{name}' resolved to empty after sanitization.", nameof(name));

        // 一時ディレクトリ遅延作成
        if (_streamTempDir is null)
        {
            _streamTempDir = Path.Combine(Path.GetTempPath(), $"SevenZipStream_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_streamTempDir);
        }

        // 一時ファイルに Stream の内容をコピーする (ファイル名は GetFileName で衝突回避)
        var tempFile = Path.Combine(_streamTempDir,
            $"{Guid.NewGuid():N}_{Io.GetFileName(safe)}");
        // 共通バッファサイズで write syscall を削減
        using (var dst = new FileStream(tempFile, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: FileSystemHelper.DefaultBufferSize))
        {
            src.CopyTo(dst, bufferSize: FileSystemHelper.DefaultBufferSize);
        }

        // path ベース追加として登録する（アーカイブ内の相対パスはサニタイズ済みの safe を使用）
        var e = new RawEntity(tempFile, safe);
        if (e.Exists) _items.Add(e);
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
        // VolumeSize 負値ガード (無限ループ回避)
        if (Options.VolumeSize < 0)
            throw new ArgumentOutOfRangeException(
                nameof(CompressionOption.VolumeSize),
                Options.VolumeSize,
                "VolumeSize must be zero (no split) or positive.");

        // 保存先ディレクトリを事前に作成する
        var dir = Io.GetDirectoryName(dest);
        Io.CreateDirectory(dir);

        // AtomicSave モード: tmp file → atomic rename で書き出す (VolumeSize=0 時のみ)
        if (Options.AtomicSave && Options.VolumeSize == 0)
        {
            SaveAtomic(dest, progress);
            return;
        }

        // フォーマットに応じた保存処理に振り分ける
        if (Format == Format.Tar)
        {
            if (Options.VolumeSize > 0)
                Logger.Warn("[Save] VolumeSize is ignored for Format.Tar");
            SaveAsTar(dest, FilterItems(_items), progress);
            if (Options.FlushToDisk) FileSystemHelper.FlushFile(dest);
            return;
        }

        // VolumeSize 指定時: 7z.dll の UpdateItems は Seek を要求するため、
        // 一度 temp file にフル アーカイブを作成してから VolumeSize ごとに分割する
        // (post-process split)。これで 7z.dll 側の制約を回避しつつ、
        // 呼び出し側には dest.001 / dest.002 ... を提供できる。
        // 注意: この実装は一時ファイル書き込み + 分割コピーの 2 回 I/O が
        // 発生する。大型アーカイブ (数 GB 以上) では TEMP 空き容量とディスク I/O に注意。
        if (Options.VolumeSize > 0)
        {
            // Format.Zip のボリューム分割は本来 `.z01/.z02/.zip` 形式だが、
            // 本実装は単純なバイナリ分割 (`.001/.002`) なので他ツール (WinZip / macOS Finder)
            // では結合後でないと認識できない。呼び出し側に互換性の注意を明示する。
            if (Format == Format.Zip)
                Logger.Warn(
                    "[Save] VolumeSize with Format.Zip produces binary-split (.001/.002) files, " +
                    "not ZIP-native multi-volume (.z01/.z02/.zip). Other tools may not recognize them.");
            if (Options.AtomicSave)
                Logger.Warn("[Save] AtomicSave is ignored when VolumeSize > 0.");

            var tempPath = Path.Combine(Path.GetTempPath(),
                $"SevenZipVol_{Guid.NewGuid():N}.tmp");
            try
            {
                using (var ss = new ArchiveStreamWriter(Io.Create(tempPath)))
                {
                    SaveAs(ss, FilterItems(_items), Format, dest, progress);
                }
                SplitFileIntoVolumes(tempPath, dest, Options.VolumeSize);
            }
            finally { Logger.Try(() => Io.Delete(tempPath)); }
            return;
        }

        // 通常モード: dest を直接開いて 7z.dll に渡す
        using (var single = new ArchiveStreamWriter(Io.Create(dest)))
        {
            SaveAs(single, FilterItems(_items), Format, dest, progress);
        }
        if (Options.FlushToDisk) FileSystemHelper.FlushFile(dest);
    }

    /// <summary>
    /// AtomicSave モード: tmp file → atomic rename で dest を書き出す。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 書き込み途中のクラッシュで元 dest が破損しないことを保証する。
    /// dest と同一ディレクトリに <c>{GUID}.ext</c> tmp を作成 → 書き込み完了 →
    /// 既存 dest があれば <c>{GUID}.bak</c> に退避 → tmp を dest へリネームの順。
    /// </para>
    /// <para>
    /// FlushToDisk=true の場合、tmp 書き込み直後に <c>FlushFileBuffers</c> を呼んでから rename する。
    /// </para>
    /// </remarks>
    private void SaveAtomic(string dest, IProgress<Report> progress)
    {
        var destDir  = Io.GetDirectoryName(dest) ?? ".";
        var destExt  = Path.GetExtension(dest);
        var tempPath = Path.Combine(destDir, $"{Guid.NewGuid():N}{destExt}.tmp");
        var backup   = Io.Exists(dest)
            ? Path.Combine(destDir, $"{Guid.NewGuid():N}.bak")
            : null;

        // LastBackupPath はこのメソッドが完全成功した場合のみ有効。開始時にクリア。
        LastBackupPath = null;

        try
        {
            // Step 1: tmp に全内容を書き込む
            if (Format == Format.Tar)
            {
                SaveAsTar(tempPath, FilterItems(_items), progress);
            }
            else
            {
                using var single = new ArchiveStreamWriter(Io.Create(tempPath));
                SaveAs(single, FilterItems(_items), Format, tempPath, progress);
            }

            // Step 2: tmp をディスクにフラッシュ (クラッシュ耐性強化)
            if (Options.FlushToDisk) FileSystemHelper.FlushFile(tempPath);

            // Step 3: 既存 dest を .bak に退避 (atomic rename)
            if (backup is not null) Io.Move(dest, backup, false);

            // Step 4: tmp を dest へ移動 (atomic rename)
            try { Io.Move(tempPath, dest, false); }
            catch
            {
                // rename 失敗時は .bak から元ファイルを復元してから再スロー
                if (backup is not null)
                {
                    try { Io.Move(backup, dest, false); }
                    catch (Exception restoreEx)
                    {
                        throw new ArchiveUpdateException(
                            $"Failed to restore original archive. Backup is at: {backup}",
                            originalPath: dest,
                            backupPath:   backup,
                            restoreEx);
                    }
                }
                throw;
            }

            // Step 5: .bak のクリーンアップ (または保持)
            if (backup is not null)
            {
                if (Options.KeepBackupOnUpdate) LastBackupPath = backup;
                else Logger.Try(() => Io.Delete(backup));
            }
        }
        catch
        {
            // tmp が残っていれば削除してクリーンアップする
            Logger.Try(() => Io.Delete(tempPath));
            throw;
        }
    }

    /// <summary>
    /// 新しいアーカイブを作成して指定した Stream に書き込む。
    /// </summary>
    /// <param name="dest">書き込み先 Stream（呼び出し側所有）。シーク可能であることが必要。</param>
    /// <param name="progress">進捗を報告するオブジェクト。</param>
    /// <param name="leaveOpen">true の場合、完了後に <paramref name="dest"/> を Dispose しない。既定値は true。</param>
    /// <remarks>
    /// TAR フォーマットは一時ファイル経由で構築される（中間ストリームはランダムアクセスを要求するため）。
    /// </remarks>
    public void Save(Stream dest, IProgress<Report> progress = null, bool leaveOpen = true)
    {
        if (dest is null) throw new ArgumentNullException(nameof(dest));

        // Stream 版の Save は VolumeSize をサポートしない。
        // 呼び出し側が設定値を設定していた場合は無視するがサイレントだと気づけないため警告。
        if (Options.VolumeSize > 0)
            Logger.Warn("[Save] VolumeSize is not supported for Stream output; the option is ignored.");
        // Stream 版では AtomicSave も無効 (rename 対象のパスが無いため)。
        if (Options.AtomicSave)
            Logger.Warn("[Save] AtomicSave is not supported for Stream output; the option is ignored.");

        if (Format == Format.Tar)
        {
            // TAR は現状 path ベースで一旦ディスクに書いてから Stream にコピーする
            var tempPath = Path.Combine(Path.GetTempPath(),
                Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                SaveAsTar(tempPath, FilterItems(_items), progress);
                using var fs = Io.Open(tempPath);
                // 大型アーカイブを効率的に書き戻すため 1MB バッファで CopyTo
                fs.CopyTo(dest, bufferSize: FileSystemHelper.DefaultBufferSize);
            }
            finally { Logger.Try(() => Io.Delete(tempPath)); }
        }
        else
        {
            using var ss = new ArchiveStreamWriter(dest, dispose: !leaveOpen);
            SaveAs(ss, FilterItems(_items), Format, string.Empty, progress);
        }

        // FlushToDisk: 渡された dest が FileStream 等であればディスク同期する。
        // それ以外 (MemoryStream / NetworkStream 等) は通常の Flush のみ。
        if (Options.FlushToDisk) FileSystemHelper.FlushToDiskIfFileStream(dest);
    }

    /// <summary>
    /// 既存アーカイブから削除するアイテムの相対パスを登録する。
    /// </summary>
    /// <param name="relativeName">削除対象の相対パス。</param>
    /// <remarks>
    /// Update 呼び出し時に適用される。
    /// ディレクトリを指定した場合はその配下のファイルも全て削除される。
    /// </remarks>
    public void Remove(string relativeName)
    {
        if (relativeName is null) throw new ArgumentNullException(nameof(relativeName));
        // 空文字列 / 空白のみを拒否 (プレフィックスが "\\" になり全エントリ削除を誘発するため)
        var normalized = relativeName.Replace('/', '\\').TrimEnd('\\');
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException(
                "relativeName resolved to empty after normalization.", nameof(relativeName));
        _removeNames.Add(normalized);
    }

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
    /// null の場合は <see cref="CompressionOption.Password"/> を**ソース解凍にも流用**する。
    /// 入力と出力で異なるパスワードを使いたい場合は、必ず明示的に sourcePassword を渡すこと。
    /// 入力が平文アーカイブの場合は sourcePassword は無視される (7z.dll はパスワード要求しない)。
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

        // LastBackupPath は正常完了時のみ設定する。開始時にクリア。
        LastBackupPath = null;

        try
        {
            // 保存先ディレクトリを事前に作成する
            var dir = Io.GetDirectoryName(actualDest);
            Io.CreateDirectory(dir);

            // using var にすると outStream が開いたまま Io.Move(actualDest, dest) が
            // 走るため、Windows 環境ではウイルスチェッカー等の介入で Move が失敗しうる。
            // UpdateCore 完了後に明示的に Dispose してから Move を実行する。
            var inStream  = new ArchiveStreamReader(Io.Open(source));
            var outStream = new ArchiveStreamWriter(Io.Create(actualDest));
            try
            {
                // sourcePassword ?? Options.Password: ソース用パスワードが未指定の場合は出力用パスワードで代用する
                UpdateCore(inStream, outStream, source, actualDest, sourcePassword ?? Options.Password, progress);
            }
            finally
            {
                // 順序重要: outStream を先に Dispose して Move 可能な状態にする
                outStream.Dispose();
                inStream.Dispose();
            }

            // 書き込み完了後にディスクフラッシュ (sameFile でも非 sameFile でも実施)。
            // rename 前に tmp ファイルの内容がディスクメディアに到達していることを保証する。
            if (Options.FlushToDisk) FileSystemHelper.FlushFile(actualDest);

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
                        // ロールバック失敗時は ArchiveUpdateException 構造化例外で通知。
                        // BackupPath / OriginalPath プロパティから呼び出し側が手動復旧できる。
                        throw new ArchiveUpdateException(
                            $"Failed to restore original archive. Backup is at: {backup}",
                            originalPath: source,
                            backupPath:   backup,
                            new AggregateException(ex, restoreEx));
                    }
                    throw; // Move 失敗の元例外を再スローする
                }
                // KeepBackupOnUpdate の場合はバックアップを保持し LastBackupPath で公開する。
                // それ以外は従来通り削除する (失敗しても無視)。
                if (Options.KeepBackupOnUpdate) LastBackupPath = backup;
                else Logger.Try(() => Io.Delete(backup));
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
    /// 既存アーカイブを Stream ベースで更新する。
    /// </summary>
    /// <param name="source">既存アーカイブを読み取る Stream（シーク可能）。</param>
    /// <param name="dest">更新後アーカイブを書き込む Stream（シーク可能）。</param>
    /// <param name="sourcePassword">ソースアーカイブのパスワード。null の場合は Options.Password を使用する。</param>
    /// <param name="progress">進捗を報告するオブジェクト。</param>
    /// <param name="leaveSourceOpen">true の場合、完了後に source を Dispose しない。既定値は true。</param>
    /// <param name="leaveDestOpen">true の場合、完了後に dest を Dispose しない。既定値は true。</param>
    /// <remarks>
    /// source と dest に同じ Stream インスタンスを渡した場合、一度 MemoryStream にコピーしてから
    /// 更新処理を行い、結果を元の Stream に書き戻す（大型アーカイブではメモリ消費に注意）。
    /// </remarks>
    public void Update(Stream source, Stream dest, string sourcePassword = null,
        IProgress<Report> progress = null, bool leaveSourceOpen = true, bool leaveDestOpen = true) =>
        Update(source, dest, renameMap: null, sourcePassword, progress, leaveSourceOpen, leaveDestOpen);


    /// <summary>
    /// 既存アーカイブを Stream ベースで更新し、保持エントリに対して rename を適用する。
    /// </summary>
    /// <param name="source">既存アーカイブを読み取る Stream（シーク可能）。</param>
    /// <param name="dest">更新後アーカイブを書き込む Stream（シーク可能）。</param>
    /// <param name="renameMap">
    /// 既存エントリのインデックス (<see cref="ArchiveEntity.Index"/>) をキーとした、新しい相対パスのマップ。
    /// 値が null または空文字列のエントリは削除扱いとなる（<see cref="Remove(string)"/> と同じ効果）。
    /// </param>
    /// <param name="sourcePassword">ソースアーカイブのパスワード。null の場合は Options.Password を使用する。</param>
    /// <param name="progress">進捗を報告するオブジェクト。</param>
    /// <param name="leaveSourceOpen">true の場合、完了後に source を Dispose しない。既定値は true。</param>
    /// <param name="leaveDestOpen">true の場合、完了後に dest を Dispose しない。既定値は true。</param>
    /// <remarks>
    /// <para>
    /// rename 処理は既存エントリのデータをそのまま再利用し、パスのみ差し替える（新規圧縮は発生しない）。
    /// 同時に <see cref="Add(string, string)"/> / <see cref="Add(Stream, string)"/> / <see cref="Remove(string)"/>
    /// による操作も適用できる。renameMap と Add に同じパスが現れた場合は Add の内容が優先される
    /// （Add のデータで置換される）。
    /// </para>
    /// <para>
    /// <b>自己参照 (source == dest) の挙動:</b> 一度 MemoryStream に読み出してから書き戻すため、
    /// アーカイブサイズが大きいとメモリ消費が 2 倍になる (元サイズ + 新サイズ)。
    /// 10MB 以上のアーカイブでは path ベース <see cref="Update(string, string, string, IProgress{Report})"/>
    /// の利用を推奨する。
    /// </para>
    /// <para>
    /// <b>OOM 時の dest 保護:</b> 自己参照更新中に <see cref="OutOfMemoryException"/> や
    /// UpdateCore 内部例外が発生した場合、元の dest 内容を維持するために処理は
    /// <b>MemoryStream ローカルバッファの生成完了までは dest に書き込まない</b>。
    /// 書き戻しフェーズ中に例外が起きた場合は dest を長さ 0 に truncate して
    /// 部分書き込みによるサイレント破損を防ぐ (呼び出し側は例外キャッチ時に dest を破棄すること)。
    /// </para>
    /// </remarks>
    public void Update(Stream source, Stream dest,
        IReadOnlyDictionary<int, string> renameMap,
        string sourcePassword = null,
        IProgress<Report> progress = null,
        bool leaveSourceOpen = true, bool leaveDestOpen = true)
    {
        if (Format == Format.Tar)
            throw new NotSupportedException("Update is not supported for TAR format.");
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (dest is null) throw new ArgumentNullException(nameof(dest));

        var sameStream = ReferenceEquals(source, dest);

        // 自己参照時は dest が truncate 可能 (= CanSeek) である必要がある。
        // NetworkStream など非シークの dest に書き戻すと既存内容の末尾に追記されて
        // 旧データ + 新データの不整合アーカイブが生成される (サイレント破損)。
        if (sameStream && !dest.CanSeek)
            throw new InvalidOperationException(
                "The destination stream must be seekable for in-place update " +
                "(source and dest reference the same stream).");

        if (sameStream)
        {
            // 自己参照更新: 一度 MemoryStream に読み出してから書き戻す
            using var copy = new MemoryStream();
            if (source.CanSeek) source.Position = 0L;
            source.CopyTo(copy);
            copy.Position = 0L;

            using var outBuffer = new MemoryStream();
            using (var inStream = new ArchiveStreamReader(copy, dispose: false))
            using (var outStream = new ArchiveStreamWriter(outBuffer, dispose: false))
            {
                // 書き戻しフェーズ前にここで例外が起きた場合、dest は無変更のまま
                // (書き戻し未実施) となる。OOM 耐性: outBuffer が完成するまで dest
                // に触らない設計。
                UpdateCore(inStream, outStream, string.Empty, string.Empty,
                    sourcePassword ?? Options.Password, progress, renameMap);
            }

            // 結果を元の Stream に書き戻す (CanSeek は上記ガードで保証済み)。
            // 書き戻し中 (CopyTo / Flush) の例外は dest を partial 状態で放置しないよう、
            // catch して dest.SetLength(0) で明示的に破棄する。
            try
            {
                dest.Position = 0L;
                dest.SetLength(0L);
                outBuffer.Position = 0L;
                outBuffer.CopyTo(dest);
                dest.Flush();
                // 呼び出し元が読み戻せるよう位置を先頭に戻す
                dest.Position = 0L;
            }
            catch
            {
                // 書き戻し失敗: dest を空にしてサイレント破損を防ぐ。
                // 呼び出し側は例外をキャッチしたら dest を破棄または再生成する責務。
                try { dest.SetLength(0L); dest.Flush(); } catch { /* 最終防御は失敗しても諦める */ }
                throw;
            }
        }
        else
        {
            using var inStream = new ArchiveStreamReader(source, dispose: !leaveSourceOpen);
            using var outStream = new ArchiveStreamWriter(dest, dispose: !leaveDestOpen);
            UpdateCore(inStream, outStream, string.Empty, string.Empty,
                sourcePassword ?? Options.Password, progress, renameMap);
        }

        // FlushToDisk: dest が FileStream ならディスク同期を行う。
        // 自己参照ケースでも通常ケースでも共通で適用 (書き戻し完了後の最終フラッシュ)。
        if (Options.FlushToDisk) FileSystemHelper.FlushToDiskIfFileStream(dest);
    }

    /// <summary>
    /// オブジェクトが使用するリソースを解放する。
    /// </summary>
    /// <param name="disposing">
    /// マネージドリソースとアンマネージドリソースの両方を解放する場合は true；
    /// アンマネージドリソースのみを解放する場合は false。
    /// </param>
    protected override void Dispose(bool disposing)
    {
        _lib.Dispose();

        // Add(Stream) で作成した一時ディレクトリを削除する
        if (_streamTempDir is not null && Directory.Exists(_streamTempDir))
        {
            try { Directory.Delete(_streamTempDir, recursive: true); }
            catch { /* クリーンアップ失敗は無視する */ }
            _streamTempDir = null;
        }
    }

    #endregion

    #region Implementations

    /// <summary>
    /// 既存アーカイブ Stream を開き、UpdatePlan に基づいて更新を実行する。
    /// </summary>
    private void UpdateCore(ArchiveStreamReader inStream, ArchiveStreamWriter outStream,
        string sourceHint, string destHint, string sourcePassword, IProgress<Report> progress,
        IReadOnlyDictionary<int, string> renameMap = null)
    {
        // COM オブジェクトを後で確実に解放するために変数を宣言しておく
        var inArchive = _lib.GetInArchive(Format);
        object outArchive = null;
        object setProps = null;
        OpenCallback openCb = null;

        try
        {
            // パスワードコールバックを生成する（暗号化アーカイブの読み取り用）
            openCb = new OpenCallback(sourceHint ?? string.Empty) { Password = new PasswordQuery(sourcePassword) };
            var openCode = inArchive.Open(inStream, IntPtr.Zero, openCb);
            if (openCode != 0) throw new IOException(
                $"Failed to open archive{(sourceHint.HasValue() ? $": {sourceHint}" : "")} (code={openCode})");

            var existingCount = inArchive.GetNumberOfItems();

            // 既存エントリのパスを 1 回だけ取得してキャッシュする。
            // (removeSet 構築と UpdatePlan で重複して COM RPC しないため)
            var existingPaths = new string[existingCount];
            for (uint i = 0; i < existingCount; i++)
            {
                existingPaths[i] = (inArchive.GetString((int)i, ItemPropId.Path) ?? string.Empty)
                    .Replace('/', '\\');
            }

            // 削除対象の既存インデックスを O(E + R) で収集する。
            // 旧実装は foreach (removeName) × for (existingCount) で O(E×R) だったが、
            // プレフィックスリストを事前構築 + 完全一致 Set で HashSet ヒットを優先する。
            HashSet<uint> removeSet = null;
            if (_removeNames.Count > 0)
            {
                removeSet = new HashSet<uint>();
                // 完全一致用セットと プレフィックス一致用リスト (末尾 '\' 付き) を事前構築
                var prefixes = new List<string>(_removeNames.Count);
                foreach (var name in _removeNames)
                {
                    prefixes.Add(name.EndsWith('\\') || name.EndsWith('/') ? name : name + "\\");
                }

                for (uint i = 0; i < existingCount; i++)
                {
                    var path = existingPaths[i];
                    if (path.Length == 0) continue;

                    // 完全一致 O(1)
                    if (_removeNames.Contains(path)) { removeSet.Add(i); continue; }

                    // プレフィックス一致: ディレクトリ削除は通常少数の名前のため O(R)
                    foreach (var prefix in prefixes)
                    {
                        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            removeSet.Add(i);
                            break;
                        }
                    }
                }
            }

            // 新規/置換アイテムリストは IncludeEmptyDirectories フィルタ後のものを使用する
            var filteredItems = FilterItems(_items);

            // renameMap を uint キーに変換（ArchiveEntity.Index は int だが plan は uint 空間）
            Dictionary<uint, string> planRenameMap = null;
            if (renameMap is not null && renameMap.Count > 0)
            {
                planRenameMap = new Dictionary<uint, string>(renameMap.Count);
                foreach (var kv in renameMap)
                {
                    if (kv.Key < 0 || (uint)kv.Key >= existingCount) continue;
                    // rename 値もサニタイズ (Zip Slip 生成側対策)。
                    // 値が null/empty は「削除」意図なのでそのまま渡す (UpdatePlan 側で削除扱い)。
                    planRenameMap[(uint)kv.Key] = string.IsNullOrEmpty(kv.Value)
                        ? kv.Value
                        : SanitizeRelativeName(kv.Value);
                }
            }

            // 既存アイテム・新規アイテム・削除セット・rename マップから UpdatePlan を生成する
            // existingPaths をキャッシュ配列から引き、重複 COM GetString を回避する
            var plan = new UpdatePlan(
                existingCount,
                idx => existingPaths[idx],
                filteredItems,
                removeSet,
                planRenameMap
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
            using var cb = new UpdateCallback(filteredItems, plan, existingBytes, progress)
            {
                Destination    = destHint ?? string.Empty,
                Password       = Options.Password,
                OnFileStarted  = RaiseFileCompressing,
                OnFileFinished = RaiseFileCompressed,
            };

            // UpdateItems を呼び出す
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
            openCb?.Dispose();
        }
    }

    /// <summary>
    /// 新しいアーカイブを Stream に書き込む。
    /// </summary>
    private void SaveAs(ArchiveStreamWriter outStream, IList<RawEntity> src, Format fmt,
        string destHint, IProgress<Report> progress)
    {
        Invoke(cb =>
        {
            // 新規アーカイブハンドラを取得する
            var archive = _lib.GetOutArchive(fmt);
            // 圧縮オプションを設定する
            var setter = CompressionOptionSetter.From(fmt, Options);
            var setProps = _lib.QueryInterface<ISetProperties>(archive);
            setter?.Invoke(setProps);
            try
            {
                return archive.UpdateItems(outStream, (uint)src.Count, cb);
            }
            finally
            {
                // COM オブジェクトを DLL アンロード前に解放する
                SevenZipLibrary.ReleaseComWrapper(setProps);
                SevenZipLibrary.ReleaseComWrapper(archive);
            }
        }, src, destHint, progress);
    }


    /// <summary>
    /// VolumeArchiveStreamWriter を使って分割書き出しを行う。
    /// </summary>
    /// <remarks>
    /// SaveAs とロジックは共通だが、第一引数が <see cref="ISequentialOutStream"/> 型となるため
    /// 別メソッドに分離する。UpdateItems は実際には ISequentialOutStream を要求するため、
    /// IOutStream 実装型（VolumeArchiveStreamWriter）を直接渡せる。
    /// </remarks>
    /// <summary>
    /// 作成済みのアーカイブファイルを VolumeSize ごとに分割して
    /// basePath.001, basePath.002, ... に書き出す。
    /// </summary>
    /// <param name="sourcePath">分割元のアーカイブファイル。</param>
    /// <param name="basePath">分割後ファイルの基準パス。</param>
    /// <param name="volumeSize">1 ボリュームあたりの最大バイト数。</param>
    /// <remarks>
    /// 既存 basePath.NNN が残っていた場合は上書きする。
    /// </remarks>
    private static void SplitFileIntoVolumes(string sourcePath, string basePath, long volumeSize)
    {
        // バッファサイズは共通定数 FileSystemHelper.DefaultBufferSize を使用
        const int bufferSize = FileSystemHelper.DefaultBufferSize;
        // LOH 圧迫を避けるため ArrayPool から貸し出す
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(bufferSize);

        // 途中で例外が発生した場合は生成済みの dest.NNN を全て削除する
        //       (中途半端な分割ファイルが残って再試行時に邪魔になるのを防ぐ)
        var createdVolumes = new List<string>();

        try
        {
            using var src = Io.Open(sourcePath);
            var totalSize = src.Length;
            long copied = 0;
            var index = 1;

            while (copied < totalSize)
            {
                var remaining = totalSize - copied;
                var currentVolumeSize = Math.Min(volumeSize, remaining);
                var volumePath = $"{basePath}.{index:D3}";
                createdVolumes.Add(volumePath);

                // 親ディレクトリを確実に作成する (base が階層を含む場合のため)
                var dir = Io.GetDirectoryName(volumePath);
                if (!string.IsNullOrEmpty(dir)) Io.CreateDirectory(dir);

                using (var dst = Io.Create(volumePath))
                {
                    long written = 0;
                    while (written < currentVolumeSize)
                    {
                        var toRead = (int)Math.Min(bufferSize, currentVolumeSize - written);
                        var read = src.Read(buffer, 0, toRead);
                        if (read <= 0) break;
                        dst.Write(buffer, 0, read);
                        written += read;
                    }
                    copied += written;
                }
                index++;
            }
        }
        catch
        {
            // 生成済みの .001 / .002 / ... を全削除して呼び出し元に例外を伝播する
            foreach (var v in createdVolumes) Logger.Try(() => Io.Delete(v));
            throw;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
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
            Io.CreateDirectory(Io.GetDirectoryName(tmp));
            using (var tarOut = new ArchiveStreamWriter(Io.Create(tmp)))
            {
                SaveAs(tarOut, src, Format.Tar, tmp, progress);
            }

            var m = Options.CompressionMethod;
            if (m == CompressionMethod.BZip2 || m == CompressionMethod.GZip || m == CompressionMethod.XZ)
            {
                // BZip2/GZip/XZ の場合は TAR を圧縮して最終出力を生成する
                var f = new List<RawEntity> { new(tmp, Io.GetFileName(tmp)) };
                using var outStream = new ArchiveStreamWriter(Io.Create(dest));
                SaveAs(outStream, f, m.ToFormat(), dest, progress);
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
    /// IncludeEmptyDirectories = false の場合、子孫を持たないディレクトリエントリを除外する。
    /// </summary>
    /// <remarks>
    /// <see cref="_items"/> はフラット化済み（AddRecursive で親ディレクトリ → 子ファイルの順に
    /// 登録される）のため、各ディレクトリ i について「i+1 以降に i.RelativeName を prefix とする
    /// 非ディレクトリエントリが 1 件でも存在するか」を線形走査で判定する。
    /// </remarks>
    /// <summary>
    /// アーカイブ内の相対パスをサニタイズする (Zip Slip 生成側対策)。
    /// </summary>
    /// <remarks>
    /// <c>Add(string, name)</c> / <c>Add(Stream, name)</c> /
    /// <c>Update(..., renameMap, ...)</c> の rename 値に共通で適用する。
    /// <c>..</c> / ドライブレター / UNC / 絶対パスは除去される。
    /// </remarks>
    internal static string SanitizeRelativeName(string name)
    {
        if (name is null) return null;
        return new SafePath(name)
        {
            AllowParentDirectory  = false,
            AllowDriveLetter      = false,
            AllowCurrentDirectory = false,
            AllowInactivation     = false,
            AllowUnc              = false,
        }.Value;
    }

    private IList<RawEntity> FilterItems(IList<RawEntity> src)
    {
        if (Options.IncludeEmptyDirectories || src.Count == 0) return src;

        // O(N × depth) アルゴリズム:
        // ファイルが属する全ての祖先ディレクトリを HashSet に事前収集することで、
        // 各ディレクトリが「子孫ファイルを持つか」を O(1) の HashSet.Contains で判定できる。
        // N=10000 ファイル × depth=5 で約 5 万回 → 旧 O(N²) の 10⁸ から 2000 倍改善。
        var dirWithChildren = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in src)
        {
            if (e.IsDirectory) continue;
            var p = e.RelativeName.Replace('/', '\\');
            // ファイルが属する全祖先ディレクトリを登録 (末尾 '\' なしで正規化)
            var sep = p.LastIndexOf('\\');
            while (sep > 0)
            {
                var dir = p.Substring(0, sep);
                if (!dirWithChildren.Add(dir)) break; // 既存ならその祖先も登録済み
                sep = dir.LastIndexOf('\\');
            }
        }

        var result = new List<RawEntity>(src.Count);
        foreach (var e in src)
        {
            if (!e.IsDirectory) { result.Add(e); continue; }

            // ディレクトリ名を正規化して (末尾 '\' 無し) HashSet を O(1) で引く
            var dirName = e.RelativeName.Replace('/', '\\').TrimEnd('\\');
            if (dirWithChildren.Contains(dirName)) result.Add(e);
        }
        return result;
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
                catch (IOException ex) when (FileSystemHelper.IsFileLocked(ex))
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
    /// UpdateCallback のインスタンスを生成して指定したコールバック関数を実行する。
    /// </summary>
    private void Invoke(Func<UpdateCallback, int> func,
        IList<RawEntity> src, string dest, IProgress<Report> progress)
    {
        // 新規作成モードの UpdateCallback を生成する
        using var cb = new UpdateCallback(src, progress)
        {
            Destination    = dest ?? string.Empty,
            Password       = Options.Password,
            OnFileStarted  = RaiseFileCompressing,
            OnFileFinished = RaiseFileCompressed,
        };

        var code = func(cb);

        // GC が UpdateItems 完了前にコールバックを回収しないよう保持する
        GC.KeepAlive(cb);

        // エラーコードを確認して例外をスローする
        cb.ThrowIfError(code);
    }

    /// <summary>
    /// FileCompressing / FileCompressed イベントを発火するヘルパー。
    /// </summary>
    private void RaiseFileCompressing(ArchiveFileEventArgs args) =>
        FileCompressing?.Invoke(this, args);

    private void RaiseFileCompressed(ArchiveFileEventArgs args) =>
        FileCompressed?.Invoke(this, args);

    #endregion

    #region Fields
    // 7z.dll のラッパー（参照カウント付き共有シングルトン）
    private readonly SevenZipLibrary _lib = SevenZipLibrary.Acquire();
    // 追加するファイル/ディレクトリのリスト
    private readonly List<RawEntity> _items = [];
    // Update 時に削除するアイテムの相対パスセット（OrdinalIgnoreCase）
    private readonly HashSet<string> _removeNames = new(StringComparer.OrdinalIgnoreCase);
    // Add(Stream) で作成した一時ファイルのディレクトリ（Dispose 時に削除）
    private string _streamTempDir;
    #endregion
}
