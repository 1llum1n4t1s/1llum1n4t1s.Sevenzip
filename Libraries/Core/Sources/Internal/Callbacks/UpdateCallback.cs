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
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;
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
    /// 更新元の既存アーカイブ (更新モードのみ) を取得または設定する。
    /// </summary>
    /// <remarks>
    /// rename エントリの Path 以外のプロパティを元エントリから返すために使う。
    /// 所有権は持たない (解放は ArchiveWriter.UpdateCore の後始末が行う)。
    /// null の場合、rename エントリの Path 以外のプロパティは空値を返す。
    /// </remarks>
    public IInArchive Source { get; init; }

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
        // _maxCompletedBytes と Bytes の更新を同一 lock で保護する（Bytes 代入を lock 外に
        // 置くと、古いスナップショットが後から書き戻されて進捗が後退しうる）。
        // lock 範囲は小さく保ち、Report 呼び出しは lock 外で行う (再入デッドロック回避)。
        lock (_completedLock)
        {
            if (value > _maxCompletedBytes) _maxCompletedBytes = value;

            // 100% 超過で進捗 UI がおかしくならないよう TotalBytes を上限とする
            // (completeValue は complexity 尺度でエントリ毎のヘッダ定数等を含むため、
            //  純粋なファイルサイズ合計である TotalBytes を僅かに超えることがある)
            Bytes = TotalBytes > 0 && _maxCompletedBytes > TotalBytes ? TotalBytes : _maxCompletedBytes;
        }

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
                // Path は新しい名前に差し替える。
                if (pid == ItemPropId.Path)
                {
                    value.Set(entry.RenameTo);
                    return (int)SevenZipCode.Success;
                }

                // Path 以外も空値を返してはならない: rename は newprop=1 で返すため、7-Zip は
                // プロパティを既存アーカイブから補完せず、このコールバックの値だけで項目を組み立てる
                // (7z: mtime/ctime/atime/attrib が消える、ZIP: 時刻 0・IsDir=false になる)。
                // 本家 UI (UI/Common/UpdateCallback.cpp) と同じく、元書庫の該当エントリの値を返す。
                return GetSourceProperty(entry.OriginalIndex, pid, ref value);
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
                // 未知のプロパティ ID は空値を返す
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

        // 最後に開いたエントリとして記録する (対応する SetOperationResult と進捗が読む)。
        // 7-Zip は GetStream(k) → SetOperationResult(k) → GetStream(k+1) を直列に呼ぶので、
        // 次の GetStream までの間はこの値が「処理中のエントリ」を一意に指す。
        // 保持アイテム (resolved < 0) でも -1 を書き、前エントリの値を持ち越さない
        // (持ち越すと保持アイテムの SetOperationResult が無関係な新規エントリを報告する)。
        Volatile.Write(ref _openedIndex, resolved);

        if (resolved < 0)
        {
            // 保持アイテムの場合: 7-zip が既存アーカイブから直接コピーするためストリーム不要
            stream = null;
            return (int)SevenZipCode.Success;
        }

        var dest = default(ISequentialInStream);
        var src  = Current(resolved);

        // FileCompressing イベントを発火する（ファイル読み取りの直前）
        if (FireFileEvent(OnFileStarted, src, resolved))
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
        // 処理開始時に index と entity をローカルへ確定し、イベント・例外・進捗で同じ組を使う。
        // 呼び出しスレッドは対応する GetStream と異なりうる (7z の LZMA2 ブロック MT では
        // k の終了が k+1 を開くスレッドから届く) ため、スレッド単位ではなくインスタンスで
        // 保持している「最後に開いたエントリ」を読む。
        // ここで -1 に戻す (消費する) ことはしない: エントリ間に届く SetCompleted の
        // 進捗 (Current()) が null になり、進捗表示の対象ファイルが途切れるため。
        var index = Volatile.Read(ref _openedIndex);
        var current = Current(index);
        // Stream 早期解放の設計メモ (検証済の現時点での結論):
        // 以下の 2 案で早期解放を試みたがどちらも破綻したため、Dispose 時一括解放に集約している:
        //
        // 案 A: SetOperationResult で _currentStream を即時 Dispose
        //   → ZIP のマルチスレッド圧縮 (ZIP Ultra 等) では GetStream / SetOperationResult 自体は
        //      直列だが、SetOperationResult は入力を圧縮スレッドへ渡した時点で呼ばれ、
        //      ストリームの使用期間はエントリ間で重なる。_currentStream が次エントリで上書きされ、
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
        if (FireFileEvent(OnFileFinished, current, index))
            return (int)SevenZipCode.Cancel;

        if (code == SevenZipCode.Success) return Report(ProgressState.Success, current);

        // 失敗は Exceptions へ積んでから Failed を報告する（ExtractCallback.SetOperationResult と同じ規約）。
        // Report は Failed で Cancel=true を立て 7z.dll へ Cancel コードを返すため、積んでおかないと
        // ThrowIfError が「純粋なユーザーキャンセル」と誤判定して OperationCanceledException を投げ、
        // 失敗コードと対象エントリ名が失われる。
        var error = new SevenZipException(code);
        // 入力 Stream の元例外が既に積まれている場合は汎用例外で覆い隠さない。
        if (!HasExceptions) PushException(error);
        return Report(error, current);
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
        // 共有可変状態は規約どおり _stateLock 配下でスナップショットを取ってから触る
        // (_streams / _tempDir はコールバックスレッドを跨いで書かれるため)。
        ArchiveStreamReader[] streams;
        string tempDir;
        lock (_stateLock)
        {
            streams = [.. _streams];
            _streams.Clear();
            tempDir = _tempDir;
            _tempDir = null;
        }

        List<Exception> errors = null;
        if (disposing)
        {
            // ここで一括解放する。詳細は SetOperationResult のコメントを参照。
            foreach (var stream in streams)
            {
                try { stream.Dispose(); }
                catch (Exception e) { (errors ??= []).Add(e); }
            }
        }

        // ロック中ファイルの一時コピーを削除する。
        // finalizer 経路 (disposing == false) では上のストリーム解放を行わないため、
        // 一時コピーのハンドルが開いたままで削除は失敗する (下の catch に落ちる)。
        // この経路は Dispose 漏れ時のみで、両方の呼び出し元が後始末で必ず破棄する。
        if (disposing)
        {
            if (tempDir is not null && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); }
                catch (Exception e) { (errors ??= []).Add(e); }
            }
        }
        else
        {
            try
            {
                if (tempDir is not null && Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch { /* finalizer から例外を漏らさない */ }
        }

        if (errors is { Count: 1 }) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors is { Count: > 1 }) throw new AggregateException("Multiple resources failed to dispose.", errors);
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
            // まず通常モード（FileShare.Read）で開く。
            // Add 時点の「再解析ポイントではない」というスナップショットは保存時には古い可能性がある
            // (Add 後にファイルがシンボリックリンク等へ差し替えられると、通常オープンはリンクを
            // 辿って追加元ツリー外のファイルを書庫へ取り込む)。開いた実体を検証してから使う。
            stream = OpenVerified(src.FullName, null);
        }
        catch (IOException ex) when (FileSystemHelper.IsFileLocked(ex))
        {
            // ロック中 → 一時コピーを作成してコピーを開く
            var tempPath = CopyLockedFile(src.FullName);
            stream = File.OpenRead(tempPath);
        }

        // ストリームを開き、Dispose 時に解放できるようリストに登録する。
        // GetStream 自体は 7-Zip が直列に呼ぶが、呼び出しスレッドはエントリごとに変わり、
        // マルチスレッド圧縮 (ThreadCount > 1) では Dispose 等の他経路と同時に触られうる。
        // 規約として _streams は _stateLock 配下でのみ読み書きする (無同期の List<T>.Add は
        // 登録漏れで Dispose の一括解放から外れ、write ロックが GC まで残留しうる)。
        var dest = new ArchiveStreamReader(stream, dispose: true, ErrorHandler);
        lock (_stateLock) _streams.Add(dest);
        return dest;
    }

    /// <summary>
    /// ロック中のファイルを一時ディレクトリにコピーし、コピー先パスを返す。
    /// FileShare.ReadWrite で開くことでロック中でも読み取れる。
    /// </summary>
    private string CopyLockedFile(string sourcePath)
    {
        // 一時ディレクトリを遅延作成（乱数名で衝突回避）。
        // GetStream → Open → ここは 7-Zip が直列に呼ぶが、呼び出しスレッドは変わりうるため
        // _tempDir は _stateLock 配下でのみ読み書きする (check-then-act をロックで囲む)。
        // 仮に 2 経路が同時に別ディレクトリを作ると後勝ちで _tempDir が上書きされ、
        // 先のディレクトリが Dispose の削除対象から漏れてロック中ファイルの平文コピーが
        // %TEMP% に恒久残留する。
        string tempDir;
        lock (_stateLock)
        {
            if (_tempDir is null)
            {
                var candidate = Path.Combine(Path.GetTempPath(), $"SevenZip_{Guid.NewGuid():N}");
                Directory.CreateDirectory(candidate);

                // RDS/Citrix 等の共有 %TEMP% 環境で攻撃者がシンボリックリンクを事前配置している
                // 可能性を排除するため、作成したディレクトリが実ディレクトリであることを検証する。
                // Directory.CreateDirectory は既存ディレクトリ (リパースポイント含む) でも例外を
                // 投げないため、明示的に Attributes でチェックする必要がある。
                var dirInfo = new DirectoryInfo(candidate);
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    // 攻撃の可能性がある。_tempDir へは代入しないので次回は作り直しになる。
                    throw new IOException(
                        $"Refusing to use reparse point as temp directory: '{dirInfo.FullName}'. " +
                        $"This may indicate a symlink attack on the shared %TEMP% directory.");
                }

                _tempDir = candidate;
            }
            tempDir = _tempDir;
        }

        // GUID プレフィックスで同名ファイルの衝突を防止
        var tempFileName = $"{Guid.NewGuid():N}_{Path.GetFileName(sourcePath)}";
        var tempPath = Path.Combine(tempDir, tempFileName);

        // 共通の 1MB バッファで write syscall 回数を削減
        const int bufferSize = FileSystemHelper.DefaultBufferSize;
        // ロック中ファイルのコピー元も通常経路と同じく再解析ポイント経由でないことを検証する。
        using var src = OpenVerified(sourcePath, FileShare.ReadWrite | FileShare.Delete);
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
    /// rename エントリの Path 以外のプロパティを、更新元アーカイブの該当エントリから取得する。
    /// </summary>
    /// <param name="originalIndex">更新元アーカイブ内のインデックス。</param>
    /// <param name="pid">取得するプロパティ ID。</param>
    /// <param name="value">取得した値 (所有権は呼び出し元の 7-Zip へ移る)。</param>
    /// <returns>操作結果コード。</returns>
    /// <remarks>
    /// <para>
    /// 7-Zip の UpdateItems は項目情報 (GetUpdateItemInfo / GetProperty) を圧縮開始前に
    /// 呼び出し元スレッドで順に収集するため、ここで更新元ハンドラーへ再入しても
    /// 圧縮ワーカーとは競合しない (本家 UI の CArchiveUpdateCallback も同じ再入を行う)。
    /// </para>
    /// <para>
    /// 生成 COM ラッパーの <see cref="IInArchive.GetProperty"/> は失敗 HRESULT を例外に変換する。
    /// COM 境界へ例外を漏らさず、<see cref="CallbackBase.PushException"/> で積んでから
    /// エラーコードを返す (積まないと ThrowIfError がキャンセル扱いに化かす)。
    /// </para>
    /// </remarks>
    private int GetSourceProperty(uint originalIndex, ItemPropId pid, ref PropVariant value)
    {
        if (Source is null)
        {
            value.Clear();
            return (int)SevenZipCode.Success;
        }

        try
        {
            Source.GetProperty(originalIndex, pid, ref value);
            return (int)SevenZipCode.Success;
        }
        catch (Exception e) when (!IsFatalException(e))
        {
            PushException(e);
            return e is SevenZipException se ? (int)se.Code : (int)SevenZipCode.UnknownError;
        }
    }

    /// <summary>
    /// 入力ファイルを開き、パスの実体 (再解析ポイントを辿らない) と同じファイルであることを検証する。
    /// </summary>
    /// <param name="path">開くファイルのパス。</param>
    /// <param name="share">共有モード。null の場合は <see cref="Io.Open(string)"/> の既定 (FileShare.Read)。</param>
    /// <returns>検証済みの読み取りストリーム。</returns>
    /// <exception cref="IOException">
    /// パスが再解析ポイント・ディレクトリになっている、または開いたファイルがパスの実体と異なる場合。
    /// </exception>
    /// <remarks>
    /// <para>
    /// 開く処理自体は <see cref="Io"/> 抽象を通し (テストの I/O 差し替えと共有違反判定を維持)、
    /// その後 <c>FILE_FLAG_OPEN_REPARSE_POINT</c> でパスの末端そのものを開いて
    /// (1) 再解析ポイント・ディレクトリでないこと、(2) ボリュームシリアル番号とファイル ID が
    /// 開いたストリームと一致すること、を確認する。(2) により「開いた時点はリンクで、検査時点で
    /// 実ファイルへ戻された」といった差し替え競合も検出できる。
    /// </para>
    /// <para>
    /// 検出は保存の失敗として扱う (呼び出し元の Run が Exceptions に積む)。
    /// <see cref="CompressionOption.SkipInaccessibleFiles"/> は Add 時点の設定であり、
    /// 保存中の検出を黙示スキップへ変換しない。
    /// </para>
    /// <para>
    /// 制約: 検証対象はパスの末端要素だけで、途中のディレクトリがジャンクションへ
    /// 差し替えられた場合は検出しない (Add のルートを保持していないため、
    /// どこまでが追加元ツリーかを判定できない)。
    /// </para>
    /// </remarks>
    private static FileStream OpenVerified(string path, FileShare? share)
    {
        var stream = share is { } s ? Io.Open(path, s) : Io.Open(path);
        try
        {
            ThrowIfRedirected(stream, path);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 開いたストリームが、パスの末端 (再解析ポイントを辿らない) と同一の通常ファイルかを検証する。
    /// </summary>
    private static void ThrowIfRedirected(FileStream stream, string path)
    {
        var full = Path.GetFullPath(path);
        using var probe = Kernel32.NativeMethods.CreateFile(
            ToExtendedPath(full),
            FileReadAttributes,
            FileShareAll,
            IntPtr.Zero,
            OpenExisting,
            // OPEN_REPARSE_POINT: リンクを辿らずリンク自体を開く。
            // BACKUP_SEMANTICS: ディレクトリ (ジャンクションを含む) に差し替えられていても開けるようにする。
            FileFlagOpenReparsePoint | FileFlagBackupSemantics,
            IntPtr.Zero);
        if (probe.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            throw new IOException($"Failed to verify the file to archive: '{full}'.",
                HResultFromWin32(error));
        }

        if (!Native.GetFileInformationByHandle(probe, out var expected))
            throw new IOException($"Failed to query the file to archive: '{full}'.",
                HResultFromWin32(Marshal.GetLastPInvokeError()));

        var attributes = (FileAttributes)expected.FileAttributes;
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Refusing to archive reparse point: '{full}'.");
        if ((attributes & FileAttributes.Directory) != 0)
            throw new IOException($"Refusing to archive '{full}' because it was replaced by a directory after it was added.");

        if (!Native.GetFileInformationByHandle(stream.SafeFileHandle, out var actual))
            throw new IOException($"Failed to query the opened file: '{full}'.",
                HResultFromWin32(Marshal.GetLastPInvokeError()));

        if (expected.VolumeSerialNumber != actual.VolumeSerialNumber ||
            expected.FileIndexHigh      != actual.FileIndexHigh ||
            expected.FileIndexLow       != actual.FileIndexLow)
        {
            throw new IOException(
                $"Refusing to archive '{full}' because the opened file is not the file at that path " +
                "(it may have been replaced by a link after it was added).");
        }
    }

    /// <summary>
    /// 絶対パスを長いパス対応の拡張形式 (<c>\\?\</c>) に変換する。
    /// </summary>
    private static string ToExtendedPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + path[2..]
            : @"\\?\" + path;
    }

    /// <summary>
    /// Win32 エラーコードを HRESULT に変換する (FileSystemHelper.IsFileLocked の判定と揃える)。
    /// </summary>
    private static int HResultFromWin32(int error) =>
        error <= 0 ? unchecked((int)0x80004005) : unchecked((int)(0x80070000u | (uint)(error & 0xFFFF)));

    /// <summary>
    /// 最後に開いた (GetStream した) エンティティを返す。
    /// </summary>
    /// <remarks>
    /// SetCompleted はマルチスレッド圧縮で並行に呼ばれるため、<see cref="_openedIndex"/> は
    /// Volatile で読む。自分が解決したインデックスを持っている呼び出し元は
    /// <see cref="Current(int)"/> を使う (読み直すと次の GetStream の値を拾いうる)。
    /// </remarks>
    private RawEntity Current()
    {
        return Current(Volatile.Read(ref _openedIndex));
    }

    /// <summary>
    /// 指定したインデックスのエンティティを返す。範囲外の場合は null を返す。
    /// </summary>
    private RawEntity Current(int index) =>
        (index >= 0 && index < _items.Count) ? _items[index] : null;

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

    /// <summary>
    /// 入力ファイルの同一性検証に使う Win32 API。
    /// </summary>
    /// <remarks>
    /// CreateFile は共有の <see cref="Kernel32.NativeMethods.CreateFile"/> を使い、ここでは
    /// ファイル ID 取得 (GetFileInformationByHandle) だけを宣言する。
    /// </remarks>
    private static partial class Native
    {
        /// <summary>BY_HANDLE_FILE_INFORMATION 構造体。</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct ByHandleFileInformation
        {
            public uint FileAttributes;
            public uint CreationTimeLow;
            public uint CreationTimeHigh;
            public uint LastAccessTimeLow;
            public uint LastAccessTimeHigh;
            public uint LastWriteTimeLow;
            public uint LastWriteTimeHigh;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        /// <summary>
        /// 開いたハンドルの属性・ボリュームシリアル番号・ファイル ID を取得する。
        /// </summary>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetFileInformationByHandle(
            Microsoft.Win32.SafeHandles.SafeFileHandle file,
            out ByHandleFileInformation information);
    }

    #endregion

    #region Fields
    // 入力ファイル検証用の CreateFile 引数 (FILE_READ_ATTRIBUTES は共有モードの衝突判定を受けない)
    private const uint FileReadAttributes       = 0x00000080;
    private const uint FileShareAll             = 0x00000001 | 0x00000002 | 0x00000004;
    private const uint OpenExisting             = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics  = 0x02000000;

    // 開いた読み取りストリームのリスト（Dispose 時に全て解放する）
    private readonly List<ArchiveStreamReader> _streams = [];

    // 新規/置換アイテムのリスト（_plan が null の場合は全アイテム、非 null の場合は新規/置換のみ）
    private readonly IList<RawEntity> _items;
    // 更新プラン（null = 新規作成モード、非 null = 更新モード）
    private readonly UpdatePlan _plan;
    // 最後に GetStream したアイテムを指す _items インデックス（未処理・保持アイテムは -1）。
    // 7-Zip は GetStream / SetOperationResult を全形式で直列に呼ぶ (GetStream(k) →
    // SetOperationResult(k) → GetStream(k+1)) が、呼び出しスレッドは対にならない
    // (7z の LZMA2 ブロック MT では k の終了が k+1 を開くスレッドから届く)。
    // ThreadLocal にするとスレッドを跨いで届く SetOperationResult がエントリを取り違える
    // (Target=null や数件前のファイル) ため、インスタンスで 1 つだけ保持する。
    // 並行に呼ばれる SetCompleted からも読むので Volatile で読み書きする。
    private int _openedIndex = -1;
    // ロック中ファイルの一時コピー先ディレクトリ（遅延作成、Dispose 時に削除）
    private string _tempDir;
    // 最後に処理したコールバックインデックス（UpdateItemProgress の重複検出用）
    private int _processedItemIndex = -1;
    // SetCompleted で受領した completeValue の単調最大値（グローバル累積の絶対進捗位置）
    private long _maxCompletedBytes;
    // SetCompleted の並行呼び出し (ZIP Ultra 等のマルチスレッド圧縮) で _maxCompletedBytes を
    // 保護するロック
    private readonly Lock _completedLock = new();
    // コールバックスレッドを跨いで触られる共有可変状態 (_streams / _tempDir) を保護するロック
    // (GetStream 自体は直列だが呼び出しスレッドはエントリごとに変わる)
    private readonly Lock _stateLock = new();
    // 最後に進捗を報告した TickCount64（スロットリング用）
    private long _lastReportedTicks;
    // 進捗報告の最小間隔は CallbackBase.ReportIntervalMs に集約
    #endregion
}
