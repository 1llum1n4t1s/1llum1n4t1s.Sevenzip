# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

7-Zip COM インターフェースを利用した .NET 10 向け圧縮・解凍ラッパーライブラリ。[Cube.FileSystem.SevenZip](https://github.com/cube-soft/cube.filesystem.sevenzip) のフォークで、.NET 10 / NativeAOT 対応 + 本家 7-Zip 26.00 バイナリの vendoring が主な変更点。NuGet パッケージ ID は `1llum1n4t1s.Sevenzip`。**Windows x64 専用**。

## ビルド・テストコマンド

```bash
# ソリューション全体のビルド（Platform=x64 は必須）
rtk dotnet build Cube.FileSystem.SevenZip.slnx -c Debug -p:Platform=x64

# Release ビルド
rtk dotnet build Cube.FileSystem.SevenZip.slnx -c Release -p:Platform=x64

# テスト全実行（--no-build を付けるとビルドをスキップ）
rtk dotnet test Tests/Core/Cube.FileSystem.SevenZip.Tests.csproj -c Debug -p:Platform=x64 --no-build

# 単一テスト実行（FullyQualifiedName で絞り込み）
rtk dotnet test Tests/Core/Cube.FileSystem.SevenZip.Tests.csproj -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~TestMethodName"

# NuGet パック（NoDefaultExcludes=true がないとネイティブアセットが packing されない）
rtk dotnet pack Libraries/Core/Cube.FileSystem.SevenZip.csproj -c Release -p:Platform=x64 -o Libraries/Core/bin -p:NoDefaultExcludes=true
```

**重要**:
- Platform は常に `x64` を指定（`Directory.Build.props` で `<Platforms>x64</Platforms>` 縛り）。出力先は `bin\x64\$(Configuration)\`。
- テスト実行時は Tests/Core が `RuntimeIdentifier=win-x64` を設定しているため、出力は `bin\x64\Debug\net10.0-windows8\win-x64\` に配置される。

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

`Libraries/Core/Native/x64/7z.dll` (7-Zip 26.00) および `7z.sfx` を直接 vendor し、NuGet パッケージの `runtimes/win-x64/native/` に同梱する。これらは **`.gitignore` の `*.dll` / `*.sfx` ルール** に引っかかるため、`.gitignore` の末尾で例外ルール `!Libraries/Core/Native/**/*.dll` / `!Libraries/Core/Native/**/*.sfx` により明示的に追跡している。

`7z.dll` は `SevenZipLibrary` (Libraries/Core/Sources/Internal/SevenZipLibrary.cs) がアセンブリ隣から `LoadLibrary` で読み込む。x64 以外のプロセスでは構築時に `PlatformNotSupportedException` を投げる（ランタイムガード）。

### 7-Zip COM Interop 構造（Libraries/Core）

```
Sources/
├── ArchiveReader.cs, ArchiveWriter.cs   # 公開 API (sealed クラス)
├── Format.cs                            # Zip/7z/Tar/Rar/Iso/Udf 等の列挙
├── ArchiveOption.cs                     # CodePage / Filter / ThreadCount など共通オプション
├── CompressionOption.cs                 # Writer 用: 圧縮レベル / メソッド / パスワード / EncryptionMethod
├── Options/                             # SfxOption, TarOption 等
└── Internal/
    ├── Interfaces/   # COM インターフェース定義 ([GeneratedComInterface])
    ├── Callbacks/    # COM コールバック実装 ([GeneratedComClass])
    │   ├── UpdateCallback.cs   # 圧縮時: IArchiveUpdateCallback + ICryptoGetTextPassword2
    │   ├── OpenCallback.cs     # 解凍時のオープン処理
    │   └── PasswordCallback.cs
    ├── Options/      # CompressionOptionSetter 系（Format 別に `em`/`cp`/`m` プロパティを 7z.dll に注入）
    └── SevenZipLibrary.cs       # 7z.dll の参照カウント付き singleton + CreateObject 関数ポインタ
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
