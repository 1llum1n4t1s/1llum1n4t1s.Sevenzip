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
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
namespace Cube.FileSystem.SevenZip;

/// <summary>
/// アーカイブ作成（新規・更新）のコールバック関数を表す。
/// </summary>
/// <remarks>
/// IArchiveUpdateCallback および ICryptoGetTextPassword2 を実装し、
/// 7-zip の UpdateItems 操作に必要な全コールバックを処理する。
///
/// 2つのモードで動作する：
/// - 新規作成モード（<see cref="_plan"/> が null）: 全アイテムを新規として扱う。
/// - 更新モード（<see cref="_plan"/> が非 null）: UpdatePlan に基づいて Keep/Replace/Add を処理する。
/// </remarks>
[GeneratedComClass]
internal sealed partial class UpdateCallback : CallbackBase, IArchiveUpdateCallback, ICryptoGetTextPassword2
{
    #region Constructors

    /// <summary>
    /// 指定した引数で UpdateCallback クラスの新しいインスタンスを初期化する（新規作成モード）。
    /// </summary>
    /// <param name="items">圧縮するファイルのリスト。</param>
    /// <param name="progress">進捗を報告するオブジェクト。</param>
    public UpdateCallback(IList<RawEntity> items, IProgress<Report> progress) : base(progress)
    {
        _items = items;
        TotalCount = items.Count;
        // 全アイテムのバイト数を合計して進捗報告の分母とする
        TotalBytes = items.Sum(e => e.Length);
    }

    /// <summary>
    /// 既存アーカイブの更新用コンストラクタ（更新モード）。
    /// UpdatePlan に基づき既存アイテムの保持・置換・新規追加を行う。
    /// </summary>
    /// <param name="items">新規/置換アイテムのリスト。</param>
    /// <param name="plan">更新プラン（Keep/Replace/Add のマッピング情報）。</param>
    /// <param name="existingBytes">保持アイテムの合計バイト数（進捗報告用）。</param>
    /// <param name="progress">進捗を報告するオブジェクト。</param>
    public UpdateCallback(IList<RawEntity> items, UpdatePlan plan, long existingBytes, IProgress<Report> progress) : base(progress)
    {
        _items = items;
        _plan  = plan;
        // 更新モードでは出力アーカイブの総アイテム数はプランが決定する
        TotalCount = plan.TotalCount;
        // 新規/置換アイテムのバイト数に加え、保持アイテムのバイト数も総量に含める
        TotalBytes = items.Sum(e => e.Length) + existingBytes;
    }

    #endregion

    #region Properties

    /// <summary>
    /// 圧縮ファイルの保存先パスを取得または設定する。
    /// </summary>
    public string Destination { get; init; }

    /// <summary>
    /// アーカイブに設定するパスワードを取得または設定する。
    /// </summary>
    /// <remarks>
    /// null または空文字の場合はパスワードなしのアーカイブを作成する。
    /// </remarks>
    public string Password { get; init; }

    /// <summary>
    /// ファイル圧縮開始時に呼び出されるイベントハンドラ。null の場合は発火しない。
    /// </summary>
    public Action<ArchiveFileEventArgs> OnFileStarted { get; init; }

    /// <summary>
    /// ファイル圧縮終了時に呼び出されるイベントハンドラ。null の場合は発火しない。
    /// </summary>
    public Action<ArchiveFileEventArgs> OnFileFinished { get; init; }

    #endregion

    #region IProgress

    /// <summary>
    /// ターゲットファイルの総バイト数を通知する。
    /// </summary>
    /// <param name="bytes">総バイト数（7-zip からの通知値、実際には使用しない）。</param>
    /// <returns>操作結果コード。</returns>
    /// <remarks>
    /// TotalBytes はコンストラクタでアイテムから計算済みのため、
    /// 7-zip からの通知値は無視する。
    /// </remarks>
    public int SetTotal(ulong bytes)
    {
        // コンストラクタで設定済みの TotalBytes を使用するため bytes は無視する
        return Report(ProgressState.Prepare, Current());
    }

