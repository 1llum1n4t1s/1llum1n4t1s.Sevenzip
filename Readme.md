# 1llum1n4t1s.Sevenzip

**リポジトリ:** [https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip)

> **これは [Cube.FileSystem.SevenZip](https://github.com/cube-soft/cube.filesystem.sevenzip) のフォークです。** ソースコードは上記リポジトリで公開しています。  
> 元リポジトリとの主な違いは以下のとおりです。
> - **.NET 10** 対応（ターゲットを net10.0 / net10.0-windows に変更）
> - CI を AppVeyor から **GitHub Actions** に移行
> - NuGet パッケージ名を **1llum1n4t1s.Sevenzip** として公開
> - **NativeAOT 対応**（COM Interop を `[GeneratedComInterface]` に、P/Invoke を `[LibraryImport]` に全面移行）
> - [Cube.Core](https://github.com/cube-soft/cube.core) をソリューション内のプロジェクトとして直接組み込み（NuGet 参照ではなく NuGet パッケージに DLL を同梱）
> - **7-Zip 本家 26.00** のネイティブバイナリを直接 vendor（`Cube.Native.SevenZip` 依存を除去）
> - **Windows x64 / arm64 対応** — `<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>` を宣言。Managed アセンブリは AnyCPU で単一、ネイティブ 7z.dll のみ RID で分岐
> - **ピン留めネイティブバイナリ配布** — NuGet パッケージに固定バージョンの 7z.dll を同梱。ビルド時のネットワークアクセスなし（決定論的ビルド保証）。7-Zip 本体の更新はメンテナ側の週次 PR bot で追従
> - **SFX (自己展開書庫) 機能の削除** — `SfxOption` / `Format.Sfx` / `7z.sfx` 同梱を廃止。自己展開書庫が必要な場合は別途 SFX モジュールを用意する必要あり（**breaking change**, v1.0.58）
> - **NLog から SuperLightLogger への移行**（テストハーネス用）
> - **アーカイブ更新機能** — `ArchiveWriter.Update()` / `Remove()` で既存アーカイブのファイル追加・置換・削除が可能
> - **ロック中ファイル自動コピー** — 他プロセスがロック中のファイルを一時コピーして圧縮に含める
> - **CodePage サポート** — ZIP ファイル名のエンコーディングを `ArchiveOption.CodePage` で指定可能（Reader / Writer 両対応）
> - **EncryptionMethod** — `CompressionOption.EncryptionMethod` で暗号化方式（Aes256, ZipCrypto 等）を選択可能
> - **IoController FileShare オーバーロード** — `Io.Open(path, FileShare)` でロックモード指定に対応

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

ArchiveWriter および ArchiveReader は、生成から破棄まで同一スレッドで実行する必要があります。非同期に圧縮・解凍したい場合は、一連の処理全体を `Task.Run()` で実行してください。

## Upstream からの変更点・注意事項

本フォークでは NativeAOT 対応のために COM Interop を `[ComImport]` から `[GeneratedComInterface]` / `[GeneratedComClass]` へ、P/Invoke を `[DllImport]` から `[LibraryImport]` へ全面移行しています。これに伴い、upstream と比較して以下の仕様変更および注意点があります。

### 公開 API の変更

| メソッド / プロパティ | 変更内容 | 影響 |
|---------|---------|------|
| `ArchiveReader.Save(string, uint[], IProgress<Report>)` | `unsafe` 修飾子が追加 | **呼び出し側への影響なし。** C# では `unsafe` メソッドの呼び出しに `unsafe` コンテキストは不要です。`AllowUnsafeBlocks` はライブラリ側のビルドにのみ必要で、消費者側のプロジェクトには不要です。 |
| `ArchiveWriter.Update(...)` | **新規追加** — 既存アーカイブのファイル追加・置換 | 4 つのオーバーロード。`sourcePassword` で暗号化アーカイブの更新にも対応。 |
| `ArchiveWriter.Remove(string)` | **新規追加** — アーカイブ内の指定アイテムを削除 | `Update()` と組み合わせて使用。 |
| `ArchiveOption.CodePage` | **新規追加** — ZIP ファイル名エンコーディング指定 | `init` プロパティ。デフォルトは `CodePage.Oem`。Reader / Writer 両対応。 |
| `CodePage` enum | **新規追加** — `Oem`, `Utf8`, `Japanese` (932) 等 | ZIP 形式限定。7z 等は UTF-16 ネイティブのため不要。 |
| `EncryptionMethod` enum | **新規追加** — 暗号化方式の選択 | `Aes128`, `Aes192`, `Aes256`, `ZipCrypto`, `Default` |
| `CompressionOption.EncryptionMethod` | **新規追加** — 暗号化方式プロパティ | `init` プロパティ。デフォルトは `EncryptionMethod.Default`。 |
| `Io.Open(string, FileShare)` | **新規追加** — FileShare 指定付きオープン | Cube.Core の `IoController` にも対応オーバーロード追加。 |

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

#### CodePage サポート

`ArchiveOption.CodePage` で ZIP ファイル名のデコードに使用するコードページを指定できます。古いツールが Shift-JIS で作成した ZIP の文字化けを防ぐ場合などに利用します。7z 形式は UTF-16 ネイティブのため、この設定は ZIP 形式でのみ有効です。

```cs
// Reader: Shift-JIS でエンコードされた ZIP を正しく読む
var options = new ArchiveOption { CodePage = CodePage.Japanese };
using var reader = new ArchiveReader(@"path\to\sjis.zip", "", options);

// Writer: UTF-8 でファイル名をエンコードして ZIP を作成
var compOpts = new CompressionOption { CodePage = CodePage.Utf8 };
using var writer = new ArchiveWriter(Format.Zip, compOpts);
```

#### EncryptionMethod

`CompressionOption.EncryptionMethod` で暗号化方式を選択できます。選択肢は `Aes128`, `Aes192`, `Aes256`, `ZipCrypto`, `Default` です。

```cs
var options = new CompressionOption
{
    EncryptionMethod = EncryptionMethod.Aes256,
    Password         = "password",
};
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

* [7-Zip](https://www.7-zip.org/) … 本家 26.00 の `7z.dll` を `Libraries/Core/Native/x64/` および `Libraries/Core/Native/arm64/` に vendor して NuGet パッケージに同梱しています (Windows x64 / arm64 対応)。`runtimes/win-x64/native/` および `runtimes/win-arm64/native/` に配置されるため、.NET SDK のランタイム RID 解決により実行時に自動的に正しいバイナリが選択されます。コンシューマのビルド時にネットワークアクセスは一切発生しません（決定論的ビルド）。7-Zip 本体の新版追従はメンテナ側の週次 PR bot (`.github/workflows/update-7zip.yml`) で行います。

## License

[本リポジトリ](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip) は次のオリジナルプロジェクトをベースにしています。

| 由来 | リポジトリ | ライセンス |
|------|------------|------------|
| 7-Zip ラッパー | [cube-soft/cube.filesystem.sevenzip](https://github.com/cube-soft/cube.filesystem.sevenzip) | コアライブラリ: [GNU LGPLv3](https://www.gnu.org/licenses/lgpl-3.0.html)、その他: [Apache 2.0](https://www.apache.org/licenses/LICENSE-2.0) |
| ユーティリティ・MVVM 等 | [cube-soft/cube.core](https://github.com/cube-soft/cube.core) | [Apache 2.0](https://www.apache.org/licenses/LICENSE-2.0) |

各プロジェクトのライセンス表記および条件は、本リポジトリ内の [License.md](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/blob/main/License.md) およびソリューション内の各ライセンスファイルを参照してください。  
Copyright © 2010 [CubeSoft, Inc.](https://www.cube-soft.jp/)

Copyright © 2026 [ゆろち](https://github.com/1llum1n4t1s/)
