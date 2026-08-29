# 1llum1n4t1s.Sevenzip

**リポジトリ:** [https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip)

> **これは [Cube.FileSystem.SevenZip](https://github.com/cube-soft/cube.filesystem.sevenzip) のフォークです。** ソースコードは上記リポジトリで公開しています。  
> 元リポジトリとの主な違いは以下のとおりです。
>
> **ランタイム / ビルド / 配布**
> - **.NET 10** 対応（ターゲットを net10.0 / net10.0-windows に変更）
> - **NativeAOT 対応**（COM Interop を `[GeneratedComInterface]` に、P/Invoke を `[LibraryImport]` に全面移行）
> - **Windows x64 / arm64 対応** — `<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>` を宣言。Managed アセンブリは AnyCPU で単一、ネイティブ 7z.dll のみ RID で分岐
> - **7-Zip 本家 26.02** のネイティブバイナリを直接 vendor（`Cube.Native.SevenZip` 依存を除去）
> - **ネイティブバイナリ自動配布** — NuGet 公開時のリリースワークフローが 7-zip.org から最新の 7z.dll を自動取得して同梱。コンシューマのビルド時にネットワークアクセスは発生しない
> - [Cube.Core](https://github.com/cube-soft/cube.core) をソリューション内のプロジェクトとして直接組み込み（NuGet 参照ではなく NuGet パッケージに DLL を同梱）
> - CI を AppVeyor から **GitHub Actions** に移行
> - NuGet パッケージ名を **1llum1n4t1s.Sevenzip** として公開
> - **NLog から SuperLightLogger への移行**（テストハーネス用）
>
> **API / 機能追加**
> - **アーカイブ更新機能** — `ArchiveWriter.Update()` / `Remove()` で既存アーカイブのファイル追加・置換・削除が可能
> - **ロック中ファイル自動コピー** — 他プロセスがロック中のファイルを一時コピーして圧縮に含める
> - **ZIP エントリ名の Reader 側 CodePage 対応** — `ArchiveOption.CodePage` が展開側でも機能（本家は Writer 側のみ）。さらに v1.0.66 から `ArchiveOption.Encoding` で任意 `Encoding` を直接指定可能
> - **Stream ベース API**（v1.0.66〜） — `ArchiveReader(Stream)` / `ArchiveWriter.Save(Stream)` / `Update(Stream, Stream, renameMap)` / `Extract(int, Stream)` / `Add(Stream, name)` など、ファイルシステムを介さない読み書き
> - **per-file 進捗イベント**（v1.0.66〜） — `FileExtracting` / `FileExtracted` / `FileCompressing` / `FileCompressed` + `ArchiveFileEventArgs` で個別エントリの進捗とキャンセルに対応
> - **柔軟な圧縮オプション**（v1.0.66〜） — `CustomParameters`（7z.dll `ISetProperties` への任意キー注入）/ `VolumeSize`（分割書き出し）/ `IncludeEmptyDirectories`（空ディレクトリ除外）
> - **追加メタデータ型**（v1.0.66〜） — `ZipArchiveEntity`（ZIP 固有メタデータ）/ `AsyncPasswordQuery`（非同期パスワード問い合わせ）/ `ArchiveEntity.IsUnicodeText`（ヒューリスティック Unicode 判定）
> - **構造化例外**（v1.0.68〜） — `ArchiveUpdateException`（Update ロールバック失敗時に `OriginalPath` / `BackupPath` を公開）
> - **クラッシュ耐性オプション**（v1.0.69〜） — `CompressionOption.FlushToDisk`（`FlushFileBuffers` でディスク同期）/ `CompressionOption.AtomicSave`（tmp → atomic rename パターン）/ `CompressionOption.KeepBackupOnUpdate`（.bak 保持）/ `ArchiveWriter.LastBackupPath` / `ArchiveWriter.BackupPaths` / `ArchiveWriter.LastTempPath`
> - **並行実行安全性 / 厳密化**（v1.0.70〜） — `SevenZipLibrary` FinalRelease 二重解放防止 / `UpdateCallback.SetCompleted` の lock 保護 / `CompressionOption.Validate()` / `AsyncPasswordQuery` UI スレッドガード / `Update(Stream, Stream, ..., allowDestructiveOnWritebackFailure)` オプトインパラメータ
>
> **削除（breaking change）**
> - **SFX (自己展開書庫) 機能の削除**（v1.0.58） — `SfxOption` / `Format.Sfx` / `7z.sfx` 同梱を廃止。自己展開書庫が必要な場合は別途 SFX モジュールを用意する必要あり

---

[1llum1n4t1s.Sevenzip](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip) は [7-Zip](http://www.7-zip.org/) の COM インターフェースを利用した .NET 10 向けラッパーライブラリです。**Windows x64 / arm64** をサポートします。ライセンスはプロジェクトにより GNU LGPLv3 または Apache 2.0 です。詳細は [License.md](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/blob/main/License.md) を参照してください。

## Usage

NuGet パッケージでインストールできます。パッケージ ID は **1llum1n4t1s.Sevenzip** です。プロジェクトファイルに依存関係を追加するか、Visual Studio の NuGet パッケージ UI から選択してください。ソースからビルドする場合は [リポジトリ](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip) をクローンしてください。

### Examples for archiving files

圧縮の簡単な例は以下のとおりです。サンプルでは `using Cube.FileSystem.SevenZip;` は省略しています。

```cs
// Set only what you need.
var files   = new[] { ".DS_Store", "Thumbs.db", "__MACOSX", "desktop.ini" };
var options = new CompressionOption
{
    CompressionLevel  = CompressionLevel.Ultra,
    CompressionMethod = CompressionMethod.Lzma,
    EncryptionMethod  = EncryptionMethod.Aes256,
    Password          = "password",
    Filter            = Filter.From(files),
    CodePage          = CodePage.Utf8,
};

using (var writer = new ArchiveWriter(Format.Zip, options))
{
    writer.Add(@"path\to\file");
    writer.Add(@"path\to\directory_including_files");

    var progress = new Progress<Report>(e => DoSomething(e));
    writer.Save(@"path\to\save.zip", progress);
}
```

ArchiveWriter にアーカイブ形式（Zip, SevenZip など）とオプションを指定し、追加したいファイル・ディレクトリを Add したうえで Save を呼び出します。Tar 系のアーカイブでは TarOption で圧縮方式（GZip, BZip2, XZ, Copy）を指定できます。

```cs
var options = new CompressionOption
{
    CompressionLevel  = CompressionLevel.Ultra,
    CompressionMethod = CompressionMethod.BZip2, // GZip, BZip2, XZ or Copy
};

using (var writer = new ArchiveWriter(Format.Tar, options))
{
    writer.Add(@"path\to\file");
    writer.Add(@"path\to\directory_including_files");
    writer.Save(@"path\to\save.tar.gz");
}
```

### Examples for extracting archives

アーカイブを解凍するには ArchiveReader を作成し、Save を呼び出します。コンストラクタの第2引数（パスワード）には文字列または `Cube.Query<string>` を指定できます。後者は対話的にパスワードを求める場合に利用します。

```cs
// Set password directly or using Query<string>
var password = new Cube.Query<string>(e =>
{
    e.Result = "password";
    e.Cancel = false;
});

// Supports only the Filter property
var files   = new[] { ".DS_Store", "Thumbs.db", "__MACOSX", "desktop.ini" };
var options = new ArchiveOption { Filter = Filter.From(files) };

using (var reader = new ArchiveReader(@"path\to\archive", password, options))
{
    var progress = new Progress<Report>(e => DoSomething(e));
    reader.Save(@"path\to\directory", progress);
}
```

ArchiveWriter および ArchiveReader はスレッドセーフではありません。1 つのインスタンスを生成から破棄まで、同時に操作するスレッドが常に 1 つになるようにしてください。スレッドを跨いで受け渡すこと自体は、`SemaphoreSlim` / `lock` / `await` などで直列化されていれば問題ありません（同一スレッドに固定する必要はありません）。単純に非同期化したい場合は、一連の処理全体を `Task.Run()` で実行してください。

### ログの有効化

本ライブラリは既定では何も出力しません。無視されたオプション、アーカイブのオープン失敗コード、スキップしたファイル、`.bak` の削除失敗といった診断情報は警告ログにのみ出るため、切り分けが必要な場合はログの出力先を設定してください。

```csharp
// 任意の ILoggerSource 実装を渡す（未設定時は何も出力しない）
Cube.Logger.Configure(new MyLoggerSource());
```

`Cube.Logger` / `Cube.ILoggerSource` は同梱の `Cube.Core.dll` に含まれます。例外として通知される失敗（`SevenZipException` / `ArchiveUpdateException` 等）はログ設定に関係なくスローされ、per-file の進捗・失敗は `IProgress<Report>` と `FileCompressing` / `FileExtracting` / `FileSkipped` イベントでも受け取れます。

## Upstream からの変更点・注意事項

本フォークでは NativeAOT 対応のために COM Interop を `[ComImport]` から `[GeneratedComInterface]` / `[GeneratedComClass]` へ、P/Invoke を `[DllImport]` から `[LibraryImport]` へ全面移行しています。これに伴い、upstream と比較して以下の仕様変更および注意点があります。

### 公開 API の変更

| メソッド / プロパティ | 変更内容 | 影響 |
|---------|---------|------|
| `ArchiveReader.Save(string, uint[], IProgress<Report>)` | `unsafe` 修飾子が追加 | **呼び出し側への影響なし。** C# では `unsafe` メソッドの呼び出しに `unsafe` コンテキストは不要です。`AllowUnsafeBlocks` はライブラリ側のビルドにのみ必要で、消費者側のプロジェクトには不要です。 |
| `ArchiveWriter.Update(...)` | **新規追加** — 既存アーカイブのファイル追加・置換 | 4 つのオーバーロード。`sourcePassword` で暗号化アーカイブの更新にも対応。 |
| `ArchiveWriter.Remove(string)` | **新規追加** — アーカイブ内の指定アイテムを削除 | `Update()` と組み合わせて使用。 |
| `ArchiveReader` での `ArchiveOption.CodePage` 対応 | **挙動拡張** — 本家は Writer 側のみの対応だったが、Reader 側でも `CodePage` が反映されるよう修正 | ZIP ファイル名デコードに使用。既存の `CodePage` enum (`Oem`/`Utf8`/`Japanese` 等) はそのまま利用可能。 |
| `Io.Open(string, FileShare)` | **新規追加** — FileShare 指定付きオープン | Cube.Core の `IoController` にも対応オーバーロード追加。 |
| `ArchiveReader(Stream, ...)` 等 | **v1.0.66 で追加** — Stream ベース API | path 版と並列運用。`leaveOpen` で所有権制御。 |
| `ArchiveReader.Extract(int, Stream)` | **v1.0.66** — 単一エントリを直接 Stream に展開 | 辞書版 `Extract(IReadOnlyDictionary<int, Stream>)` も提供。 |
| `ArchiveWriter.Save(Stream, ...)` | **v1.0.66** — Stream に直接書き込み | `VolumeSize` は未サポート (警告ログ発火 → [ログの有効化](#ログの有効化))。 |
| `ArchiveWriter.Add(Stream, name)` | **v1.0.66** — Stream エントリ追加 (一時ファイル経由シム) | `name` は `SafePath` でサニタイズ。 |
| `ArchiveWriter.Update(Stream, Stream, renameMap?, ...)` | **v1.0.66** — Stream ベース更新 + rename マップ | 自己参照 Stream は `CanSeek` 必須。 |
| `ArchiveReader.FileExtracting` / `FileExtracted` | **v1.0.66** — per-file 展開イベント | `ArchiveFileEventArgs.Cancel=true` でキャンセル可。 |
| `ArchiveWriter.FileCompressing` / `FileCompressed` | **v1.0.66** — per-file 圧縮イベント | 同上。 |
| `ArchiveOption.Encoding : Encoding` | **v1.0.66** — 任意 `Encoding` 指定 | `CodePage` enum より優先。 |
| `CompressionOption.CustomParameters` | **v1.0.66** — 7z.dll `ISetProperties` パススルー | `mt=<N>`, `cu=on` 等の任意キー注入。 |
| `CompressionOption.IncludeEmptyDirectories` | **v1.0.66** — 空ディレクトリ除外 | 既定 `true` (互換性維持)。 |
| `CompressionOption.VolumeSize` | **v1.0.66** — 分割書き出し | `dest.001 / dest.002 ...` を post-process split で生成。`Save(string)` のみ対応。 |
| `ArchiveEntity.IsUnicodeText` | **v1.0.66** — Unicode デコード判定 (ヒューリスティック) | ZIP bit 11 の厳密値ではない点に注意。 |
| `ZipArchiveEntity` | **v1.0.66** — Format.Zip 専用の拡張エントリ型 | `Method` / `HostOS` / `PackedSize` / `Comment` 取得可。 |
| `AsyncPasswordQuery` | **v1.0.66** — 非同期パスワードハンドラ | `Func<CancellationToken, Task<string>>` を `IQuery<string>` として公開。 |
| `ArchiveUpdateException` | **v1.0.68** — Update ロールバック失敗時の構造化例外 | `OriginalPath` / `BackupPath` から手動復旧が可能。 |
| `CompressionOption.FlushToDisk` | **v1.0.69** — `FlushFileBuffers` でディスク同期 | OS ページキャッシュまで flush。PLP 非対応ストレージのデバイスキャッシュは保証しない。 |
| `CompressionOption.AtomicSave` | **v1.0.69** — tmp → atomic rename パターン | NTFS 同一ボリューム前提。`VolumeSize` との併用不可 (例外)。 |
| `CompressionOption.KeepBackupOnUpdate` | **v1.0.69** — Save/Update 完了後も .bak を保持 | 前回値は次回操作で自動削除され孤立しない。 |
| `ArchiveWriter.LastBackupPath` / `BackupPaths` / `LastTempPath` | **v1.0.69〜v1.0.70** — .bak / tmp パスの公開 | `BackupPaths` は全履歴 (v1.0.70)。`LastTempPath` は異常終了時のクリーンアップ用 (v1.0.70)。 |
| `CompressionOption.Validate(Format)`（内部メソッド） | **v1.0.70** — オプション矛盾の早期検出 | AtomicSave+VolumeSize / Tar+Password / 負値ガード。利用者が直接呼ぶ API ではなく、`ArchiveWriter` の ctor / `Save()` が内部で検証して例外を投げる。 |
| `Update(Stream, Stream, ..., allowDestructiveOnWritebackFailure)` | **v1.0.70** — 自己参照書き戻し失敗時の dest 挙動をオプトイン化 | デフォルト false = 部分書き込み保持 / true = 全消失 (旧動作)。 |
| `CompressionOption.SkipInaccessibleFiles` / `ArchiveWriter.FileSkipped` / `FileSkippedEventArgs` | **v1.0.76〜v1.0.86** — アクセス不能または安全に追跡できないアイテムを skip して圧縮を続行 | 既定 `false`（`AccessException`）。`true` で `Add()` 時にアクセス不能なファイルや再解析ポイントを除外し、`FileSkipped` イベント（`FullName` / `RelativeName` / `Reason`）で通知する。v1.0.86 以降はジャンクションやシンボリックリンクのリンク先を再帰的に取り込まない。適用範囲は `Add()` 時の fail-fast のみ。 |
| `ArchiveWriter(Format, ...)` コンストラクタ | **v1.0.78** — 未対応フォーマットの fail-fast 検証 + ctor 失敗時クリーンアップ | `Format.Unknown` は `UnknownFormatException`、書き込み非対応フォーマット (Rar 等) は ctor で即例外 (従来は `Save()` 時に失敗)。失敗時は取得済みリソースを同期解放。 |
| 圧縮進捗 (`Report.Bytes`) の精度修正 | **v1.0.79** — 大規模アーカイブで進捗が早期に 100% へ張り付くバグを修正 | 7-Zip の completeValue をグローバル累積値として単調最大値で集計（旧実装はマルチスレッド圧縮の値後退を二重加算していた）。 |
| `ArchiveReader.Extract(...)` / `Save(string, uint[], ...)` のインデックス正規化 | **v1.0.82** — 展開対象インデックスを内部で昇順・重複なしへ正規化 | 従来は非昇順の `Dictionary` / 配列を渡すと該当エントリが**例外なく未展開のまま**終わっていた（`Dictionary` のキー列挙順は仕様上不定）。呼び出し側の配列は変更しない。 |
| `AsyncPasswordQuery.AllowBlockingOnCapturedContext` | **v1.0.82** — デッドロックガードのオプトアウト | ガードの判定を型名の denylist から「同期コンテキストが捕捉されていれば危険」へ変更。Avalonia / Blazor / Unity / 独自コンテキストでもガードが働く。ブロックしても安全と分かっている場合のみ `true` にする。ASP.NET Core / コンソール / `Task.Run` 配下は `Current` が null なので影響なし。 |
| `ArchiveReader` / `ArchiveWriter` の破棄後呼び出し | **v1.0.82** — `ObjectDisposedException` を投げる | 従来は破棄後に `Save()` / `Update()` を呼ぶとプロセスがクラッシュしうる状態だった。 |
| `ArchiveReader` コンストラクタの失敗コード | **v1.0.82** — I/O 障害を書庫破損と区別 | 負の HRESULT（ネットワーク断・アクセス拒否等）では `Code` が `IsNotArc` ではなく `UnknownError` になり、`InnerException` に実 HRESULT に対応する例外が入る。S_FALSE（本当に書庫でない場合）は従来通り `IsNotArc`。 |
| 圧縮失敗時の例外 | **v1.0.82** — 失敗が「利用者によるキャンセル」に化ける問題を修正 | 従来は per-file の圧縮失敗が空メッセージの `OperationCanceledException` になり、失敗コードと対象エントリ名が失われていた。 |
| `ArchiveOption.Filter` の比較 | **v1.0.82** — カルチャ依存比較を Ordinal へ変更 | 従来は `tr-TR` 等で `I` / `ı` の扱いが変わり、同じアーカイブでも環境によって除外されるファイルが変わっていた。 |

### 新機能

#### アーカイブ更新機能

`ArchiveWriter.Update()` で既存アーカイブにファイルを追加・置換し、`Remove()` でアーカイブ内の特定ファイルを削除できます。暗号化アーカイブの更新には `sourcePassword` パラメータを指定します。

```cs
using (var writer = new ArchiveWriter(Format.Zip))
{
    writer.Add(@"path\to\new_file.txt");
    writer.Remove("old_file.txt");
    writer.Update(@"path\to\existing.zip", @"path\to\updated.zip");
}
```

#### ロック中ファイルの自動コピー

圧縮時に他プロセスがロック中のファイルを自動検出し、`%TEMP%\SevenZip_{GUID}\` に一時コピーして処理します。`ERROR_SHARING_VIOLATION` (0x80070020) および `ERROR_LOCK_VIOLATION` (0x80070021) を検出対象とし、一時ファイルは `Dispose` 時に自動削除されます。呼び出し側のコード変更は不要です。

#### ZIP エントリ名の Reader 側 CodePage 対応

`ArchiveOption.CodePage` を **展開時 (ArchiveReader)** にも反映するようになりました（本家は Writer 側のみ対応）。古いツールが Shift-JIS で作成した ZIP の文字化けを防ぐ場合などに利用します。7z 形式は UTF-16 ネイティブのため、この設定は ZIP 形式でのみ有効です。

```cs
// Reader: Shift-JIS でエンコードされた ZIP を正しく読む（フォーク独自）
var options = new ArchiveOption { CodePage = CodePage.Japanese };
using var reader = new ArchiveReader(@"path\to\sjis.zip", "", options);

// v1.0.66 以降は任意の Encoding を直接指定可能（CP437 等にも対応）
var utf8Options = new ArchiveOption { Encoding = System.Text.Encoding.UTF8 };
using var reader2 = new ArchiveReader(@"path\to\utf8.zip", "", utf8Options);
```

### SFX 機能の削除 (breaking change, v1.0.58)

v1.0.58 で **自己展開書庫 (SFX) 関連の API をすべて削除**しました。Windows 11 以降は標準で ZIP 形式をサポートしており、SFX 形式の必要性が低下したことが理由です。

削除された API:
- `SfxOption` クラス
- `Format.Sfx` 列挙値
- `ArchiveWriter.SaveAsSfx()` （内部メソッド）
- `7z.sfx` の NuGet パッケージ同梱

v1.0.56 以前で `SfxOption` / `Format.Sfx` を使用していたコードはコンパイルエラーになります。自己展開書庫の生成が必要な場合は、7-Zip 公式配布の `7z.sfx` / `7zCon.sfx` を別途取得し、`ArchiveWriter` で作成した `.7z` アーカイブと連結してください。

### COM 例外の HRESULT 変更

`[ComImport]` の CLR CCW は、マネージド例外をすべて `E_FAIL`（0x80004005）として返していました。`[GeneratedComInterface]` では例外ごとに固有の HRESULT が返されます（例: `KeyNotFoundException` → 0x80131577）。

本ライブラリ内部では HRESULT を適切にハンドリングしているため、**通常の C# 消費者には影響ありません。** ただし、ネイティブ C++ コードから直接 COM コールバックの HRESULT を検査している場合は注意が必要です。

### IProgress\<Report\> 実装の要件

upstream では `SetTotal` コールバックで `ProgressState.Prepare` が報告されていましたが、`[ComImport]` の CCW が例外を `E_FAIL` として飲み込んでいたため、`IProgress<Report>` 実装側が `Prepare` を処理しなくても表面上は動作していました。

本フォークでは `[GeneratedComInterface]` により例外が正確に伝播するため、**カスタムの `IProgress<Report>` 実装はすべての `ProgressState` 値（`Prepare`, `Start`, `Progress`, `Success`, `Failed`）を処理する必要があります。** 未処理の状態値があると例外として 7-Zip 側に伝わり、操作が中断されます。

### COM オブジェクトのライフタイム管理

COM オブジェクトの生成・解放を `StrategyBasedComWrappers` + `CreateObjectFlags.UniqueInstance` による明示的管理に変更しました。これにより、ネイティブ DLL アンロード後の GC ファイナライザによるクラッシュが防止されます。消費者側のコードに変更は不要です。

## Dependencies

* [7-Zip](https://www.7-zip.org/) … 本家の `7z.dll` を `Libraries/Core/Native/x64/` および `Libraries/Core/Native/arm64/` に vendor して NuGet パッケージに同梱しています (Windows x64 / arm64 対応)。`runtimes/win-x64/native/` および `runtimes/win-arm64/native/` に配置されるため、.NET SDK のランタイム RID 解決により実行時に自動的に正しいバイナリが選択されます。NuGet 公開時のリリースワークフロー (`.github/workflows/publish.yml`) が 7-zip.org から最新版を自動取得して同梱します。

## License

[本リポジトリ](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip) は次のオリジナルプロジェクトをベースにしています。

| 由来 | リポジトリ | ライセンス |
|------|------------|------------|
| 7-Zip ラッパー | [cube-soft/cube.filesystem.sevenzip](https://github.com/cube-soft/cube.filesystem.sevenzip) | コアライブラリ: [GNU LGPLv3](https://www.gnu.org/licenses/lgpl-3.0.html)、その他: [Apache 2.0](https://www.apache.org/licenses/LICENSE-2.0) |
| ユーティリティ・MVVM 等 | [cube-soft/cube.core](https://github.com/cube-soft/cube.core) | [Apache 2.0](https://www.apache.org/licenses/LICENSE-2.0) |

各プロジェクトのライセンス表記および条件は、本リポジトリ内の [License.md](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/blob/main/License.md) およびソリューション内の各ライセンスファイルを参照してください。  
Copyright © 2010 [CubeSoft, Inc.](https://www.cube-soft.jp/)

Copyright © 2026 [ゆろち](https://github.com/1llum1n4t1s/)