    /// <summary>
    /// アーカイブ済みバイト数を通知する。
    /// </summary>
    /// <param name="bytes">処理済みバイト数を指すポインタ。null の場合は 0 として扱う。</param>
    /// <returns>操作結果コード。</returns>
    /// <remarks>
    /// 7-zip の completeValue は SetTotal と同尺度のグローバル累積値（アーカイブ全体の
    /// 絶対進捗位置）であり、ファイル毎にリセットされない（7-Zip 公式コンソール UI も
    /// _percent.Completed への絶対代入で解釈している）。ZIP 等のマルチスレッド圧縮では
    /// スレッド間の読み取りレースにより直前より小さい値がまれに届くため、単調最大値のみを
    /// 採用して UI の進捗後退を防ぐ。
    ///
    /// 旧実装の「ファイル毎リセット検出」（値の減少を検出するたびに直前値を累積加算）は、
    /// この後退をファイル切替と誤認してグローバル累積値を二重加算し、大規模アーカイブで
    /// 進捗が実処理の半ばで 100% に張り付く原因だった（実測: 528k ファイル / 60GB の ZIP
    /// 圧縮 22 分中、16 回の後退 → 23.7% 過剰計上 → 残り 9.2 分が 100% 表示のまま）。
    /// </remarks>
    public int SetCompleted(IntPtr bytes)
    {
        var value = bytes != IntPtr.Zero ? Marshal.ReadInt64(bytes) : 0L;

        // ZIP Ultra 等のマルチスレッド圧縮では SetCompleted が並行呼び出しされうるため、
        // _maxCompletedBytes のフィールドアクセスを lock で保護する。
        // lock 範囲は小さく保ち、Report 呼び出しは lock 外で行う (再入デッドロック回避)。
        long snap;
        lock (_completedLock)
        {
            if (value > _maxCompletedBytes) _maxCompletedBytes = value;
            snap = _maxCompletedBytes;
        }

        // 100% 超過で進捗 UI がおかしくならないよう TotalBytes を上限とする
        // (completeValue は complexity 尺度でエントリ毎のヘッダ定数等を含むため、
        //  純粋なファイルサイズ合計である TotalBytes を僅かに超えることがある)
        Bytes = TotalBytes > 0 && snap > TotalBytes ? TotalBytes : snap;

        // 50ms 以内の連続呼び出しは進捗報告をスキップしてコールバックのオーバーヘッドを削減する
        var now = Environment.TickCount64;
        if (now - _lastReportedTicks < ReportIntervalMs)
            return (int)SevenZipCode.Success;

        _lastReportedTicks = now;
        return Report(ProgressState.Progress, Current());
    }

    #endregion

    #region IArchiveUpdateCallback

