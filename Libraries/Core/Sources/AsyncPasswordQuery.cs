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
using System;
using System.Threading;
using System.Threading.Tasks;
namespace Cube.FileSystem.SevenZip;

/// <summary>
/// 非同期でパスワードを要求するハンドラを <see cref="IQuery{T}"/> として公開する実装。
/// </summary>
/// <remarks>
/// <para>
/// 7-Zip COM コールバック (<c>ICryptoGetTextPassword</c>) は同期呼び出しを要求するため、
/// 内部で <c>Task.Run</c> 上でハンドラを走らせ、<c>GetAwaiter().GetResult()</c> で同期待機する。これにより
/// 呼び出し元が UI スレッドで <c>ContentDialog.ShowAsync()</c> 等を扱う際のデッドロックを
/// 緩和する。
/// </para>
/// <para>
/// <b>前提条件 (重要):</b> 本クラスは <see cref="ArchiveReader"/> を **バックグラウンド
/// スレッド (Task.Run 配下)** から呼ぶ使い方を前提としている。UI スレッドや専用の
/// SynchronizationContext を持つスレッドから直接 <see cref="ArchiveReader.Save(string)"/> を
/// 呼ぶと、<c>GetAwaiter().GetResult()</c> がそのスレッドをブロックするためデッドロック
/// に陥る。
/// </para>
/// <para>
/// <b>UI への dispatch は呼び出し側の責務:</b> WinUI では
/// <c>DispatcherQueue.TryEnqueue</c> + <see cref="TaskCompletionSource{TResult}"/> で
/// UI スレッドに戻してから <c>ShowAsync()</c> を await する。
/// </para>
/// <para>
/// <b>複数回呼び出しに注意:</b> 7-Zip はパスワード誤入力時に同じハンドラを
/// 複数回呼ぶことがある。ハンドラは毎回独立した Dialog を開ける設計にしておくこと
/// (一回限りの <c>TaskCompletionSource</c> を使い回すと 2 回目以降で null が返る)。
/// </para>
/// <para>
/// パスワード取得結果:
/// <list type="bullet">
/// <item><description>ハンドラが非 null/非空文字列を返す → <c>message.Value</c> にセット、<c>Cancel=false</c></description></item>
/// <item><description>ハンドラが null/空文字列を返す → <c>message.Cancel=true</c>（キャンセル扱い）</description></item>
/// <item><description>ハンドラが例外を投げる or CancellationToken により中断される → <c>message.Cancel=true</c></description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class AsyncPasswordQuery : IQuery<string>
{
    #region Constructors

    /// <summary>
    /// 指定した非同期ハンドラで <see cref="AsyncPasswordQuery"/> の新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="handler">
    /// パスワードを返す非同期デリゲート。受け取る CancellationToken は <see cref="CancellationToken.None"/>。
    /// </param>
    public AsyncPasswordQuery(Func<CancellationToken, Task<string>> handler)
        : this(handler, CancellationToken.None) { }

    /// <summary>
    /// 指定した非同期ハンドラとキャンセルトークンで初期化する。
    /// </summary>
    /// <param name="handler">パスワードを返す非同期デリゲート。</param>
    /// <param name="cancellationToken">ハンドラに伝播するキャンセルトークン。</param>
    public AsyncPasswordQuery(Func<CancellationToken, Task<string>> handler, CancellationToken cancellationToken)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _cancellationToken = cancellationToken;
    }

    #endregion

    #region Properties

    /// <summary>
    /// 同期コンテキストが捕捉された状態でのブロックを許可するかどうかを取得または設定する。
    /// </summary>
    /// <remarks>
    /// 既定は <c>false</c>。<see cref="Request"/> は <see cref="SynchronizationContext.Current"/> が
    /// 捕捉されているとデッドロックを避けるため <see cref="InvalidOperationException"/> を投げる。
    /// ブロックしても継続が進むと分かっているカスタムコンテキストでのみ <c>true</c> にする。
    /// 通常は代わりに <c>Task.Run</c> で ArchiveReader / ArchiveWriter の処理全体を包む。
    /// </remarks>
    public bool AllowBlockingOnCapturedContext { get; init; }

    #endregion

    #region Methods

    /// <summary>
    /// 同期的にパスワードを要求する（7-Zip コールバックからの呼び出し点）。
    /// </summary>
    /// <param name="message">要求メッセージ。結果は Value / Cancel に設定される。</param>
    /// <exception cref="InvalidOperationException">
    /// 呼び出し元で同期コンテキストが捕捉されている場合 (WinUI / WPF / WinForms / Avalonia /
    /// Blazor / Unity / 独自の単一スレッド実装など)。そのスレッドで同期ブロックすると、
    /// ハンドラが同じコンテキストへマーシャルバックした時点でデッドロックするため早期失敗させる。
    /// バックグラウンドスレッド (Task.Run 配下) から呼ぶこと。ブロックしても安全と分かっている
    /// コンテキストでは <see cref="AllowBlockingOnCapturedContext"/> でオプトアウトできる。
    /// </exception>
    public void Request(QueryMessage<string, string> message)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));

        // 同期ブロックが危険なコンテキストからの直接呼び出しを検出してデッドロックを事前に防ぐ。
        var ctx = SynchronizationContext.Current;
        if (!AllowBlockingOnCapturedContext && IsBlockingUnsafe(ctx))
        {
            throw new InvalidOperationException(
                "AsyncPasswordQuery.Request must not be called while a synchronization context is " +
                "captured (detected: " + ctx.GetType().FullName + "). " +
                "Wrap the ArchiveReader/Writer call in Task.Run to avoid deadlock when the handler " +
                "marshals back to that context. If the context is known to be safe to block, set " +
                nameof(AllowBlockingOnCapturedContext) + " to true.");
        }

        try
        {
            // Task.Run でハンドラを別スレッドに切り出して GetResult で同期化する。
            // Task.Run(Func<Task<T>>) オーバーロードにより、ハンドラの戻り値 Task<string>
            // は自動で unwrap されて待機される。
            var value = Task.Run(() => _handler(_cancellationToken), _cancellationToken)
                .GetAwaiter().GetResult();

            if (string.IsNullOrEmpty(value))
            {
                message.Value  = null;
                message.Cancel = true;
            }
            else if (ContainsInvalidControlChar(value))
            {
                // 制御文字 (NUL / LF / CR / TAB 等) はネイティブ 7z.dll 側で誤解される可能性があるため拒否。
                // メッセージにパスワードを含めないようにする。
                Logger.Warn("[AsyncPasswordQuery] handler returned a password containing control characters; rejected.");
                message.Value  = null;
                message.Cancel = true;
            }
            else
            {
                message.Value  = value;
                message.Cancel = false;
            }
        }
        catch (OperationCanceledException)
        {
            message.Value  = null;
            message.Cancel = true;
        }
        catch (Exception ex)
        {
            // ex.Message は握り潰す (ハンドラ内でパスワードをメッセージに含めた場合の漏洩回避)。
            // 型名とスタック位置だけログする。
            Logger.Warn($"[AsyncPasswordQuery] handler threw {ex.GetType().FullName}");
            message.Value  = null;
            message.Cancel = true;
        }
    }

    #endregion

    #region Implementations

    /// <summary>
    /// パスワード文字列に不正な制御文字が含まれるかを判定する。
    /// </summary>
    /// <remarks>
    /// NUL (U+0000) はネイティブ文字列終端扱いされ残りが切り捨てられる。
    /// LF / CR / TAB / その他 U+0001〜U+001F / U+007F もパスワードとしては不正扱い。
    /// 加えて Unicode Category.Control (U+0080〜U+009F, U+2028, U+2029 等) もブロック。
    /// U+200B (ZWSP) / U+200C / U+200D / U+2060 の不可視文字は Format カテゴリなので
    /// ここではブロックしないが、ユーザーが意図せず入れた場合にパスワード比較が狂う可能性がある。
    /// </remarks>
    private static bool ContainsInvalidControlChar(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c < 0x20 || c == 0x7F) return true;
            // Unicode カテゴリで "Control" (Cc) に該当するものも拒否 (U+0080〜U+009F 等)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                == System.Globalization.UnicodeCategory.Control) return true;
        }
        return false;
    }

    /// <summary>
    /// 現在の <see cref="SynchronizationContext"/> 上で同期ブロックするのが危険かを判定する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 危険な条件は「呼び出しスレッドがハンドラの継続に必要かどうか」であり、特定の型名とは
    /// 対応しない。かつては WinUI / WPF / WinForms / ASP.NET Classic を型名の部分一致
    /// (denylist) で検出していたが、Avalonia の <c>AvaloniaSynchronizationContext</c>、
    /// Blazor の <c>RendererSynchronizationContext</c>、Unity、独自の単一スレッド実装は
    /// どれにも一致せず、ガードが無言で無効化されて恒久デッドロックに至っていた。
    /// 安全ゲートに denylist は極性が逆なので、
    /// 「コンテキストが捕捉されていれば危険」を既定とする allowlist 方式へ反転している。
    /// </para>
    /// <para>
    /// 例外は <see cref="SynchronizationContext"/> そのもののインスタンスだけ。既定実装は
    /// <see cref="SynchronizationContext.Post"/> をスレッドプールへ投げるため、ブロックしても
    /// 継続が進み、デッドロックしない。ASP.NET Core は <c>Current</c> が null なので影響を受けない。
    /// 独自だがブロックしても安全なコンテキストでは
    /// <see cref="AllowBlockingOnCapturedContext"/> で明示的にオプトアウトできる。
    /// </para>
    /// </remarks>
    private static bool IsBlockingUnsafe(SynchronizationContext ctx) =>
        ctx is not null && ctx.GetType() != typeof(SynchronizationContext);

    #endregion

    #region Fields
    private readonly Func<CancellationToken, Task<string>> _handler;
    private readonly CancellationToken _cancellationToken;
    #endregion
}
