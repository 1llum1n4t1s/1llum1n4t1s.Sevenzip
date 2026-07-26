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
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
namespace Cube.FileSystem.SevenZip;

/// <summary>
/// アーカイブからファイルを展開するためのコールバック関数を提供する。
/// </summary>
/// <remarks>
/// IArchiveExtractCallback および ICryptoGetTextPassword を実装し、
/// 7-zip の Extract 操作に必要な全コールバックを処理する。
/// </remarks>
[GeneratedComClass]
internal partial class ExtractCallback : PasswordCallback, IArchiveExtractCallback
{
    #region Constructors

    /// <summary>
    /// 指定した引数で ExtractCallback クラスの新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="src">ソースアーカイブのパス（パスワードコールバックに渡す）。</param>
    /// <param name="iterator">展開対象アイテムのイテレータ。</param>
    /// <param name="progress">進捗を報告するオブジェクト。</param>
    public ExtractCallback(string src, ArchiveEnumerator iterator, IProgress<Report> progress) :
        base(src, progress)
    {
        _iterator  = iterator;
        TotalCount = iterator.Count;
        TotalBytes = -1; // SetTotal で初期化されるまで未設定を示す -1
        Count      = 0;
        Bytes      = 0;
    }

    #endregion

    #region Properties

    /// <summary>
    /// 展開したアイテムを保存するディレクトリのパスを取得または設定する。
    /// </summary>
    public string Destination { get; init; } = string.Empty;

    /// <summary>
    /// 指定したファイルまたはディレクトリをフィルタリングするかどうかを判定する関数を
    /// 取得または設定する。
    /// </summary>
    /// <remarks>
    /// 戻り値が true のアイテムはスキップされる。null の場合はフィルタなし。
    /// </remarks>
    public Predicate<Entity> Filter { get; init; }

    /// <summary>
    /// インデックス毎に外部 Stream へ展開するマップを取得または設定する。
    /// </summary>
    /// <remarks>
    /// キーが <see cref="ArchiveEntity.Index"/> と一致するエントリは、
    /// <see cref="Destination"/> 配下にファイルを作成する代わりに対応する
    /// Stream へ直接書き込む（Stream 自体は呼び出し側で所有する）。
    /// </remarks>
    public IReadOnlyDictionary<int, System.IO.Stream> StreamOutputs { get; init; }

    /// <summary>
    /// ファイル展開開始時に呼び出されるイベントハンドラ。null の場合は発火しない。
    /// </summary>
    public Action<ArchiveFileEventArgs> OnFileStarted { get; init; }

    /// <summary>
    /// ファイル展開終了時に呼び出されるイベントハンドラ。null の場合は発火しない。
    /// </summary>
    public Action<ArchiveFileEventArgs> OnFileFinished { get; init; }

    #endregion

    #region IProgress

    /// <summary>
    /// 指定したアイテム群の展開完了時の総バイト数を設定する。
    /// </summary>
    /// <param name="bytes">総バイト数。</param>
    public int SetTotal(ulong bytes)
    {
        // 初回呼び出し時のみ TotalBytes を設定する（Extract を複数回呼んだ場合は保持する）
        if (TotalBytes < 0) TotalBytes = (long)bytes;

        // 複数回 Extract を呼ぶ場合のバイト数の差分（ハック値）を計算する
        // 2回目以降の SetTotal は 1回目より少ない値になる場合があるため補正する
        _hack = Math.Max((long)bytes - TotalBytes, 0);
        return Report(ProgressState.Prepare, Current());
    }

    /// <summary>
    /// 展開済みバイト数を設定する。
    /// </summary>
    /// <param name="bytes">処理済みバイト数を指すポインタ。null の場合は更新しない。</param>
    /// <returns>操作結果コード。</returns>
    /// <remarks>
    /// IInArchive.Extract を複数回呼ぶとフォーマットによって SetTotal と SetCompleted の
    /// 値が異なる場合がある。このメソッドはその差異を正規化する。
    /// </remarks>
    public int SetCompleted(IntPtr bytes)
    {
        if (bytes != IntPtr.Zero)
        {
            // ポインタが有効な場合のみバイト数を更新する
            // _hack を引いてオフセット補正し、[0, TotalBytes] の範囲にクランプする
            Bytes = Math.Min(Math.Max(Marshal.ReadInt64(bytes) - _hack, 0), TotalBytes);
        }

        // 50ms 以内の連続呼び出しは進捗報告をスキップしてコールバックのオーバーヘッドを削減する
        var now = Environment.TickCount64;
        if (now - _lastReportedTicks < ReportIntervalMs)
            return (int)SevenZipCode.Success;

        _lastReportedTicks = now;
        return Report(ProgressState.Progress, Current());
    }

    #endregion

    #region IArchiveExtractCallback