    /// <summary>
    /// 更新アイテムの情報を取得する。
    /// </summary>
    /// <param name="index">アイテムのインデックス（7-zip の UpdateItems インデックス空間）。</param>
    /// <param name="newdata">1 = 新しいデータが必要；0 = 既存データをコピー。</param>
    /// <param name="newprop">1 = 新しいプロパティが必要；0 = 既存プロパティをコピー。</param>
    /// <param name="indexInArchive">既存アーカイブ内の元インデックス；新規の場合は uint.MaxValue。</param>
    /// <returns>操作結果コード。</returns>
    public int GetUpdateItemInfo(uint index, ref int newdata, ref int newprop, ref uint indexInArchive)
    {
        if (_plan is not null)
        {
            // 更新モード: UpdatePlan のエントリに基づいて各フラグを設定する
            var entry = _plan.Entries[index];
            if (entry.IsNewOrReplaced)
            {
                // 新規または置換: 新しいデータとプロパティを要求する
                newdata = 1;
                newprop = 1;
                indexInArchive = entry.OriginalIndex; // 対応する元インデックス（新規は uint.MaxValue）
            }
            else if (entry.RenameTo is not null)
            {
                // 保持 + rename: データは既存のまま、プロパティだけ新しい値を要求する
                newdata = 0;
                newprop = 1;
                indexInArchive = entry.OriginalIndex;
            }
            else
            {
                // 保持: 既存アーカイブからデータとプロパティをそのままコピーする
                newdata = 0;
                newprop = 0;
                indexInArchive = entry.OriginalIndex;
            }
        }
        else
        {
            // 新規作成モード: 全アイテムを新規として扱う
            newdata = 1;
            newprop = 1;
            indexInArchive = uint.MaxValue; // 既存アーカイブには対応エントリなし
        }

        // 新規アイテムリスト内のインデックスを解決して進捗アイテムを取得する
        var i = ResolveItemIndex(index);
        var e = (i >= 0 && i < _items.Count) ? _items[i] : null;

        // アイテムが変わった場合は準備フェーズの進捗を報告する
        if (UpdateItemProgress(index))
            return Report(ProgressState.Prepare, e);

        return (int)SevenZipCode.Success;
    }

    /// <summary>
    /// 指定した引数に応じたプロパティ情報を取得する。
    /// </summary>
    /// <param name="index">対象ファイルのインデックス（7-zip の UpdateItems インデックス空間）。</param>
    /// <param name="pid">取得するプロパティ ID。</param>
    /// <param name="value">指定したプロパティの値（out パラメータ）。</param>
    /// <returns>操作結果コード。</returns>
    public int GetProperty(uint index, ItemPropId pid, ref PropVariant value)
    {
        // rename エントリの場合: プランから元情報を参照して必要最小限のプロパティだけ返す
        if (_plan is not null && index < _plan.Entries.Length)
        {
            var entry = _plan.Entries[index];
            if (!entry.IsNewOrReplaced && entry.RenameTo is not null)
            {
                // Path は新しい名前に差し替え、その他は空 (7-zip が既存アーカイブから補完する)
                if (pid == ItemPropId.Path) value.Set(entry.RenameTo);
                else value.Clear();
                return (int)SevenZipCode.Success;
            }
        }

        var i = ResolveItemIndex(index);
        if (i < 0)
        {
            // 保持アイテムの場合: 7-zip は newprop=0 のため通常呼ばれないが安全のため処理する
            value.Clear();
            return (int)SevenZipCode.Success;
        }

        var src = (i >= 0 && i < _items.Count) ? _items[i] : null;
        if (src is null) return (int)SevenZipCode.Unavailable;

        // アイテムが変わった場合は進捗を更新する（GetProperty は GetUpdateItemInfo より先に呼ばれる場合がある）
        var indexChanged = UpdateItemProgress(index);

        switch (pid)
        {
            case ItemPropId.Path:
                // アーカイブ内の相対パスを設定する
                value.Set(src.RelativeName);
                break;
            case ItemPropId.Attributes:
                // ファイル属性（隠しファイル、読み取り専用など）を設定する
                value.Set((uint)src.Attributes);
                break;
            case ItemPropId.IsDirectory:
                // ディレクトリかどうかを設定する
                value.Set(src.IsDirectory);
                break;
            case ItemPropId.IsAnti:
                // Anti アイテム（削除マーカー）は常に false を設定する
                value.Set(false);
                break;
            case ItemPropId.CreationTime:
                // ファイルの作成日時を設定する
                value.Set(src.CreationTime);
                break;
            case ItemPropId.LastAccessTime:
                // ファイルの最終アクセス日時を設定する
                value.Set(src.LastAccessTime);
                break;
            case ItemPropId.LastWriteTime:
                // ファイルの最終更新日時を設定する
                value.Set(src.LastWriteTime);
                break;
            case ItemPropId.Size:
                // ファイルサイズを ulong として設定する
                value.Set((ulong)src.Length);
                break;
            case ItemPropId.Comment:
                // コメントが設定されている場合のみ値をセットする（ZIP 形式専用）
                if (src.Comment is not null) value.Set(src.Comment);
                else value.Clear();
                break;
            default:
                // 未知のプロパティ ID はトレースログに記録して空値を返す
                Logger.Trace($"Pid:{pid}");
                value.Clear();
                break;
        }

        if (indexChanged)
        {
            // アイテムが変わった場合は準備フェーズの進捗を報告する
            return Report(ProgressState.Prepare, src);
        }

        return (int)SevenZipCode.Success;
    }

