# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

7-Zip COM インターフェースを利用した .NET 用圧縮・解凍ラッパーライブラリ。[Cube.FileSystem.SevenZip](https://github.com/cube-soft/cube.filesystem.sevenzip) のフォークで、.NET 10 / NativeAOT 対応が主な変更点。NuGet パッケージ ID は `1llum1n4t1s.Sevenzip`。

## ビルド・テストコマンド

```bash
# ソリューション全体のビルド
rtk dotnet build Cube.FileSystem.SevenZip.slnx -c Debug -p:Platform=x64

# Release ビルド
rtk dotnet build Cube.FileSystem.SevenZip.slnx -c Release -p:Platform=x64

# テスト実行（全テスト）
rtk dotnet test Tests/Core/Cube.FileSystem.SevenZip.Tests.csproj -c Debug -p:Platform=x64

# 単一テスト実行
rtk dotnet test Tests/Core/Cube.FileSystem.SevenZip.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TestMethodName"

# NuGet パック
rtk dotnet pack Libraries/Core/Cube.FileSystem.SevenZip.csproj -c Release -p:Platform=x64 -o Libraries/Core/bin -p:NoDefaultExcludes=true
```

**重要**: Platform は常に `x64` を指定する。出力先は `bin\x64\$(Configuration)\`。

## アーキテクチャ

### プロジェクト構成

- **Libraries/Core** — メインライブラリ（`Cube.FileSystem.SevenZip`）。公開 API は `ArchiveReader`（解凍）と `ArchiveWriter`（圧縮）。ライセンス: LGPLv3
- **Libraries/Cube.Core** — MVVM ユーティリティ、基底クラス（`DisposableBase`, `Bindable`, `IQuery<T>` 等）。ライセンス: Apache 2.0
- **Libraries/Cube.FileSystem.AlphaFS** — 長いパス対応の AlphaFS ラッパー
- **Libraries/Cube.Logging.NLog** — NLog ラッパー
- **Libraries/Cube.Trick** — 実験的機能
- **Tests/Core** — NUnit 4 によるテスト（コンソールアプリとして実行）

### 7-Zip COM Interop 構造（Libraries/Core）

```
Sources/
├── ArchiveReader.cs, ArchiveWriter.cs   # 公開 API（sealed クラス）
├── Format.cs, CompressionOption.cs      # 列挙型・オプション
├── ArchiveEntity.cs, Filter.cs          # エンティティ・フィルタ
└── Internal/
    ├── Interfaces/   # COM インターフェース定義（[GeneratedComInterface]）
    ├── Callbacks/    # COM コールバック実装（[GeneratedComClass]）
    └── ...           # ヘルパー、ネイティブ呼び出し
```

### NativeAOT 対応の規約

COM Interop と P/Invoke は全面的に AOT 互換 API に移行済み:
- `[ComImport]` → `[GeneratedComInterface]`（ソースジェネレーター使用）
- `[DllImport]` → `[LibraryImport]`
- COM オブジェクトは `StrategyBasedComWrappers` + `UniqueInstance` で管理
- AOT 非互換コード（リフレクションベース）には `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` を付与

### コード規約

- `DisableImplicitNamespaceImports = true` — 全 using を明示的に記述
- `AllowUnsafeBlocks = true` — COM ポインタ操作のため
- `#region` でセクション分割（Constructors / Properties / Methods / Fields）
- XML ドキュメントコメントは日本語
- ArchiveReader/ArchiveWriter は**同一スレッドで生成から破棄まで実行**する必要あり（非同期は `Task.Run` で一連の処理を包む）

## CI/CD

GitHub Actions で `release/**` ブランチへの push 時に NuGet パッケージを自動公開（`.github/workflows/publish.yml`）。