    /// <summary>
    /// 展開データの書き込み先ストリームを取得する。
    /// </summary>
    /// <param name="index">アーカイブ内のインデックス。</param>
    /// <param name="stream">出力ストリーム。スキップ/ディレクトリの場合は null。</param>
    /// <param name="mode">操作モード（Extract / Test / Skip）。</param>
    /// <returns>操作結果コード。</returns>
    public int GetStream(uint index, out ISequentialOutStream stream, AskMode mode)
    {
        var dest = default(ISequentialOutStream);
        try
        {
            // ストリーム生成処理を Run でラップして例外ハンドリングと進捗報告を一元化する
            return Run(() => {
                dest = NewStream(index, mode);
                return (int)SevenZipCode.Success;
            }, ProgressState.Start, Current);
        }
        finally
        {
            // 例外が発生した場合でも out パラメータを確実に設定する
            stream = dest;
        }
    }

    /// <summary>
    /// ファイル展開直前に呼び出される。
    /// </summary>
    /// <param name="mode">操作モード（Extract / Test / Skip）。</param>
    public int PrepareOperation(AskMode mode)
    {
        _mode = mode;
        // Skip の場合は進捗報告しない（カウントにも含めない）
        if (mode == AskMode.Skip) return (int)SevenZipCode.Success;

        // FileExtracting イベントを発火する（Extract / Test モードのみ）
        var cancelled = FireFileEvent(OnFileStarted, Current(), Current()?.Index ?? -1);
        if (cancelled) return (int)SevenZipCode.Cancel;

        return Report(ProgressState.Progress, Current());
    }

    /// <summary>
    /// 展開結果を設定する。
    /// </summary>
    /// <param name="code">操作結果コード。</param>
    public int SetOperationResult(SevenZipCode code)
    {
        if (code != SevenZipCode.Success) Logger.Warn($"[{code}] Index:{Current()?.Index ?? -1}, Name:{Current()?.RawName ?? ""}");

        // _iterator が無効な状態で SetOperationResult が呼ばれるケース（破損アーカイブで
        // 7z.dll が GetStream をスキップしたとき等）に備え、以降の Finalize / イベント発火をガードする
        var iteratorValid = _iterator.Valid;

        // WrongPassword または PasswordTimes>0 時の DataError は、誤パスワードが
        // PasswordQuery._cache に永続して UI 再試行を無視する問題を防ぐため、cache をクリアする。
        // 次回 CryptoGetTextPassword 呼び出し時に Query.Request が再度実行され、
        // ユーザーに再入力の機会が与えられる。
        if (code == SevenZipCode.WrongPassword ||
            (code == SevenZipCode.DataError && PasswordTimes > 0))
        {
            (Password as PasswordQuery)?.Reset();
        }

        // WrongPassword は即座に返してパスワード再試行フローに入る
        if (code == SevenZipCode.WrongPassword) return (int)code;
        // パスワード試行済みの DataError は WrongPassword として扱う
        if (code == SevenZipCode.DataError && PasswordTimes > 0) return (int)SevenZipCode.WrongPassword;
        // Skip モードの場合はエラーコードを無視して Success を返す
        if (_mode == AskMode.Skip) return (int)SevenZipCode.Success;

        try
        {
            // Extract モードの場合、ストリームを閉じてファイル属性を設定する (iterator が valid のときのみ)
            if (_mode == AskMode.Extract && iteratorValid) Finalize(_iterator.Current);
            Count++;

            // FileExtracted イベントを発火する（Extract モードのみ、成功・失敗問わず / iterator valid 時のみ）
            if (_mode == AskMode.Extract && iteratorValid)
            {
                var cancelled = FireFileEvent(OnFileFinished, _iterator.Current, _iterator.Current?.Index ?? -1);
                if (cancelled) return (int)SevenZipCode.Cancel;
            }

            return Report(code, null);
        }
        catch (Exception e) { return Report(code, e); }
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
        // (ArchiveStreamWriter → BaseStream) に触らない。各ストリームは自身の
        // finalizer / SafeHandle が回収する (ArchiveReader.Dispose の同ガード参照)。
        if (disposing)
        {
            // 未クローズのストリームを全て解放する
            foreach (var kv in _streams) kv.Value?.Dispose();
        }
        _streams.Clear();
    }

    #endregion

    #region Implementations

