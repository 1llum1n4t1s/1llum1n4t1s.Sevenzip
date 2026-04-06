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
    /// 7-zip はファイルごとに 0 から当該ファイルサイズまでをリセットして報告する場合がある。
    /// 値のリセットを検出して累積バイト数を計算することで常に全体の進捗を報告する。
    /// </remarks>
    public int SetCompleted(IntPtr bytes)
    {
        var value = bytes != IntPtr.Zero ? Marshal.ReadInt64(bytes) : 0L;

        // 値が前回より小さくなった場合は次のファイルに移った（リセットされた）と判断し、
        // 前回のファイルのバイト数を累積値に加算する
        if (value < _lastCompletedBytes)
            _cumulativeBytes += _lastCompletedBytes;
        _lastCompletedBytes = value;

        // 累積値と現在のファイルの進捗を合算して全体の処理済みバイト数とする
        Bytes = _cumulativeBytes + value;

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
        // 全ての読み取りストリームを解放する
        foreach (var stream in _streams) stream.Dispose();
        _streams.Clear();
    }

    #endregion

    #region Implementations

    /// <summary>
    /// 指定したエンティティのファイルを開いて読み取りストリームを返す。
    /// </summary>
    private ArchiveStreamReader Open(Entity src)
    {
        // ディレクトリまたは存在しないファイルはストリーム不要
        if (!src.Exists || src.IsDirectory) return null;

        // ストリームを開き、Dispose 時に解放できるようリストに登録する
        var dest = new ArchiveStreamReader(Io.Open(src.FullName));
        _streams.Add(dest);
        return dest;
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
    // 最後に処理したコールバックインデックス（UpdateItemProgress の重複検出用）
    private int _processedItemIndex = -1;
    // 7-zip のバイト報告がリセットされた場合の累積バイト数
    private long _cumulativeBytes = 0L;
    // 前回の SetCompleted で受け取ったバイト数（リセット検出用）
    private long _lastCompletedBytes = 0L;
    // 最後に進捗を報告した TickCount64（スロットリング用）
    private long _lastReportedTicks;
    // 進捗報告の最小間隔（ミリ秒）
    private const long ReportIntervalMs = 50;
    #endregion
}
