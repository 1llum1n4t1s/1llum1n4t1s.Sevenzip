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
namespace Cube.FileSystem.SevenZip;

/// <summary>
/// 圧縮・解凍処理の進捗報告機能を提供する基底クラス。
/// </summary>
internal abstract class CallbackBase : DisposableBase
{
    #region Constructors

    /// <summary>
    /// 指定した引数で CallbackBase クラスの新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="aggregator">進捗報告を受け取るオブジェクト。null の場合は報告しない。</param>
    protected CallbackBase(IProgress<Report> aggregator) => _inner = aggregator;

    #endregion

    #region Properties

    /// <summary>
    /// 処理済みファイル数を取得または設定する。
    /// </summary>
    public long Count { get; protected set; }

    /// <summary>
    /// 処理対象の総ファイル数を取得または設定する。
    /// </summary>
    public long TotalCount { get; protected set; }

    /// <summary>
    /// 処理済みバイト数を取得または設定する。
    /// </summary>
    public long Bytes { get; protected set; }

    /// <summary>
    /// 処理対象の総バイト数を取得または設定する。
    /// </summary>
    public long TotalBytes { get; protected set; }

    /// <summary>
    /// 処理中に発生した例外のスタックを取得する。
    /// </summary>
    /// <remarks>
    /// 例外は LIFO 順で積まれる。呼び出し元は ThrowIfError() 等で確認すること。
    /// </remarks>
    public Stack<Exception> Exceptions { get; } = new();

    #endregion

    #region Methods

    #region Report

    /// <summary>
    /// 指定した引数で進捗を報告する。
    /// </summary>
    /// <param name="state">進捗の状態。</param>
    /// <param name="entity">処理対象のアイテム。</param>
    /// <returns>
    /// <see cref="Report.Cancel"/> が true の場合は Cancel コード；それ以外は None。
    /// </returns>
    protected int Report(ProgressState state, Entity entity) =>
        Report(Make(state, entity, null));

    /// <summary>
    /// エラー情報を含む進捗を報告する。
    /// </summary>
    /// <param name="error">発生した例外オブジェクト。</param>
    /// <param name="entity">処理対象のアイテム。</param>
    /// <returns>
    /// <see cref="Report.Cancel"/> が true の場合は Cancel コード；それ以外は None。
    /// </returns>
    protected int Report(Exception error, Entity entity) =>
        Report(Make(ProgressState.Failed, entity, error));

    /// <summary>
    /// 指定した Report オブジェクトで進捗を報告する。
    /// </summary>
    /// <param name="src">報告するレポートオブジェクト。</param>
    /// <returns>
    /// <see cref="Report.Cancel"/> が true の場合は Cancel コード；それ以外は Success。
    /// </returns>
    protected int Report(Report src)
    {
        // null チェックにより aggregator が設定されていない場合は何もしない
        _inner?.Report(src);
        // キャンセル要求があれば Cancel コードを返し、7-zip に処理中断を伝える
        return (int)(src.Cancel ? SevenZipCode.Cancel : SevenZipCode.Success);
    }

    #endregion

    #region Run

    /// <summary>
    /// 指定した関数を実行し、進捗を報告する。
    /// </summary>
    /// <param name="func">実行するユーザー関数。</param>
    /// <param name="state">完了時に報告する進捗の状態。</param>
    /// <param name="entity">処理対象のアイテム。</param>
    /// <returns>
    /// キャンセル要求があった場合は Cancel コード；
    /// それ以外は <paramref name="func"/> の戻り値。
    /// </returns>
    protected int Run(Func<int> func, ProgressState state, Entity entity) =>
        Run(func, state, () => entity);