    /// <summary>
    /// 指定した引数に応じたストリームを取得する。
    /// </summary>
    /// <param name="index">対象ファイルのインデックス（7-zip の UpdateItems インデックス空間）。</param>
    /// <param name="stream">読み取りストリーム（保持アイテムまたはディレクトリの場合は null）。</param>
    /// <returns>操作結果コード。</returns>
    public int GetStream(uint index, out ISequentialInStream stream)
    {
        var resolved = ResolveItemIndex(index);
        if (resolved < 0)
        {
            // 保持アイテムの場合: 7-zip が既存アーカイブから直接コピーするためストリーム不要
            stream = null;
            return (int)SevenZipCode.Success;
        }

        _index = resolved;

        var dest = default(ISequentialInStream);
        var src  = Current();

        // FileCompressing イベントを発火する（ファイル読み取りの直前）
        if (FireFileEvent(OnFileStarted, src, _index))
        {
            stream = null;
            return (int)SevenZipCode.Cancel;
        }

        try
        {
            // ファイルを開いてストリームを生成する処理を Run でラップする
            return Run(() => {
                dest = Open(src);
                return (int)SevenZipCode.Success;
            }, ProgressState.Start, src);
        }
        finally
        {
            // 例外が発生した場合でも out パラメータを確実に設定する
            stream = dest;
        }
    }

    /// <summary>
    /// 操作結果を設定する。
    /// </summary>
    /// <param name="code">操作結果コード。</param>
    /// <returns>操作結果コード。</returns>
    public int SetOperationResult(SevenZipCode code)
    {
        if (code != SevenZipCode.Success) Logger.Warn($"[{code}] Index:{_index}, Name:{Current()?.RawName ?? ""}");

        // Stream 早期解放の設計メモ (検証済の現時点での結論):
        // 以下の 2 案で早期解放を試みたがどちらも破綻したため、Dispose 時一括解放に集約している:
        //
        // 案 A: SetOperationResult で _currentStream を即時 Dispose
        //   → 並行 GetStream (ZIP Ultra 等) で _currentStream が別エントリで上書きされ、
        //      誤って使用中の stream を Dispose → MarshalDirectiveException (0x80131522)
        //
        // 案 B: Read で EOF 到達を検知して SetCompleted 時に CleanupFinishedStreams
        //   → ZIP Ultra のマルチスレッド圧縮では Read 完了後も 7z.dll が stream を保持し
        //      続ける経路があり、Dispose 後に COM 経由でアクセスされて同じく例外
        //
        // 正しく安全な早期解放には index 付き SetOperationResult 相当の改造か、
        // 7z.dll 側パッチが必要。現在のワークロード規模 (数万エントリ) では Dispose
        // 時一括解放でハンドル上限に余裕があるため、現状の設計で妥協している。

        // FileCompressed イベントを発火する（成功・失敗問わず）
        if (FireFileEvent(OnFileFinished, Current(), _index))
            return (int)SevenZipCode.Cancel;

        // 成功の場合は Success 状態、失敗の場合は例外を含む Failed 状態を報告する
        return code == SevenZipCode.Success ?
               Report(ProgressState.Success, Current()) :
               Report(new SevenZipException(code), Current());
    }

    /// <summary>
    /// EnumProperties 7-zip 内部関数（未実装）。
    /// </summary>
    /// <returns>E_NOTIMPL (0x80004001)。</returns>
    public long EnumProperties(IntPtr enumerator) => 0x80004001L; // E_NOTIMPL