    /// <summary>
    /// 指定した引数からストリームを生成する。
    /// </summary>
    private ArchiveStreamWriter NewStream(uint index, AskMode mode)
    {
        // イテレータを進めて対象インデックスのエントリを見つける
        while (_iterator.MoveNext())
        {
            if (_iterator.Current.Index == index) break;
        }

        // 対象インデックスが見つからない場合、または Extract モードでない場合は null を返す
        if (!_iterator.Valid || mode != AskMode.Extract) return null;

        var e = _iterator.Current;

        // StreamOutputs に一致するエントリは外部 Stream へ直接書き込む
        if (StreamOutputs is not null && StreamOutputs.TryGetValue(e.Index, out var external) && external is not null)
        {
            // 外部 Stream は呼び出し側が所有するため dispose=false で包む
            var w = new ArchiveStreamWriter(external, dispose: false);
            _streams.Add(e.Index, w);
            _streamOutputIndices.Add(e.Index);
            return w;
        }

        if (e.FullName.HasValue())
        {
            if (Filter?.Invoke(e) ?? false)
            {
                // フィルタに一致したアイテムはスキップする
                Skip(e);
            }
            else if (e.IsDirectory)
            {
                // Destination が空（Stream 出力専用モードなど）の場合はディレクトリ作成をスキップする
                if (Destination.HasValue()) e.CreateDirectory(Destination);
            }
            else
            {
                // Destination が空の場合、path ベース出力先は無いので null を返す（該当エントリはスキップされる）
                if (!Destination.HasValue()) return null;

                // ファイルの場合は書き込みストリームを生成して辞書に登録する
                var stream = Io.Create(Combine(Destination, e.FullName));
                var dest   = new ArchiveStreamWriter(stream);
                _streams.Add(e.Index, dest);
                return dest;
            }
        }
        return null;
    }

    /// <summary>
    /// 指定したオブジェクトの後処理を実行する。
    /// </summary>
    private void Finalize(ArchiveEntity src)
    {
        if (src is null) return;

        // 外部 Stream 出力のエントリは属性設定スキップ対象としてマーク
        var streamOutput = _streamOutputIndices.Contains(src.Index);

        if (_streams.TryGetValue(src.Index, out var obj))
        {
            // ストリームをフラッシュして閉じる（外部 Stream は dispose=false のため実 Stream は閉じない）
            obj?.Dispose();
            _ = _streams.Remove(src.Index);
        }

        Logger.Trace($"[{nameof(Finalize)}] Index:{src.Index}, Name:{src.FullName.Quote()}");

        // 外部 Stream 出力の場合または Destination 未指定の場合はファイル属性設定をスキップする
        if (streamOutput || !Destination.HasValue()) return;

        // FullName が空のエントリ（エントリ名が ".." や "." でサニタイズ結果が空になったもの）は
        // 展開自体をスキップしている (NewStream の同名ガード参照)。ここで属性設定へ進むと
        // Io.Combine(Destination, "") が Destination 自身を返すため、アーカイブ由来の属性
        // (Hidden / System / ReadOnly) とタイムスタンプが「展開先ディレクトリ」に適用される。
        if (!src.FullName.HasValue()) return;

        // 元のファイル属性（更新日時など）をコピーして設定する
        src.SetAttributes(Destination);
    }

    /// <summary>
    /// 現在の対象アイテムのスキップ処理を実行する。
    /// </summary>
    private void Skip(ArchiveEntity src) => Logger.Try(() => {
        // ディレクトリのスキップはカウントに含めない
        if (!src.IsDirectory) Count++;
        Logger.Debug($"[{nameof(Skip)}] ({Count}/{TotalCount}) {src.FullName.Quote()}");
    });

    /// <summary>
    /// 7-zip 操作結果コードと例外に基づいて進捗を報告する。
    /// </summary>
    private int Report(SevenZipCode code, Exception error)
    {
        var e = Current();
        // 成功かつエラーなしの場合は Success 状態を報告する
        if (code == SevenZipCode.Success && error is null) return Report(ProgressState.Success, e);

        // エラーが発生した場合は例外をスタックに積んで Failed を報告する
        var obj = error ?? new SevenZipException(code);
        PushException(obj);
        return Report(obj, e);
    }

    /// <summary>
    /// 現在処理中のアーカイブエントリを返す。
    /// </summary>
    private ArchiveEntity Current() => _iterator.Valid ? _iterator.Current : null;

    #endregion

    #region Fields
    // 展開対象アイテムのイテレータ
    private readonly ArchiveEnumerator _iterator;
    // インデックス → 書き込みストリームのマップ（Finalize まで保持する）
    private readonly Dictionary<int, ArchiveStreamWriter> _streams = new();
    // 外部 Stream 出力対象となったエントリのインデックス（属性設定スキップ用）
    private readonly HashSet<int> _streamOutputIndices = new();
    // 現在の操作モード（Extract / Test / Skip）
    private AskMode _mode = AskMode.Extract;
    // 複数回 Extract を呼んだ場合のバイトオフセット補正値
    private long _hack;
    // 最後に進捗を報告した TickCount64（スロットリング用）
    private long _lastReportedTicks;
    // 進捗報告の最小間隔は CallbackBase.ReportIntervalMs に集約
    #endregion
}
