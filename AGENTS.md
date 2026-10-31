# AGENTS.md

この文書は本リポジトリで作業するエージェント向けの規約と検証手順です。システム構造と設計判断は [DESIGN.md](DESIGN.md)、利用方法は [Readme.md](Readme.md) を参照してください。

## プロジェクト概要

7-Zip COM インターフェースを利用した .NET 10 向け圧縮・解凍ラッパーライブラリ。[Cube.FileSystem.SevenZip](https://github.com/cube-soft/cube.filesystem.sevenzip) のフォークで、.NET 10 / NativeAOT 対応 + 本家 7-Zip バイナリの vendoring が主な変更点。NuGet パッケージ ID は `1llum1n4t1s.Sevenzip`。**Windows x64 / arm64 専用**。公開 API は `ArchiveReader`（解凍）と `ArchiveWriter`（圧縮）の 2 クラス。Stream ベース API / per-file 進捗イベント / アーカイブ更新・削除 / クラッシュ耐性オプション（AtomicSave / FlushToDisk / KeepBackupOnUpdate）等を本家から大幅に拡張。SFX 機能は v1.0.58 で削除済み (breaking change)。

## ビルド・テストコマンド

```bash
# lockfile と依存関係の整合性を確認
dotnet restore Cube.FileSystem.SevenZip.slnx --locked-mode

# ソリューション全体のビルド（Managed DLL は AnyCPU、ネイティブ 7z.dll のみ RID で分岐）
dotnet build Cube.FileSystem.SevenZip.slnx -c Debug -p:Platform=AnyCPU

# Release ビルド
dotnet build Cube.FileSystem.SevenZip.slnx -c Release -p:Platform=AnyCPU

# テスト全実行（--no-build を付けるとビルドをスキップ）
dotnet test Tests/Core/Cube.FileSystem.SevenZip.Tests.csproj -c Debug -p:Platform=AnyCPU --no-build

# 単一テスト実行（FullyQualifiedName で絞り込み）
dotnet test Tests/Core/Cube.FileSystem.SevenZip.Tests.csproj -c Debug -p:Platform=AnyCPU --no-build --filter "FullyQualifiedName~TestMethodName"

# NuGet パック（NoDefaultExcludes=true がないとネイティブアセットが packing されない）
dotnet pack Libraries/Core/Cube.FileSystem.SevenZip.csproj -c Release -p:Platform=AnyCPU -o Libraries/Core/bin -p:NoDefaultExcludes=true
```

**重要**:
- Platform のデフォルトは `AnyCPU`（`Directory.Build.props` で `<Platforms>AnyCPU;x64;arm64</Platforms>`）。出力先は `bin\$(Configuration)\`。x64 / arm64 明示時は `bin\x64\...` / `bin\arm64\...` に分岐。
- `-p:Platform` を各プロジェクトへ伝播させるには `.slnx` の per-project `<Platform Solution="*|x64" Project="x64" />` mapping が必要。これが無いと solution 経由のビルドは指定を無視して全プロジェクトを AnyCPU で解決し、同梱ネイティブ 7z.dll の選択と出力先分岐が効かなくなる（`.slnx` にプロジェクトを追加したら mapping も追加する）。
- テスト実行時は Tests/Core が RID を Platform に追従させるため、出力は `bin\Debug\net10.0-windows8\win-x64\`（AnyCPU / x64）または `bin\arm64\Debug\net10.0-windows8\win-arm64\`（arm64）に配置される。RID を `win-x64` 固定にすると `-p:Platform=arm64` で `PlatformTarget` と衝突して `NETSDK1032` になる。arm64 のテスト実行自体は arm64 ホストが必要。
- managed アセンブリはアーキ非依存で、NuGet パッケージは Platform に関係なく x64 / arm64 両方のネイティブ 7z.dll を同梱する。pack は AnyCPU で行えば十分。
- Tests/Core の RID が Platform 依存なので、`Tests/Core/packages.lock.json` の RID セクション（`net10.0-windows8.0/win-x64`）も Platform 依存になる。**`-p:Platform=arm64` で restore / build すると `win-arm64` に書き換わる**。CI は `dotnet restore --locked-mode`（AnyCPU）で検証するため、arm64 を試した後は、開始時の未コミット変更を保護したうえで、今回の検証による RID 差分だけを戻し、AnyCPU の `--locked-mode` restore を確認する。

## バージョン管理

バージョンは `Directory.Build.props` の `<Version>` タグで一元管理（全プロジェクト共通）。patch を +1 ずつインクリメント（`/vava` スキルで自動化）。コミットメッセージは日本語。

## 実装・配布時の制約

構成、COM 境界、入出力の流れ、ロック中ファイルと再解析ポイントの扱いは [DESIGN.md](DESIGN.md) を正本とします。

- `Libraries/Core/Native/{x64,arm64}/7z.dll` は `.gitignore` の `!Libraries/Core/Native/**/*.dll` で追跡を維持します。
- ネイティブバイナリまたは配布処理を更新するときは、DLL と同じ配布物のライセンスも同期し、pack 後の `.nupkg` に両アーキテクチャの DLL と `licenses/7-Zip/License.txt` が含まれることを確認します。取得・配置の設計は [DESIGN.md](DESIGN.md#native-binary-を-nuget-に同梱する) を参照してください。
- I/O を差し替えるテストでは `SourceFileFixture.Teardown()` の `Io.Configure(new())` による標準実装への復帰を維持します。`Io.Open(path)` は `FileShare.Read`、共有モードが必要な処理は `Io.Open(path, share)` を使います。
- `SkipInaccessibleFiles` は `Add()` 時のアクセス不能・再解析ポイントをスキップする設定です。保存中に生じた I/O 失敗をキャンセルや黙示スキップへ変換しないでください。

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
- `ArchiveReader` / `ArchiveWriter` は**1 インスタンスを同時に触るスレッドを常に 1 つに保つ**（スレッドアフィニティは要求しない。`SemaphoreSlim` / `lock` / `await` による happens-before があればスレッドを跨いでよい。単純な非同期は `Task.Run` で一連の処理を包む）
- finalizer 経路（`Dispose(false)`）の処理は**全て try/catch で囲む**。未処理例外はプロセスごと即死するため。`SevenZipLibrary.ReleaseFromFinalizerSafe` に共通化済み

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
- `_stateLock` — `_streams`（開いたストリームのリスト）/ `_tempDir`（ロック中ファイルの一時コピー先）
- `_currentIndex` — callback worker ごとの `ThreadLocal<int>`。`GetStream` と対応する `SetOperationResult` のエントリを他 worker と混同しない
- `CallbackBase.PushException()` — `Exceptions` スタックへの積み込み。**`Exceptions.Push` を直接呼ばない**

自分の解決済みインデックスを持つ呼び出し元は `Current(int)` を使い、引数なしの `Current()` は現在 callback worker の `_currentIndex` を読む。`SetOperationResult` は処理開始時に index と entity をローカルへ確定し、イベント・例外・進捗で同じ組を使う。

失敗を `Exceptions` へ積むのは必須である。`ThrowIfError` は `Exceptions` が空なら「純粋なユーザーキャンセル」と判定して `OperationCanceledException` を投げるため、積み忘れると実際の I/O 失敗が「利用者が中断した」に化ける。

## テストハーネス

- NUnit 5 をコンソールアプリ (`OutputType=Exe`) として実行
- テストハーネスはロガーを初期化せず、ログファイルも生成しない
- `FileFixture` 継承で `Get()` / `GetSource()` ヘルパーが使える
- 期待値は `Tests/Core/Examples/Expected/{archive}.txt` に CSV 形式で記録

## CI/CD

GitHub Actions で `release/**` ブランチへの push 時に、7-zip.org から最新 7z.dll を取得 → ビルド → テスト → NuGet パッケージを自動公開（`.github/workflows/publish.yml`）。`main` ブランチには公開トリガーは設定されていない。

### テストのロケール非依存性

CI の runner は **ja-JP ロケールではない**（OS の非 Unicode プログラム用コードページが CP932 ではない）。7-Zip 26.x は UTF-8 フラグ（bit11）の立っていない ZIP のエントリ名を自動検出しないため、**非 ASCII 名を含むアーカイブを扱うテストは `ArchiveOption.CodePage` を明示指定する**。指定を省くとホストのロケールで結果が変わり、ローカル（ja-JP）では通るのに CI で落ちる。

実体のエンコーディングは各アーカイブの生バイト列から判定済み。

| アーカイブ | bit11 | 生バイト列 | 指定 |
|---|---|---|---|
| `SampleComma.zip` / `SampleKanji.zip` / `SampleFilter.zip` / `SampleUnixSjis.zip` | なし | cp932 でのみデコード可 | `CodePage.Japanese` |
| `SampleMac.zip` | なし | utf-8 でのみデコード可（NFD） | `CodePage.Utf8` |
| `SampleUtf8.zip` | **あり** | — | 指定不要 |

`ArchiveReaderTest.Test` は `TestCases` を `Extract` と共有しているため、引数を増やすときは**両メソッドのシグネチャを揃える**（片方だけだと `Too many arguments provided` で失敗する）。

`Items_ExplicitCodePage_OverridesHostCodePage` が「正しい CodePage では期待名が得られ、誤った CodePage では得られない」ことを検証しており、デコードがホストのコードページではなく指定値に支配されている根拠になっている。`ArchiveReaderTest.IgnoreCultureError` は `EncryptionException` だけを非 ja-JP 環境で無視するヘルパーで、アサーション失敗は素通りするため**ロケール依存を隠す用途に使わない**。

**本家 7-Zip のバイナリは Authenticode 署名されていない**（26.02 の GitHub リリース installer と同梱 `7z.dll` はいずれも `Get-AuthenticodeSignature` が `NotSigned` を返すことを実測確認）。そのため `Test-7zSignature` は「署名があれば厳密検証、無ければ警告して続行」とする。**ここを「未署名なら失敗」に変えるとリリースが全て止まる**。未署名版に対する改ざん対策は次の 2 つで担保している。

- `Assert-AllowedUrl` による HTTPS 強制 + 配信元ホスト allowlist（`www.7-zip.org` / `7-zip.org` / `github.com` / `objects.githubusercontent.com`）。ダウンロード URL はページのスクレイプ由来なので、ホストを限定しないと任意の配信元へ誘導されうる。
- 同梱した 7z.dll の FileVersion と SHA256 を job summary へ記録（CI ログは保持期間で消えるため、後から「どの DLL が入ったパッケージか」を切り分けるための証跡）。
