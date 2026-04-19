# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

7-Zip COM インターフェースを利用した .NET 10 向け圧縮・解凍ラッパーライブラリ。[Cube.FileSystem.SevenZip](https://github.com/cube-soft/cube.filesystem.sevenzip) のフォークで、.NET 10 / NativeAOT 対応 + 本家 7-Zip 26.00 バイナリの vendoring が主な変更点。NuGet パッケージ ID は `1llum1n4t1s.Sevenzip`。**Windows x64 / arm64 専用**。v1.0.58 で SFX 関連 API を削除 (breaking change)、v1.0.60 で AnyCPU 移行 + ARM64 対応 + ビルド時 auto-update 機構を追加、v1.0.64 で auto-update 機構を撤去し決定論的ビルドに回帰した。v1.0.66 で Stream ベース API / CustomParameters / VolumeSize / AsyncPasswordQuery 等の大規模追加、v1.0.67〜v1.0.68 で 6 人分隊レビュー指摘の品質改善。v1.0.69 で停電耐性オプション (`FlushToDisk` / `AtomicSave` / `KeepBackupOnUpdate` / `LastBackupPath` / `ArchiveUpdateException`) を追加。v1.0.70 で並行実行安全性 (`SevenZipLibrary` の FinalRelease 二重解放防止、`ArchiveWriter` の `LastBackupPath` 自動クリーンアップ、`UpdateCallback.SetCompleted` lock 保護、`CompressionOption.Validate()`) と doc 厳密化 (AtomicSave の NTFS 制約、FlushToDisk の PLP 制約を明示) + `BackupPaths` 履歴 / `LastTempPath` 公開 + `allowDestructiveOnWritebackFailure` オプトインパラメータ追加。

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
- テスト実行時は Tests/Core が `RuntimeIdentifier=win-x64` を設定しているため、出力は `bin\Debug\net10.0-windows8\win-x64\` に配置される。

## バージョン管理

バージョンは `Directory.Build.props` の `<Version>` タグで一元管理（全プロジェクト共通）。偶数マイナー（1.0.50 → 1.0.52 → 1.0.54 → ...）で bump する慣習。コミットメッセージは日本語。

## アーキテクチャ

### プロジェクト構成

- **Libraries/Core** — メインライブラリ (`Cube.FileSystem.SevenZip`)。公開 API は `ArchiveReader`（解凍）と `ArchiveWriter`（圧縮）。ライセンス: LGPLv3
- **Libraries/Cube.Core** — MVVM ユーティリティ、基底クラス (`DisposableBase`, `Bindable`, `IQuery<T>`, `Io`, `IoController` 等)。`PrivateAssets="all"` で参照し、DLL は NuGet パッケージに直接同梱。ライセンス: Apache 2.0
- **Libraries/Cube.Logging** — SuperLightLogger ラッパー。テストハーネスでのみ使用（`IsPackable=false`）。NLog からの移行先。
- **Tests/Core** — NUnit 4 によるテスト（コンソールアプリとして実行、`OutputType=Exe`）。
- **Tests/Private** — テスト用共通基盤 (`FileFixture`, `SourceFileFixture`)。

### ベンダードネイティブバイナリ

`Libraries/Core/Native/x64/7z.dll` および `Libraries/Core/Native/arm64/7z.dll` (7-Zip 26.00) を直接 vendor し、NuGet パッケージの `runtimes/win-x64/native/` と `runtimes/win-arm64/native/` に同梱する。これらは **`.gitignore` の `*.dll` ルール** に引っかかるため、末尾の例外ルール `!Libraries/Core/Native/**/*.dll` により明示的に追跡している。

`7z.dll` は `SevenZipLibrary` (Libraries/Core/Sources/Internal/SevenZipLibrary.cs) がアセンブリ隣から `LoadLibrary` で読み込む。.NET SDK の runtime asset 規約により、実行時の RID に応じて `runtimes/win-{rid}/native/7z.dll` が自動的にアセンブリ隣に配置される。x64 / arm64 以外のプロセスでは構築時に `PlatformNotSupportedException` を投げる（ランタイムガード）。

### ネイティブバイナリの配置

NuGet パッケージの `runtimes/win-{x64,arm64}/native/7z.dll` として配布。`RuntimeIdentifier` 指定ビルドや `dotnet publish` ではアセンブリ直下に自動配置される。RID なしの `dotnet build` では `runtimes/{rid}/native/` サブディレクトリに配置されるため、`SevenZipLibrary` がフォールバック探索で対応する。

7-Zip 本体の追従は `.github/workflows/update-7zip.yml` の週次 PR bot がメンテナ向けに自動 PR を作成し、レビュー後にリリースする運用。コンシューマのビルド時にネットワークアクセスは一切発生しない（決定論的ビルド保証）。

### 7-Zip COM Interop 構造（Libraries/Core）

```
Sources/
├── ArchiveReader.cs, ArchiveWriter.cs   # 公開 API (sealed クラス / Stream 版オーバーロード v1.0.66 / AtomicSave+BackupPaths+LastTempPath v1.0.70)
├── ArchiveEntity.cs                     # アーカイブエントリ (継承可 / IsUnicodeText v1.0.66)
├── ZipArchiveEntity.cs                  # ZIP 固有エントリ (Method / HostOS 等 v1.0.66)
├── ArchiveFileEventArgs.cs              # per-file イベント引数 (v1.0.66)
├── AsyncPasswordQuery.cs                # 非同期パスワード問い合わせ + UI スレッドガード (v1.0.66 / v1.0.70)
├── ArchiveUpdateException.cs            # Update ロールバック失敗時の構造化例外 (v1.0.68)
├── Format.cs                            # Zip/7z/Tar/Rar/Iso/Udf 等の列挙
├── Options/
│   ├── ArchiveOption.cs                 # CodePage / Encoding / Filter / ThreadCount など共通オプション
│   └── CompressionOption.cs             # Writer 用: 圧縮レベル / メソッド / パスワード / CustomParameters / VolumeSize / IncludeEmptyDirectories / FlushToDisk / AtomicSave / KeepBackupOnUpdate + Validate() (v1.0.69〜v1.0.70)
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
    ├── FileSystemHelper.cs  # IsFileLocked 共通化 (v1.0.66)
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

### NativeAOT 対応の規約

COM Interop と P/Invoke は全面的に AOT 互換 API に移行済み:
- `[ComImport]` → `[GeneratedComInterface]`（ソースジェネレーター使用）
- `[DllImport]` → `[LibraryImport]`
- COM オブジェクトは `StrategyBasedComWrappers` + `CreateObjectFlags.UniqueInstance` で明示管理
- AOT 非互換コード（リフレクションベース）には `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` を付与
- `[GeneratedComInterface]` は例外ごとに固有の HRESULT を返すため、カスタムの `IProgress<Report>` 実装は全ての `ProgressState` 値を処理する必要がある（未処理だと例外が 7-Zip 側に伝播して中断する）

### コード規約

- `DisableImplicitNamespaceImports = true` — 全 using を明示的に記述
- `AllowUnsafeBlocks = true` — COM ポインタ操作のため（`ArchiveReader.Save(string, uint[], IProgress<Report>)` が `unsafe`）
- `#region` でセクション分割（Constructors / Properties / Methods / Fields）
- XML ドキュメントコメントは日本語
- `ArchiveReader` / `ArchiveWriter` は**同一スレッドで生成から破棄まで実行**する必要あり（非同期は `Task.Run` で一連の処理を包む）

## 既知の 7-Zip 26.00 挙動変化

本家 25.01 → 26.00 への移行に伴い、以下の挙動変化がテストに影響する。Cube 時代の Babel パッチ（SJIS 自動検出）が無くなったのが主因。

- **SJIS エンコード ZIP**: 自動検出されないため `ArchiveOption.CodePage = CodePage.Japanese` を明示指定する必要がある。`Extract_SampleUnixSjis` テスト参照。
- **Mac 製 ZIP (NFD)**: `名称未設定フォルダ` 等の濁点付き文字が NFD のまま展開される。テスト側で `FindEntity` ヘルパーが NFC/NFD の両方を試すフォールバック実装を持つ。
- **ZipSlipWin 系のパスサニタイゼーション**: バックスラッシュを含むエントリ名は Windows 禁止文字を Private Use Area (`U+F02F` / `U+F05C`) にエスケープする。特定ファイル名ではなく「destDir 外に漏洩しない」ことだけを `Extract_ZipSlipWin` で検証する。
- **CJK パスワードでの ZIP 作成**: upstream regression。`ZipCrypto` / `AES256` 共に `E_INVALIDARG` で失敗する（7z.exe CLI でも再現）。本家修正待ちのためテストケースから除外済み（`ArchiveWriterTest.cs` のコメント参照）。

## テストハーネス

- NUnit 4 をコンソールアプリ (`OutputType=Exe`) として実行
- `[OneTimeSetUp]` (`Tests/Core/Sources/Program.cs`) で `Logger.Configure(new LoggerSource("Cube.FileSystem.SevenZip.Tests.log"))` を呼ぶ
- `FileFixture` 継承で `Get()` / `GetSource()` ヘルパーが使える
- 期待値は `Tests/Core/Examples/Expected/{archive}.txt` に CSV 形式で記録

## CI/CD

GitHub Actions で `release/**` ブランチへの push 時に NuGet パッケージを自動公開（`.github/workflows/publish.yml`）。`main` ブランチには公開トリガーは設定されていない。
