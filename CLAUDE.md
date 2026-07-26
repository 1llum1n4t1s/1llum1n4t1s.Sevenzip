# CLAUDE.md

This file provides guidance to Claude Code and other coding agents working in this repository.

## プロジェクト概要

7-Zip COM インターフェースを利用した .NET 10 向け圧縮・解凍ラッパーライブラリ。[Cube.FileSystem.SevenZip](https://github.com/cube-soft/cube.filesystem.sevenzip) のフォークで、.NET 10 / NativeAOT 対応 + 本家 7-Zip バイナリの vendoring が主な変更点。NuGet パッケージ ID は `1llum1n4t1s.Sevenzip`。**Windows x64 / arm64 専用**。公開 API は `ArchiveReader`（解凍）と `ArchiveWriter`（圧縮）の 2 クラス。Stream ベース API / per-file 進捗イベント / アーカイブ更新・削除 / クラッシュ耐性オプション（AtomicSave / FlushToDisk / KeepBackupOnUpdate）等を本家から大幅に拡張。SFX 機能は v1.0.58 で削除済み (breaking change)。

## ビルド・テストコマンド

```bash
# ソリューション全体のビルド（Managed DLL は AnyCPU、ネイティブ 7z.dll のみ RID で分岐）
rtk dotnet build Cube.FileSystem.SevenZip.slnx -c Debug -p:Platform=AnyCPU

# Release ビルド
rtk dotnet build Cube.FileSystem.SevenZip.slnx -c Release -p:Platform=AnyCPU

# テスト全実行（--no-build を付けるとビルドをスキップ）
rtk dotnet test Tests/Core/Cube.FileSystem.SevenZip.Tests.csproj -c Debug -p:Platform=AnyCPU --no-build

# 単一テスト実行（FullyQualifiedName で絞り込み）
rtk dotnet test Tests/Core/Cube.FileSystem.SevenZip.Tests.csproj -c Debug -p:Platform=AnyCPU --no-build --filter "FullyQualifiedName~TestMethodName"

# NuGet パック（NoDefaultExcludes=true がないとネイティブアセットが packing されない）
rtk dotnet pack Libraries/Core/Cube.FileSystem.SevenZip.csproj -c Release -p:Platform=AnyCPU -o Libraries/Core/bin -p:NoDefaultExcludes=true
```

**重要**:
- Platform のデフォルトは `AnyCPU`（`Directory.Build.props` で `<Platforms>AnyCPU;x64;arm64</Platforms>`）。出力先は `bin\$(Configuration)\`。x64 / arm64 明示時は `bin\x64\...` / `bin\arm64\...` に分岐。
- `-p:Platform` を各プロジェクトへ伝播させるには `.slnx` の per-project `<Platform Solution="*|x64" Project="x64" />` mapping が必要。これが無いと solution 経由のビルドは指定を無視して全プロジェクトを AnyCPU で解決し、同梱ネイティブ 7z.dll の選択と出力先分岐が効かなくなる（`.slnx` にプロジェクトを追加したら mapping も追加する）。
- テスト実行時は Tests/Core が RID を Platform に追従させるため、出力は `bin\Debug\net10.0-windows8\win-x64\`（AnyCPU / x64）または `bin\arm64\Debug\net10.0-windows8\win-arm64\`（arm64）に配置される。RID を `win-x64` 固定にすると `-p:Platform=arm64` で `PlatformTarget` と衝突して `NETSDK1032` になる。arm64 のテスト実行自体は arm64 ホストが必要。
- managed アセンブリはアーキ非依存で、NuGet パッケージは Platform に関係なく x64 / arm64 両方のネイティブ 7z.dll を同梱する。pack は AnyCPU で行えば十分。
- Tests/Core の RID が Platform 依存なので、`Tests/Core/packages.lock.json` の RID セクション（`net10.0-windows8.0/win-x64`）も Platform 依存になる。**`-p:Platform=arm64` で restore / build すると `win-arm64` に書き換わる**。CI は `dotnet restore --locked-mode`（AnyCPU）で検証するため、arm64 を試した後は `git checkout -- Tests/Core/packages.lock.json` で戻してからコミットする。

## バージョン管理

バージョンは `Directory.Build.props` の `<Version>` タグで一元管理（全プロジェクト共通）。patch を +1 ずつインクリメント（`/vava` スキルで自動化）。コミットメッセージは日本語。

## アーキテクチャ

### プロジェクト構成

- **Libraries/Core** — メインライブラリ (`Cube.FileSystem.SevenZip`)。ライセンス: LGPLv3
- **Libraries/Cube.Core** — MVVM ユーティリティ、基底クラス (`DisposableBase`, `Bindable`, `IQuery<T>`, `Io`, `IoController` 等)。`PrivateAssets="all"` で参照し、DLL は NuGet パッケージに直接同梱。ライセンス: Apache 2.0
- **Libraries/Cube.Logging** — SuperLightLogger ラッパー。テストハーネスでのみ使用（`IsPackable=false`）。NLog からの移行先。
- **Tests/Core** — NUnit 4 によるテスト（コンソールアプリとして実行、`OutputType=Exe`）。
- **Tests/Private** — テスト用共通基盤 (`FileFixture`, `SourceFileFixture`)。

### ベンダードネイティブバイナリ

`Libraries/Core/Native/x64/7z.dll` および `Libraries/Core/Native/arm64/7z.dll` (7-Zip 26.02) を直接 vendor し、NuGet パッケージの `runtimes/win-x64/native/` と `runtimes/win-arm64/native/` に同梱する。これらは **`.gitignore` の `*.dll` ルール** に引っかかるため、末尾の例外ルール `!Libraries/Core/Native/**/*.dll` により明示的に追跡している。

`7z.dll` は `SevenZipLibrary` (Libraries/Core/Sources/Internal/SevenZipLibrary.cs) がアセンブリ隣から `LoadLibrary` で読み込む。.NET SDK の runtime asset 規約により、実行時の RID に応じて `runtimes/win-{rid}/native/7z.dll` が自動的にアセンブリ隣に配置される。x64 / arm64 以外のプロセスでは構築時に `PlatformNotSupportedException` を投げる（ランタイムガード）。

### ネイティブバイナリの配置

NuGet パッケージの `runtimes/win-{x64,arm64}/native/7z.dll` として配布。`RuntimeIdentifier` 指定ビルドや `dotnet publish` ではアセンブリ直下に自動配置される。RID なしの `dotnet build` では `runtimes/{rid}/native/` サブディレクトリに配置されるため、`SevenZipLibrary` がフォールバック探索で対応する。

7-Zip 本体の追従は `publish.yml` のリリースワークフロー内で自動化。`release/**` ブランチ push 時に 7-zip.org から最新の 7z.dll をダウンロードし、NuGet パッケージに同梱する。リポジトリ内の vendored DLL はローカル開発・テスト用のフォールバック。

### 7-Zip COM Interop 構造（Libraries/Core）

```
Sources/
├── ArchiveReader.cs, ArchiveWriter.cs   # 公開 API (sealed クラス)
├── ArchiveEntity.cs                     # アーカイブエントリ (継承可)
├── ZipArchiveEntity.cs                  # ZIP 固有エントリ (Method / HostOS 等)
├── ArchiveFileEventArgs.cs              # per-file イベント引数
├── AsyncPasswordQuery.cs                # 非同期パスワード問い合わせ + UI スレッドガード
├── ArchiveUpdateException.cs            # Update ロールバック失敗時の構造化例外
├── Format.cs                            # Zip/7z/Tar/Rar/Iso/Udf 等の列挙
├── Options/
│   ├── ArchiveOption.cs                 # CodePage / Encoding / Filter / ThreadCount など共通オプション
│   └── CompressionOption.cs             # Writer 用: 圧縮レベル / メソッド / パスワード / CustomParameters / VolumeSize / FlushToDisk / AtomicSave / KeepBackupOnUpdate + Validate()
└── Internal/
    ├── Interfaces/          # COM インターフェース定義 ([GeneratedComInterface])
    ├── Callbacks/           # COM コールバック実装 ([GeneratedComClass])
    │   ├── UpdateCallback.cs   # 圧縮時: IArchiveUpdateCallback + ICryptoGetTextPassword2 + per-file イベント
    │   ├── ExtractCallback.cs  # 展開時: IArchiveExtractCallback + StreamOutputs 分岐
    │   ├── OpenCallback.cs     # 解凍時のオープン処理
    │   ├── PasswordCallback.cs
    │   └── CallbackBase.cs     # 共通進捗報告 + FireFileEvent ヘルパー
    ├── Options/             # CompressionOptionSetter 系 (既知キー + CustomParameters merge)
    ├── UpdatePlan.cs        # 更新プラン (Keep / Replace / Add / Rename / Remove)
    ├── FileSystemHelper.cs  # IsFileLocked 共通化
    └── SevenZipLibrary.cs   # 7z.dll の参照カウント付き singleton + CreateObject 関数ポインタ
```

### ファイルI/O基盤（Cube.Core）

`Io` 静的クラス + `IoController` でファイル操作を提供。テスト時は `SourceFileFixture.Teardown()` で `Io.Configure(new())` により標準実装にリセットされる。

`Io.Open()` には 2 つのオーバーロード:
- `Io.Open(path)` — `FileShare.Read` 相当
- `Io.Open(path, FileShare share)` — 任意の `FileShare` モード（フォーク追加）

### ロック中ファイルの自動コピー機能

圧縮時に他プロセスがロックしているファイルを自動的に一時コピーして処理する:

1. `ArchiveWriter.AddItem()` — 事前チェックで `Io.Open()` 失敗時、`Io.Open(path, FileShare.ReadWrite | FileShare.Delete)` でフォールバック確認
2. `UpdateCallback.Open()` — `Save()` 時に `Io.Open()` 失敗なら `CopyLockedFile()` で `%TEMP%\SevenZip_{GUID}\` に一時コピーし、コピーを開く
3. `UpdateCallback.Dispose()` — 一時ディレクトリを削除

ロック判定は `IsFileLocked()` で HResult（`ERROR_SHARING_VIOLATION = 0x80070020`、`ERROR_LOCK_VIOLATION = 0x80070021`）を確認。

#### 完全排他ファイルの skip 続行 (`CompressionOption.SkipInaccessibleFiles`)

`FileShare.None` で他プロセスが排他保持しているファイル (Visual Studio の `.vsidx` 等) は、自動コピー機構（`FileShare.ReadWrite | FileShare.Delete` で再試行）でも開けず、既定では `AddItem()` の外側 catch が `AccessException` を投げて圧縮全体が失敗する。

呼び出し側 (GUI アプリ等) で「1 ファイルアクセス不能で全体を死なせない」が要件のとき、`CompressionOption.SkipInaccessibleFiles = true` を渡すと:

1. `AddItem()` の outer catch で `_items` に追加せずスキップする（`AccessException` を投げない）
2. `ArchiveWriter.FileSkipped` イベント（引数: `FileSkippedEventArgs { FullName, RelativeName, Reason }`）が発火する
3. `Logger.Warn` にスキップ事実が記録される

既定は `false`（従来通り throw）で後方互換性を保持する。**現状の適用範囲は `Add()` 時の fail-fast のみ**。Add 通過後に Save 中で他プロセスが新たにロックを取り始める race は対象外（`UpdateCallback.Open` 側は引き続き失敗時に例外を投げる）— 実害が出たら別途対応する。

### NativeAOT 対応の規約

COM Interop と P/Invoke は全面的に AOT 互換 API に移行済み:
- `[ComImport]` → `[GeneratedComInterface]`（ソースジェネレーター使用）
- `[DllImport]` → `[LibraryImport]`
- COM オブジェクトは `StrategyBasedComWrappers` + `CreateObjectFlags.UniqueInstance` で明示管理
- AOT 非互換コード（リフレクションベース）には `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` を付与
- `[GeneratedComInterface]` は例外ごとに固有の HRESULT を返すため、カスタムの `IProgress<Report>` 実装は全ての `ProgressState` 値を処理する（未処理だと例外が 7-Zip 側に伝播して中断する）

### コード規約

- `DisableImplicitNamespaceImports = true` — 全 using を明示的に記述する
- `AllowUnsafeBlocks = true` — COM ポインタ操作のため（`ArchiveReader.Save(string, uint[], IProgress<Report>)` が `unsafe`）
- `#region` でセクション分割（Constructors / Properties / Methods / Fields）
- XML ドキュメントコメントは日本語
- `ArchiveReader` / `ArchiveWriter` は**同一スレッドで生成から破棄まで実行する**（非同期は `Task.Run` で一連の処理を包む）

## 既知の 7-Zip 26.00 挙動変化

本家 25.01 → 26.00 への移行に伴い、以下の挙動変化がテストに影響する。Cube 時代の Babel パッチ（SJIS 自動検出）が無くなったのが主因。

- **SJIS エンコード ZIP**: 自動検出されないため `ArchiveOption.CodePage = CodePage.Japanese` を明示指定する。`Extract_SampleUnixSjis` テスト参照。
- **Mac 製 ZIP (NFD)**: `名称未設定フォルダ` 等の濁点付き文字が NFD のまま展開される。テスト側で `FindEntity` ヘルパーが NFC/NFD の両方を試すフォールバック実装を持つ。
- **ZipSlipWin 系のパスサニタイゼーション**: バックスラッシュを含むエントリ名は Windows 禁止文字を Private Use Area (`U+F02F` / `U+F05C`) にエスケープする。特定ファイル名ではなく「destDir 外に漏洩しない」ことだけを `Extract_ZipSlipWin` で検証する。
- **CJK パスワードでの ZIP 作成**: upstream regression。`ZipCrypto` / `AES256` 共に `E_INVALIDARG` で失敗する（7z.exe CLI でも再現）。本家修正待ちのためテストケースから除外済み（`ArchiveWriterTest.cs` のコメント参照）。

## 進捗報告 (SetCompleted) のセマンティクス

`IArchiveUpdateCallback::SetCompleted` の completeValue は **SetTotal と同尺度のグローバル累積値**（アーカイブ全体の絶対進捗位置、complexity 尺度でヘッダ定数込み）であり、ファイル毎に 0 リセットされない（7-Zip 公式コンソール UI も絶対代入で解釈。CMtProgressMixer2 / CLocalProgress が CriticalSection 直列化 + offset 加算で合成）。ただしマルチスレッド圧縮ではスレッド間の読み取りレースで**まれに直前より小さい値が届く**ため、`UpdateCallback.SetCompleted` は単調最大値のみを採用する。

旧実装（〜1.0.78）の「値の減少 = ファイル切替リセット」ヒューリスティックは、この後退のたびにグローバル累積値を二重加算し、大規模アーカイブで進捗が早期に 100% へ張り付く原因だった（実測: 528k ファイル / 60GB の ZIP 圧縮 22 分中に 16 回の後退 → 23.7% 過剰計上 → 残り 9.2 分が 100% 表示のまま）。回帰テスト: `UpdateCallbackProgressTest`。

関連の設計妥協: `UpdateCallback` は GetStream で開いた入力ストリームを **Dispose まで保持**する（早期解放は ZIP Ultra MT で COM 違反、`SetOperationResult` のコメント参照）。このため**圧縮中は全対象ファイルが FileShare.Read で write ロックされ続ける**（数十万ファイル × 数十分の圧縮では利用者のファイル保存が失敗しうる既知の制約）。なお `Io.Open` は既定バッファ (4096B) の `FileStream` を返すため、数十万ストリーム同時保持ではバッファ分のメモリも線形に増える（`ArchiveStreamReader` は呼び出し側 span へ直読するので `bufferSize: 1` にできるが、`Io` 抽象のオーバーロード追加が必要なため未対応）。

## COM コールバックの並行呼び出し

7z.dll のマルチスレッド圧縮（`CompressionOption.ThreadCount > 1`）は `GetStream` / `SetCompleted` / `SetOperationResult` を**並行に呼ぶ**。`UpdateCallback` の共有可変状態はロックで保護する:

- `_completedLock` — `SetCompleted` の `_maxCompletedBytes` / `Bytes`
- `_stateLock` — `_streams`（開いたストリームのリスト）/ `_tempDir`（ロック中ファイルの一時コピー先）/ `_index`（処理中エントリ）
- `CallbackBase.PushException()` — `Exceptions` スタックへの積み込み。**`Exceptions.Push` を直接呼ばない**

`_index` は上書きされうるため、自分の解決済みインデックスを持つ呼び出し元は `Current(int)` を使う（引数なしの `Current()` はロック経由で現在値を読む）。

失敗を `Exceptions` へ積むのは必須である。`ThrowIfError` は `Exceptions` が空なら「純粋なユーザーキャンセル」と判定して `OperationCanceledException` を投げるため、積み忘れると実際の I/O 失敗が「利用者が中断した」に化ける。

## テストハーネス

- NUnit 4 をコンソールアプリ (`OutputType=Exe`) として実行
- `[OneTimeSetUp]` (`Tests/Core/Sources/Program.cs`) で `Logger.Configure(new LoggerSource("Cube.FileSystem.SevenZip.Tests.log"))` を呼ぶ
- `FileFixture` 継承で `Get()` / `GetSource()` ヘルパーが使える
- 期待値は `Tests/Core/Examples/Expected/{archive}.txt` に CSV 形式で記録

## CI/CD

GitHub Actions で `release/**` ブランチへの push 時に、7-zip.org から最新 7z.dll を取得 → ビルド → NuGet パッケージを自動公開（`.github/workflows/publish.yml`）。`main` ブランチには公開トリガーは設定されていない。
