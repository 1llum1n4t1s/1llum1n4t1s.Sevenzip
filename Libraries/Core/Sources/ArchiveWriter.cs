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

/// <summary>
/// 新しいアーカイブを作成する機能を提供する。
/// </summary>
/// <remarks>
/// <para>
/// スレッドセーフではない。1 インスタンスを生成から破棄まで<b>同時に触るスレッドは常に 1 つ</b>に
/// 保つ必要がある。スレッドを跨いで受け渡すこと自体は、直列化プリミティブ
/// (<c>SemaphoreSlim</c> / <c>lock</c> / <c>await</c>) による happens-before があれば可
/// (スレッドアフィニティは要求しない)。単純な非同期利用は一連の処理を Task.Run で包む。
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
/// <see cref="Update(System.IO.Stream, System.IO.Stream, System.Collections.Generic.IReadOnlyDictionary{int, string}, string, System.IProgress{Report}, bool, bool, bool)"/>
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
    /// <exception cref="UnknownFormatException">
    /// format が <see cref="Format.Unknown"/> の場合。
    /// </exception>
    public ArchiveWriter(Format format, CompressionOption options)
    {
        if (format == Format.Unknown)
        {
            // ctor 失敗で this は孤児化する。リソース未取得のため解放は不要だが、
            // finalizer 不要を明示する (ctor cleanup の方針は下の catch コメント参照)。
            GC.SuppressFinalize(this);
            throw new UnknownFormatException();
        }

        Format  = format;
        Options = options;
        try
        {
            _lib = SevenZipLibrary.Acquire();

            // 書き込み非対応フォーマット (Rar 等の読み取り専用) を Save() 時まで
            // 遅延させずここで fail-fast 検出する。検証のみが目的のため生成した
            // COM オブジェクトは即解放する。
            IOutArchive archive = null;
            try { archive = _lib.GetOutArchive(format); }
            finally { _lib.ReleaseComWrapper(archive); }
        }
        catch
        {
            // ctor 失敗時のクリーンアップ (ArchiveReader の ctor と同型)。throw すると
            // this は誰にも参照されず孤児化し、取得済みのライブラリ参照カウントが GC まで
            // リークする (finalizer 経路の Dispose(false) は _lib に触らないガードがあり、
            // 解放は _lib 自身の finalizer 任せになる)。ここで同期的に解放し、
            // finalizer 自体を不要化する。
            try { Dispose(true); } catch { /* cleanup は best effort */ }
            GC.SuppressFinalize(this);
            throw;
        }
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

    /// <summary>
    /// <see cref="CompressionOption.SkipInaccessibleFiles"/> が true のとき、
    /// 読み取り不能または安全に追跡できないアイテムをスキップした際に発火するイベント。
    /// </summary>
    /// <remarks>
    /// イベント引数 (<see cref="FileSkippedEventArgs"/>) には対象アイテムのパス・相対パス・
    /// 原因例外が入る。呼び出し側はログ出力や UI 通知に利用できる。
    /// </remarks>
    public event EventHandler<FileSkippedEventArgs> FileSkipped;

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
    /// 複数回 Save / Update を呼ぶと値は最後の操作のもので上書きされるが、
    /// <b>前回値は上書き前に自動削除される</b>ため「古い .bak が孤立する」問題は発生しない。
    /// 全履歴が必要な場合は <see cref="BackupPaths"/> を参照。
    /// </para>
    /// </remarks>
    public string LastBackupPath { get; private set; }

    /// <summary>
    /// このインスタンスで発生した全てのバックアップ (.bak) パスを取得する。
    /// </summary>
    /// <remarks>
    /// <see cref="CompressionOption.KeepBackupOnUpdate"/> が <c>true</c> の場合に、
    /// Save / Update の呼び出しごとに追記される。古い .bak は <see cref="LastBackupPath"/>
    /// 更新時に自動削除されるため通常は 1 要素だが、自動削除が失敗したケースでは
    /// 履歴が残るのでクリーンアップ漏れの検出に使える。
    /// </remarks>
    public System.Collections.Generic.IReadOnlyList<string> BackupPaths => _backupPaths;

    /// <summary>
    /// 直近の <see cref="Update(string, string)"/> 等で生成された作業用 tmp ファイルのパス。
    /// </summary>
    /// <remarks>
    /// 正常完了時は null。異常終了 (SMB 切断、ネットワーク障害等) でクリーンアップが失敗した
    /// 場合のみ値が残る。呼び出し側は catch ブロックでこの値を確認して手動削除すること。
    /// </remarks>
    public string LastTempPath { get; private set; }

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
        ThrowIfDisposed();
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
        ThrowIfDisposed();
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
        ThrowIfDisposed();
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
    /// <exception cref="ArchiveUpdateException">
    /// <see cref="CompressionOption.AtomicSave"/> = true 時、tmp → dest の rename 失敗後の
    /// ロールバック (.bak からの復元) も失敗した場合。<see cref="ArchiveUpdateException.BackupPath"/>
    /// に .bak パスが入っているので呼び出し側が手動復旧できる。
    /// </exception>
    /// <exception cref="System.IO.IOException">
    /// 空き容量不足 / ディスクエラー / ファイル競合など通常の I/O 失敗。
    /// </exception>
    public void Save(string dest, IProgress<Report> progress)
    {
        ThrowIfDisposed();
        // オプションの組み合わせを早期検証 (VolumeSize/ThreadCount 負値、AtomicSave+VolumeSize、
        // Tar+Password の矛盾など)
        Options.Validate(Format);

        // .bak を作らない経路でも前回操作の値が残らないようクリアする
        // (LastBackupPath の契約: .bak が生成されなかった場合は null)。
        ResetLastBackupPath();

        // 保存先ディレクトリを事前に作成する
        var dir = Io.GetDirectoryName(dest);
        Io.CreateDirectory(dir);

        // AtomicSave モード: tmp file → atomic rename で書き出す (VolumeSize=0 時のみ)
        if (Options.AtomicSave)
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
            // AtomicSave + VolumeSize>0 は上のバリデーションで例外となるためここには到達しない。

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

        // 空き容量のベストエフォートチェック:
        // 既存 dest があればそのサイズ × 1.1 倍の空きを要求する (圧縮後のサイズは未知だが近似)。
        // 早期失敗させることで「数分書き込んだ末にディスク不足で失敗」という UX 破綻を防ぐ。
        FileSystemHelper.EnsureEnoughFreeSpace(destDir, dest);

        // LastTempPath は正常完了時のみ null。開始時にクリア (前回の残骸参照を落とす)。
        LastTempPath = null;
        ResetLastBackupPath();

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
                if (Options.KeepBackupOnUpdate) SetBackupPath(backup);
                // 削除に失敗した .bak はディスクに残るためパスを公開する。null にすると
                // LastBackupPath / BackupPaths のどちらにも残らず発見手段が無くなる。
                else if (Logger.Try(() => Io.Delete(backup))) SetBackupPath(null);
                else SetBackupPath(backup);
            }
            else SetBackupPath(null);
        }
        catch (ArchiveUpdateException)
        {
            // ロールバック失敗時は dest が存在せず、tmp (= 完成した新アーカイブ) が唯一の
            // 成果物になっている。削除すると圧縮結果が失われるため、パスだけ公開して残す。
            if (Io.Exists(tempPath)) LastTempPath = tempPath;
            throw;
        }
        catch
        {
            // tmp が残っていれば削除してクリーンアップする。失敗したら LastTempPath で公開。
            try
            {
                if (Io.Exists(tempPath)) Io.Delete(tempPath);
            }
            catch
            {
                // クリーンアップ失敗時は LastTempPath に残しておき、呼び出し側が手動削除できるようにする。
                LastTempPath = tempPath;
            }
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
        ThrowIfDisposed();
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
            var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tmp");
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
        ThrowIfDisposed();
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
    /// source と dest が同じパスを指す場合、または <see cref="CompressionOption.AtomicSave"/> = true の場合は
    /// 一時ファイルを経由して安全に更新する。
    /// </remarks>
    /// <exception cref="ArchiveUpdateException">
    /// 自己更新 (source == dest) または AtomicSave モードで、rename 失敗後のロールバックも失敗した場合。
    /// <see cref="ArchiveUpdateException.BackupPath"/> / <see cref="ArchiveUpdateException.OriginalPath"/>
    /// から手動復旧可能。
    /// </exception>
    /// <exception cref="System.NotSupportedException">TAR フォーマットへの Update 呼び出し。</exception>
    /// <exception cref="System.IO.IOException">空き容量不足 / ディスクエラー / ファイル競合等。</exception>
    public void Update(string source, string dest, string sourcePassword, IProgress<Report> progress)
    {
        ThrowIfDisposed();
        // 更新非対応フォーマットは早期に例外をスローする
        if (Format == Format.Tar)
            throw new NotSupportedException("Update is not supported for TAR format.");

        // オプションの組み合わせを早期検証
        Options.Validate(Format);

        // Update はボリューム分割を行わない。無言で無視すると呼び出し側が dest.001 を
        // 探して見つからず原因に到達できないため、Save(Stream) と同じく警告を出す。
        if (Options.VolumeSize > 0)
            Logger.Warn($"[{nameof(ArchiveWriter)}] CompressionOption.VolumeSize is ignored by " +
                        $"{nameof(Update)}; the archive is written as a single file.");

        // 絶対パスに正規化してから同一ファイルかどうかを判定する
        var srcFull  = Path.GetFullPath(source);
        var destFull = Path.GetFullPath(dest);
        var sameFile = string.Equals(srcFull, destFull, StringComparison.OrdinalIgnoreCase);

        // sameFile: 既存ファイルを直接上書きできないため別 tmp path に書いてから rename。
        // AtomicSave: 非 sameFile でも既存 dest を tmp 経由で置き換え、途中失敗時に dest を守る。
        var useTempThenRename = sameFile || Options.AtomicSave;

        var destDir = Path.GetDirectoryName(destFull);
        var actualDest = useTempThenRename
            ? Path.Combine(destDir, $"{Guid.NewGuid():N}{Path.GetExtension(destFull)}")
            : dest;

        // dest が既存でバックアップ退避が必要なケース (sameFile または AtomicSave + 既存 dest)
        var needsBackup = sameFile || (Options.AtomicSave && Io.Exists(dest) && !sameFile);
        var backup = needsBackup
            ? Path.Combine(destDir, $"{Guid.NewGuid():N}.bak")
            : null;

        // 空き容量ベストエフォートチェック (dest サイズ × 1.1 を要求)
        if (useTempThenRename) FileSystemHelper.EnsureEnoughFreeSpace(destDir, dest);

        // LastTempPath は正常完了時のみ null。開始時にクリア。
        LastTempPath = null;
        ResetLastBackupPath();

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
                try { outStream.Dispose(); }
                catch (Exception ex) { Logger.Warn($"[Update] outStream.Dispose failed: {ex.Message}"); }
                try { inStream.Dispose(); }
                catch (Exception ex) { Logger.Warn($"[Update] inStream.Dispose failed: {ex.Message}"); }
            }

            // 書き込み完了後にディスクフラッシュ。
            // rename 前に tmp ファイルの内容がディスクメディアに到達していることを保証する。
            if (Options.FlushToDisk) FileSystemHelper.FlushFile(actualDest);

            if (useTempThenRename)
            {
                // 既存 dest を .bak に退避 (backup が不要な新規作成ケースはスキップ)
                if (backup is not null) Io.Move(dest, backup, false);
                try
                {
                    // 新しく生成した一時ファイルを最終的な保存先に移動する
                    Io.Move(actualDest, dest, false);
                }
                catch (Exception ex)
                {
                    // Move 失敗時はバックアップから元のファイルを復元する
                    if (backup is not null)
                    {
                        try { Io.Move(backup, dest, false); }
                        catch (Exception restoreEx)
                        {
                            // ロールバック失敗時は ArchiveUpdateException 構造化例外で通知。
                            // BackupPath / OriginalPath プロパティから呼び出し側が手動復旧できる。
                            throw new ArchiveUpdateException(
                                $"Failed to restore original archive. Backup is at: {backup}",
                                originalPath: dest,
                                backupPath:   backup,
                                new AggregateException(ex, restoreEx));
                        }
                    }
                    throw; // Move 失敗の元例外を再スローする
                }
                // KeepBackupOnUpdate の場合はバックアップを保持し LastBackupPath で公開する。
                // それ以外は従来通り削除する (失敗しても無視)。
                if (backup is not null)
                {
                    if (Options.KeepBackupOnUpdate) SetBackupPath(backup);
                    // KeepBackupOnUpdate=false でも、削除に失敗した .bak は残るため
                    // パスを公開する。null にしてしまうと LastBackupPath / BackupPaths の
                    // どちらにも残らず、呼び出し側が孤児 .bak を発見する手段が無くなる
                    // (AV スキャナやインデクサの共有違反で日常的に起きる)。
                    else if (Logger.Try(() => Io.Delete(backup))) SetBackupPath(null);
                    else SetBackupPath(backup);
                }
                else SetBackupPath(null);
            }
        }
        catch (ArchiveUpdateException)
        {
            // ロールバック失敗時は dest が存在せず、actualDest (= 完成した新アーカイブ) が
            // 唯一の成果物になっている。ここで削除すると数十分の圧縮結果が失われるため、
            // tmp のクリーンアップは行わず、パスだけ公開して呼び出し側の判断に委ねる。
            if (useTempThenRename && Io.Exists(actualDest)) LastTempPath = actualDest;
            throw;
        }
        catch
        {
            // 異常終了時は生成した tmp ファイルを削除。
            // 削除失敗時は LastTempPath で公開して呼び出し側がクリーンアップできるようにする。
            if (useTempThenRename)
            {
                try
                {
                    if (Io.Exists(actualDest)) Io.Delete(actualDest);
                }
                catch
                {
                    LastTempPath = actualDest;
                }
            }
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
    /// <param name="allowDestructiveOnWritebackFailure">
    /// 自己参照 (source == dest) 時の書き戻しフェーズで例外が発生した場合に、dest を長さ 0 に truncate して
    /// サイレント破損を防ぐ挙動を許可するかどうか。既定値 <c>false</c> の場合は truncate せずに例外を伝播し、
    /// dest の状態は呼び出し側に委ねられる (部分書き込み状態の可能性あり)。
    /// </param>
    /// <remarks>
    /// <para>
    /// rename 処理は既存エントリのデータをそのまま再利用し、パスのみ差し替える（新規圧縮は発生しない）。
    /// 同時に <see cref="Add(string, string)"/> / <see cref="Add(Stream, string)"/> / <see cref="Remove(string)"/>
    /// による操作も適用できる。renameMap と Add に同じパスが現れた場合は Add の内容が優先される
    /// （Add のデータで置換される）。
    /// </para>
    /// <para>
    /// <b>自己参照 (source == dest) の挙動:</b> 一度 MemoryStream に読み出してから書き戻すため、
    /// アーカイブサイズが大きいと最悪 <b>元サイズ + 新サイズ + MemoryStream の内部バッファ成長 (各最大 2x)</b>
    /// でピーク <b>2〜4 倍</b> のメモリを消費する。10MB 以上のアーカイブでは path ベース
    /// <see cref="Update(string, string, string, IProgress{Report})"/> の利用を推奨する。
    /// </para>
    /// <para>
    /// <b>OOM 時の dest 保護:</b> 自己参照更新中に <see cref="OutOfMemoryException"/> や
    /// UpdateCore 内部例外が発生した場合、元の dest 内容を維持するために処理は
    /// <b>MemoryStream ローカルバッファの生成完了までは dest に書き込まない</b>。
    /// 書き戻しフェーズ中に例外が起きた場合の挙動は <paramref name="allowDestructiveOnWritebackFailure"/>
    /// で切り替え可能。
    /// </para>
    /// </remarks>
    public void Update(Stream source, Stream dest,
        IReadOnlyDictionary<int, string> renameMap,
        string sourcePassword = null,
        IProgress<Report> progress = null,
        bool leaveSourceOpen = true, bool leaveDestOpen = true,
        bool allowDestructiveOnWritebackFailure = false)
    {
        ThrowIfDisposed();
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
            // 自己参照更新: 結果を MemoryStream に組み立ててから書き戻す。
            // 入力側の全量コピーは行わない。source == dest はシーク可能であることが上の
            // ガードで保証済みで、かつ outBuffer が完成するまで dest には一切触らないため、
            // source をそのまま読み取り専用の入力として使える。以前は入力も MemoryStream へ
            // 複製していたためピークメモリがアーカイブサイズの 2 倍になっていた。
            source.Position = 0L;

            using var outBuffer = new MemoryStream();
            using (var inStream = new ArchiveStreamReader(source, dispose: false))
            using (var outStream = new ArchiveStreamWriter(outBuffer, dispose: false))
            {
                // 書き戻しフェーズ前にここで例外が起きた場合、dest は無変更のまま
                // (書き戻し未実施) となる。OOM 耐性: outBuffer が完成するまで dest
                // に触らない設計。
                UpdateCore(inStream, outStream, string.Empty, string.Empty,
                    sourcePassword ?? Options.Password, progress, renameMap);
            }

            // 結果を元の Stream に書き戻す (CanSeek は上記ガードで保証済み)。
            // 書き戻し中 (CopyTo / Flush) の例外挙動は allowDestructiveOnWritebackFailure で切り替え:
            //  - false (既定): dest の状態はそのまま (部分書き込み状態の可能性あり) で例外のみ伝播
            //  - true: dest.SetLength(0) で破棄してサイレント破損を防ぐ
            try
            {
                dest.Position = 0L;
                dest.SetLength(0L);
                outBuffer.Position = 0L;
                outBuffer.CopyTo(dest, FileSystemHelper.DefaultBufferSize);
                dest.Flush();
                // 呼び出し元が読み戻せるよう位置を先頭に戻す
                dest.Position = 0L;
            }
            catch
            {
                if (allowDestructiveOnWritebackFailure)
                {
                    try { dest.SetLength(0L); dest.Flush(); } catch { /* 最終防御は失敗しても諦める */ }
                }
                // false の場合は dest をそのまま呼び出し側に委ねる (部分書き込み状態を保つ)
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
        // _lib は ctor の Acquire 失敗時は null のため null 条件演算子で扱う。
        if (disposing) _lib?.Dispose();
        else
        {
            // finalizer 経路 (disposing == false) では通常の Dispose を呼べない。
            // SevenZipLibrary.Dispose は追跡 COM ラッパーの FinalRelease と 7z.dll の
            // アンロードを行うが、finalizer スレッドでは対象が既に finalize 済みの可能性があり、
            // また DLL をアンロードすると後続の ComObject finalizer の Release が
            // アンマップ領域へ到達する。参照カウントだけを戻す専用経路を使う。
            // ここを省くと Dispose 漏れ 1 回で参照カウントが永久に 0 へ戻らず、
            // 以降に正しく Dispose された全インスタンスの解放まで無効化される。
            // 警告ログを含め全体を保護する (finalizer スレッドの未処理例外は致死)。
            SevenZipLibrary.ReleaseFromFinalizerSafe(_lib, nameof(ArchiveWriter));
        }

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
    /// 破棄済みの場合に <see cref="ObjectDisposedException"/> を投げる。
    /// </summary>
    /// <remarks>
    /// 破棄後に Save / Update を呼ぶと、参照カウントが 0 に落ちて 7z.dll が FreeLibrary 済みの場合に
    /// キャッシュ済み CreateObject 関数ポインタがアンマップ領域へ分岐し、catch 不可の
    /// AccessViolationException でプロセスごと即死する（Save は先に dest を truncate するため
    /// 出力ファイルも失われる）。ArchiveCollection と同じく明示的な例外で早期検出する。
    /// </remarks>
    private void ThrowIfDisposed()
    {
        if (Disposed) throw new ObjectDisposedException(nameof(ArchiveWriter));
    }

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
            if (openCode != 0)
            {
                if (openCb.Exceptions.Count > 0)
                {
                    var inner = openCb.Exceptions.Pop();
                    if (inner is EncryptionException) throw inner;
                    throw new SevenZipException(SevenZipCode.HeadersError, inner);
                }

                if (openCode > 0) throw new SevenZipException(SevenZipCode.IsNotArc);
                throw new SevenZipException(SevenZipCode.UnknownError,
                    Marshal.GetExceptionForHR(openCode) ??
                    new COMException($"IInArchive.Open failed. HRESULT: 0x{openCode:X8}", openCode));
            }

            var existingCount = inArchive.GetNumberOfItems();

            // 既存エントリのパスを 1 回だけ取得してキャッシュする。
            // (removeSet 構築と UpdatePlan で重複して COM RPC しないため)
            var existingPaths = new string[existingCount];
            for (uint i = 0; i < existingCount; i++)
            {
                existingPaths[i] = (inArchive.GetString((int)i, ItemPropId.Path) ?? string.Empty)
                    .Replace('/', '\\');
            }

            // 削除対象の既存インデックスを O(E × depth) で収集する。
            // path.StartsWith(name + "\\") は「path のいずれかの祖先ディレクトリが name と一致」と
            // 同値なので、祖先を辿って完全一致 Set に問い合わせれば削除名の件数 R に依存しない。
            // (旧実装は非該当エントリ全件に対し prefixes を線形走査していたため実質 O(E×R) で、
            //  Remove を多数呼ぶ呼び出し側では E=10万・R=1万 で 10^9 回の StartsWith になっていた)
            HashSet<uint> removeSet = null;
            if (_removeNames.Count > 0)
            {
                removeSet = new HashSet<uint>();
                for (uint i = 0; i < existingCount; i++)
                {
                    var path = existingPaths[i];
                    if (path.Length == 0) continue;

                    // 自身と全祖先を O(1) で判定する (_removeNames は OrdinalIgnoreCase)
                    var cursor = path;
                    while (cursor.Length > 0)
                    {
                        if (_removeNames.Contains(cursor)) { removeSet.Add(i); break; }

                        var sep = cursor.LastIndexOf('\\');
                        if (sep <= 0) break;
                        cursor = cursor[..sep];
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
                    if (string.IsNullOrEmpty(kv.Value))
                    {
                        planRenameMap[(uint)kv.Key] = kv.Value;
                        continue;
                    }

                    var sanitized = SanitizeRelativeName(kv.Value);
                    if (string.IsNullOrEmpty(sanitized)) throw new ArgumentException(
                        "The renamed archive path must contain at least one safe path segment.", nameof(renameMap));
                    planRenameMap[(uint)kv.Key] = sanitized;
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
            _lib.ReleaseComWrapper(setProps);
            _lib.ReleaseComWrapper(outArchive);
            if (inArchive is not null)
            {
                inArchive.Close(); // アーカイブを閉じてからラッパーを解放する
                _lib.ReleaseComWrapper(inArchive);
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
            // 新規アーカイブハンドラ / SetProperties 変数を先に宣言し、
            // setter.Invoke 中の例外でも確実に finally で解放できるよう外枠の try で囲む。
            IOutArchive archive = null;
            ISetProperties setProps = null;
            try
            {
                archive  = _lib.GetOutArchive(fmt);
                setProps = _lib.QueryInterface<ISetProperties>(archive);
                var setter = CompressionOptionSetter.From(fmt, Options);
                // setter.Invoke 自体が例外をスローしても下の finally で解放される
                setter?.Invoke(setProps);
                return archive.UpdateItems(outStream, (uint)src.Count, cb);
            }
            finally
            {
                // COM オブジェクトを DLL アンロード前に解放する (順序: setProps → archive)
                _lib.ReleaseComWrapper(setProps);
                _lib.ReleaseComWrapper(archive);
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
        var dir = Io.Combine(Io.GetDirectoryName(dest), $"{Guid.NewGuid():N}");
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

        if (!AddItem(src)) return;
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
        // 初期容量に src.Count を渡して 1 万件以上でも rehash を避ける。
        var dirWithChildren = new HashSet<string>(src.Count, StringComparer.OrdinalIgnoreCase);
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
    private bool AddItem(RawEntity src)
    {
        try
        {
            // ジャンクションやシンボリックリンクを辿ると、循環したり追加元ツリー外の
            // ファイルを意図せず取り込んだりするため、明示的な追跡オプションが無い
            // 現状では拒否する。
            if ((src.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Refusing to archive reparse point: '{src.FullName}'.");

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
            return true;
        }
        catch (Exception e)
        {
            // アクセスエラーをログに記録してラップした例外をスローする
            Logger.Debug($"Path:{src.FullName.Quote()}, Error:{e.Message} ({e.GetType().Name})");

            // SkipInaccessibleFiles=true: アーカイブ全体を死なせず当該アイテムをスキップ。
            // FileShare.None で他プロセスが排他保持しているファイル (Visual Studio の .vsidx 等) を
            // 想定。再解析ポイントも追跡せず同じ通知経路で除外する。_items に追加しないので
            // Save 時の GetStream にも到達せず、ディレクトリの場合は呼び出し元が子孫列挙を止める。
            if (Options?.SkipInaccessibleFiles == true)
            {
                Logger.Warn($"Skipped inaccessible or unsafe item: Path:{src.FullName.Quote()}, " +
                            $"Reason:{e.Message} ({e.GetType().Name})");
                FileSkipped?.Invoke(this, new FileSkippedEventArgs
                {
                    FullName     = src.FullName,
                    RelativeName = src.RelativeName,
                    Reason       = e,
                });
                return false;
            }

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

    /// <summary>
    /// 操作開始時に <see cref="LastBackupPath"/> をクリアする。
    /// </summary>
    /// <remarks>
    /// <see cref="LastBackupPath"/> の契約は「今回の操作で生成された .bak、無ければ null」だが、
    /// .bak を作らない経路 (非 Atomic な Save) や操作中の例外では <see cref="SetBackupPath"/> が
    /// 呼ばれず、前回操作の値が残って契約を破る。呼び出し側が「今回の失敗の .bak」と
    /// 誤認して 1 世代古いアーカイブから復元する事故につながるため、開始時にクリアする。
    /// ファイル自体は削除せず <see cref="BackupPaths"/> 履歴にも残すので、
    /// 過去の .bak は引き続き検出・クリーンアップできる。
    /// </remarks>
    private void ResetLastBackupPath() => LastBackupPath = null;

    /// <summary>
    /// 新しい .bak パスを設定する共通ヘルパー。
    /// 前回値が残っていれば自動削除し、<see cref="LastBackupPath"/> と <see cref="BackupPaths"/> 履歴を更新する。
    /// </summary>
    /// <param name="backup">今回生成した .bak パス。null の場合は「.bak なし」を表す。</param>
    /// <remarks>
    /// <paramref name="backup"/> が null の場合は「.bak なし」として LastBackupPath をクリアするのみ。
    /// 前回値の自動削除に失敗した場合は BackupPaths に残り続けるため、呼び出し側がクリーンアップ可能。
    /// </remarks>
    private void SetBackupPath(string backup)
    {
        // 自動削除の対象は「報告用の LastBackupPath」ではなく「前回実際に作った .bak」で判定する。
        // LastBackupPath は操作開始時に ResetLastBackupPath でクリアされるため、
        // こちらを起点にすると世代を跨いだ .bak の自動削除が働かなくなる。
        var previous = _lastActualBackup;
        LastBackupPath = backup;
        if (backup is not null)
        {
            _backupPaths.Add(backup);
            _lastActualBackup = backup;
        }

        // 前回値が存在していれば「古い .bak が孤立する」問題を防ぐため自動削除を試みる。
        // 削除に失敗しても BackupPaths 履歴には残るので呼び出し側が検出・クリーンアップ可能。
        if (previous is not null && previous != backup)
        {
            try
            {
                if (Io.Exists(previous)) Io.Delete(previous);
                _backupPaths.Remove(previous);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ArchiveWriter] Failed to delete previous backup '{previous}': {ex.Message}");
            }
        }

        // 削除できた (または対象が無かった) 場合は追跡から外す
        if (backup is null && previous is not null && !Io.Exists(previous)) _lastActualBackup = null;
    }

    #endregion

    #region Fields
    // 7z.dll のラッパー（参照カウント付き共有シングルトン）。
    // ctor 失敗時に同期解放できるよう、フィールド初期化子ではなく ctor 内で取得する。
    private readonly SevenZipLibrary.Lease _lib;
    // 追加するファイル/ディレクトリのリスト
    private readonly List<RawEntity> _items = [];
    // Update 時に削除するアイテムの相対パスセット（OrdinalIgnoreCase）
    private readonly HashSet<string> _removeNames = new(StringComparer.OrdinalIgnoreCase);
    // Add(Stream) で作成した一時ファイルのディレクトリ（Dispose 時に削除）
    private string _streamTempDir;
    // KeepBackupOnUpdate=true 時の .bak 履歴。前回値は自動削除されるが、削除失敗時は残る。
    private readonly List<string> _backupPaths = [];
    // 直近に実際に生成した .bak パス（世代を跨いだ自動削除の判定用）。
    // 報告用の LastBackupPath は操作開始時にクリアされるため、削除追跡は別に持つ。
    private string _lastActualBackup;
    #endregion
}