    #endregion

    #region ICryptoGetTextPassword2

    /// <summary>
    /// 圧縮ファイルに設定するパスワードを取得する。
    /// </summary>
    /// <param name="enabled">パスワードが有効な場合は 1；無効な場合は 0。</param>
    /// <param name="password">パスワード文字列。</param>
    /// <returns>操作結果コード。</returns>
    public int CryptoGetTextPassword2(ref int enabled, out string password)
    {
        // Password が設定されている場合は有効フラグを立てる
        enabled  = Password.HasValue() ? 1 : 0;
        password = Password;
        return (int)SevenZipCode.Success;
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// オブジェクトが使用するリソースを解放する。
    /// </summary>
    /// <param name="disposing">
    /// マネージドリソースとアンマネージドリソースの両方を解放する場合は true；
    /// アンマネージドリソースのみを解放する場合は false。
    /// </param>
    protected override void Dispose(bool disposing)
    {
        // finalizer 経路 (disposing == false) では他のマネージドオブジェクト
        // (ストリーム) に触らない。各ストリームは自身の finalizer / SafeHandle が
        // 回収する (ArchiveReader.Dispose の同ガード参照)。一時ディレクトリ削除は
        // 純粋な OS 呼び出しなので finalizer 経路でも実行する。
        if (disposing)
        {
            // ここで一括解放する。詳細は SetOperationResult のコメントを参照。
            foreach (var stream in _streams)
            {
                try { stream.Dispose(); } catch { /* 解放失敗は無視 */ }
            }
        }
        _streams.Clear();

        // ロック中ファイルの一時コピーを削除する
        if (_tempDir is not null && Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* クリーンアップ失敗は無視 */ }
        }
    }

    #endregion

    #region Implementations

    /// <summary>
    /// 指定したエンティティのファイルを開いて読み取りストリームを返す。
    /// ファイルが他プロセスにロックされている場合は一時コピーを作成して開く。
    /// </summary>
    private ArchiveStreamReader Open(Entity src)
    {
        // ディレクトリまたは存在しないファイルはストリーム不要
        if (!src.Exists || src.IsDirectory) return null;

        Stream stream;
        try
        {
            // まず通常モード（FileShare.Read）で開く
            stream = Io.Open(src.FullName);
        }
        catch (IOException ex) when (FileSystemHelper.IsFileLocked(ex))
        {
            // ロック中 → 一時コピーを作成してコピーを開く
            var tempPath = CopyLockedFile(src.FullName);
            stream = File.OpenRead(tempPath);
        }

        // ストリームを開き、Dispose 時に解放できるようリストに登録する
        var dest = new ArchiveStreamReader(stream);
        _streams.Add(dest);
        return dest;
    }