    /// <summary>
    /// 指定した関数を実行し、進捗を報告する。
    /// </summary>
    /// <param name="func">実行するユーザー関数。</param>
    /// <param name="state">完了時に報告する進捗の状態。</param>
    /// <param name="entity">処理対象アイテムを返す関数（遅延評価）。</param>
    /// <returns>
    /// キャンセル要求があった場合は Cancel コード；
    /// それ以外は <paramref name="func"/> の戻り値。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>例外透過性ポリシー</b>: <paramref name="func"/> がスローした全ての <see cref="Exception"/>
    /// は <see cref="Exceptions"/> スタックに積まれ、7-Zip への戻り値は以下のルールに従う:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// aggregator (IProgress&lt;Report&gt;) が存在する場合: <see cref="ProgressState.Failed"/> を
    /// 報告し、ハンドラが <see cref="Report.Cancel"/> を設定していれば
    /// <see cref="SevenZipCode.Cancel"/>、そうでなければ <see cref="SevenZipCode.Success"/>
    /// を返す (Failed 状態時は Make が Cancel=true を自動設定するため通常は Cancel)。
    /// </description></item>
    /// <item><description>
    /// aggregator が null の場合: 例外が <see cref="SevenZipException"/> なら
    /// <see cref="SevenZipException.Code"/> を、それ以外は
    /// <see cref="SevenZipCode.UnknownError"/> を返す。
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>例外の再スローは行わない</b>。これは COM 境界を越えて .NET 例外を伝播させない
    /// ための意図的な設計であり、呼び出し元 (ArchiveReader / ArchiveWriter) は処理完了後に
    /// <see cref="Exceptions"/> スタックを確認することで元の例外を再スロー可能。
    /// 詳細は <see cref="ArchiveReader"/> / <see cref="ArchiveWriter"/> の実装参照。
    /// </para>
    /// <para>
    /// <b>注意:</b> <see cref="OutOfMemoryException"/> / <see cref="StackOverflowException"/>
    /// 等の corrupted state exception も現状は catch される。将来的にこれらを再スローする
    /// 挙動に変更する可能性があるため、呼び出し側は Exceptions スタックに積まれた例外の
    /// 型をチェックして fatal 例外なら即座に再スローするのが望ましい。
    /// </para>
    /// </remarks>
    protected int Run(Func<int> func, ProgressState state, Func<Entity> entity)
    {
        try
        {
            // ユーザー関数を実行して 7-zip の操作結果コードを取得する
            var c0 = func();
            // 完了を報告してキャンセルコードを取得する
            var c1 = Report(state, entity());
            // キャンセルが優先される（ユーザーが中断要求した場合はキャンセルを伝播する）
            return c1 == (int)SevenZipCode.Cancel ? c1 : c0;
        }
        catch (Exception e)
        {
            // 例外を Exceptions スタックに積み、COM 境界を越えて .NET 例外を伝播させない。
            // 呼び出し元は処理完了後に Exceptions を確認することで元の例外を再スロー可能。
            Exceptions.Push(e);
            // aggregator が存在する場合は失敗を報告してコールバックを継続させる
            if (_inner is not null) return Report(e, entity());
            // aggregator なしの場合は例外の種類に応じたエラーコードを返す
            if (e is SevenZipException se) return (int)se.Code;
            return (int)SevenZipCode.UnknownError;
        }
    }

    #endregion

    /// <summary>
    /// 2つのパスを結合する。
    /// </summary>
    /// <param name="s0">基準となるパス（空の場合は <paramref name="s1"/> をそのまま返す）。</param>
    /// <param name="s1">結合するパス。</param>
    /// <returns>結合されたパス。</returns>
    protected string Combine(string s0, string s1) => !s0.HasValue() ? s1 : Io.Combine(s0, s1);


    /// <summary>
    /// 進捗報告の最小間隔 (ミリ秒)。
    /// </summary>
    /// <remarks>
    /// ExtractCallback / UpdateCallback で共有する進捗報告の最小間隔。
    /// 50ms = 毎秒 20 回の報告頻度、UI の視覚的スムーズさとコールバックオーバーヘッドのバランスで決定。
    /// </remarks>
    protected const long ReportIntervalMs = 50L;


    /// <summary>
    /// per-file イベントを発火し、ハンドラからキャンセル要求があれば true を返す共通ヘルパー。
    /// </summary>
    /// <param name="handler">発火するハンドラ (null なら何もしない)。</param>
    /// <param name="target">イベント対象の Entity。</param>
    /// <param name="index">エントリのインデックス (不明時は -1)。</param>
    /// <returns>ハンドラが Cancel=true を設定した場合、または例外をスローした場合 true。</returns>
    protected bool FireFileEvent(System.Action<ArchiveFileEventArgs> handler, Entity target, int index)
    {
        if (handler is null) return false;
        var args = new ArchiveFileEventArgs
        {
            Target     = target,
            Index      = index,
            Count      = Count,
            TotalCount = TotalCount,
        };
        try { handler(args); }
        catch (System.Exception ex)
        {
            Exceptions.Push(ex);
            return true;
        }
        return args.Cancel;
    }

    #endregion

    #region Implementations

    /// <summary>
    /// 指定した引数から Report オブジェクトを生成する。
    /// </summary>
    /// <param name="state">進捗の状態。</param>
    /// <param name="entity">処理対象のアイテム。</param>
    /// <param name="error">発生した例外。null の場合はエラーなし。</param>
    /// <returns>現在の進捗情報を含む Report オブジェクト。</returns>
    private Report Make(ProgressState state, Entity entity, Exception error) => new()
    {
        // Failed 状態の場合はキャンセルフラグを立てて処理を中断させる
        Cancel     = state == ProgressState.Failed,
        State      = state,
        Exception  = error,
        Target     = entity,
        // 現在の進捗カウンタのスナップショットをレポートに埋め込む
        Bytes      = Bytes,
        Count      = Count,
        TotalBytes = TotalBytes,
        TotalCount = TotalCount,
    };

    #endregion

    #region Fields
    // 進捗通知の受信者。null の場合は報告をスキップする。
    private readonly IProgress<Report> _inner;
    #endregion
}