    /// <summary>
    /// ロック中のファイルを一時ディレクトリにコピーし、コピー先パスを返す。
    /// FileShare.ReadWrite で開くことでロック中でも読み取れる。
    /// </summary>
    private string CopyLockedFile(string sourcePath)
    {
        // 一時ディレクトリを遅延作成（乱数名で衝突回避）
        if (_tempDir is null)
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"SevenZip_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);

            // RDS/Citrix 等の共有 %TEMP% 環境で攻撃者がシンボリックリンクを事前配置している
            // 可能性を排除するため、作成したディレクトリが実ディレクトリであることを検証する。
            // Directory.CreateDirectory は既存ディレクトリ (リパースポイント含む) でも例外を
            // 投げないため、明示的に Attributes でチェックする必要がある。
            var dirInfo = new DirectoryInfo(_tempDir);
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                // 攻撃の可能性がある。別の tempdir を作り直す。
                _tempDir = null;
                throw new IOException(
                    $"Refusing to use reparse point as temp directory: '{dirInfo.FullName}'. " +
                    $"This may indicate a symlink attack on the shared %TEMP% directory.");
            }
        }

        // GUID プレフィックスで同名ファイルの衝突を防止
        var tempFileName = $"{Guid.NewGuid():N}_{Path.GetFileName(sourcePath)}";
        var tempPath = Path.Combine(_tempDir, tempFileName);

        // 共通の 1MB バッファで write syscall 回数を削減
        const int bufferSize = FileSystemHelper.DefaultBufferSize;
        using var src = Io.Open(sourcePath, FileShare.ReadWrite | FileShare.Delete);
        using var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: bufferSize);

        var srcLength = src.Length;
        if (srcLength > 0)
        {
            dst.SetLength(srcLength);
            src.CopyTo(dst, bufferSize);
            // ソースが縮小した場合に末尾ゼロ埋めを除去
            if (dst.Position != srcLength)
                dst.SetLength(dst.Position);
        }

        return tempPath;
    }

    /// <summary>
    /// 現在処理中のエンティティを返す。
    /// </summary>
    private RawEntity Current() => (_index >= 0 && _index < _items.Count) ? _items[_index] : null;

    /// <summary>
    /// 7-zip コールバックインデックスを <see cref="_items"/> 内のインデックスに変換する。
    /// </summary>
    /// <param name="index">7-zip の UpdateItems インデックス空間のインデックス。</param>
    /// <returns>
    /// _items 内のインデックス；
    /// 保持アイテム（新しいデータ不要）の場合は -1。
    /// </returns>
    /// <remarks>
    /// 新規作成モード（_plan が null）ではインデックスをそのまま返す。
    /// 更新モードでは UpdatePlan を参照してマッピングを行う。
    /// </remarks>
    private int ResolveItemIndex(uint index)
    {
        // 新規作成モード: コールバックインデックスと _items インデックスは同一
        if (_plan is null) return (int)index;
        // 更新モード: プランのエントリから _items インデックスを取得する（保持は -1）
        return _plan.Entries[index].NewItemIndex;
    }

    /// <summary>
    /// アイテムインデックスが変化した場合に進捗カウンタを更新する。
    /// </summary>
    /// <param name="index">現在の 7-zip コールバックインデックス。</param>
    /// <returns>インデックスが変化した場合は true；変化なしの場合は false。</returns>
    private bool UpdateItemProgress(uint index)
    {
        var i = (int)index;
        if (_processedItemIndex != i)
        {
            // 新しいアイテムに移ったので処理済みカウントを更新する
            _processedItemIndex = i;
            Count = i + 1;
            return true;
        }
        return false;
    }

    #endregion

    #region Fields
    // 開いた読み取りストリームのリスト（Dispose 時に全て解放する）
    private readonly List<ArchiveStreamReader> _streams = [];

    // 新規/置換アイテムのリスト（_plan が null の場合は全アイテム、非 null の場合は新規/置換のみ）
    private readonly IList<RawEntity> _items;
    // 更新プラン（null = 新規作成モード、非 null = 更新モード）
    private readonly UpdatePlan _plan;
    // 現在処理中のアイテムを指す _items インデックス（未処理は -1）
    private int _index = -1;
    // ロック中ファイルの一時コピー先ディレクトリ（遅延作成、Dispose 時に削除）
    private string _tempDir;
    // 最後に処理したコールバックインデックス（UpdateItemProgress の重複検出用）
    private int _processedItemIndex = -1;
    // SetCompleted で受領した completeValue の単調最大値（グローバル累積の絶対進捗位置）
    private long _maxCompletedBytes;
    // SetCompleted の並行呼び出し (ZIP Ultra 等のマルチスレッド圧縮) で _maxCompletedBytes を
    // 保護するロック
    private readonly object _completedLock = new();
    // 最後に進捗を報告した TickCount64（スロットリング用）
    private long _lastReportedTicks;
    // 進捗報告の最小間隔は CallbackBase.ReportIntervalMs に集約
    #endregion
}
