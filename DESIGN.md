# システム設計

この文書は `1llum1n4t1s.Sevenzip` の現在の実装構造と、実装上の不変条件を記録する設計の正本です。利用方法と公開 API の例は [Readme.md](Readme.md)、開発時の必須コマンドと作業規約は [AGENTS.md](AGENTS.md) を参照してください。

## 目的とスコープ

本システムは、7-Zip の COM インターフェースを .NET 11 から利用する Windows 専用ラッパーです。公開 API は主に `ArchiveReader` と `ArchiveWriter` で構成し、パスと `Stream` の両方を入力・出力に使えます。

サポートするプロセスアーキテクチャは x64 と arm64 です。managed assembly は AnyCPU とし、実行時の RID に対応する `7z.dll` を NuGet runtime asset として同梱します。自己展開書庫（SFX）の生成はスコープ外です。

## 主要コンポーネント

| コンポーネント | 責務 | 境界 |
| --- | --- | --- |
| `Libraries/Core` | 公開 API、書庫エンティティ、オプション、7-Zip COM interop、コールバック、更新計画 | 利用者コードと native `7z.dll` の境界 |
| `ArchiveReader` | 書庫のオープン、エントリ列挙、パスまたは Stream への展開、パスワード・進捗・per-file イベント | 読み取り用 `IInArchive` と `ExtractCallback` を所有 |
| `ArchiveWriter` | 入力収集、新規作成、更新・削除、分割、クラッシュ耐性保存、進捗・per-file イベント | 書き込み用 `IOutArchive` と `UpdateCallback` を所有 |
| `SevenZipLibrary` | `7z.dll` の探索・ロード、`CreateObject` 呼び出し、COM wrapper と DLL lifetime の共有管理 | unmanaged handle と managed object の境界 |
| callbacks / interfaces | 7-Zip からの COM 呼び出しを Stream、ファイル I/O、進捗、イベント、例外へ変換 | `[GeneratedComInterface]` / `[GeneratedComClass]` による AOT 対応境界 |
| `UpdatePlan` | 既存エントリと追加・置換・改名・削除要求を、出力エントリ列へ統合 | 更新方針と COM callback の分離 |
| `FileSystemHelper` | 展開先の containment 検証、再解析ポイント拒否、親ディレクトリと出力ファイルのハンドル固定 | 書庫内パスと Windows filesystem namespace の境界 |
| `CompressionOption` / `ArchiveOption` | 形式別の圧縮、暗号化、文字コード、filter、保存耐性などを表現・検証 | 公開設定と 7-Zip property の境界 |
| `Libraries/Cube.Core` | `Io` / `IoController`、dispose 基盤、query、進捗型などの共通機能 | OS ファイルシステムを差し替え可能な抽象へ変換 |
| `Tests/Core` / `Tests/Private` | 公開 API、COM callback、文字コード、パス安全性、並行進捗、更新復旧の回帰検証 | 実際の vendored `7z.dll` と差し替え可能な I/O の両方を使用 |

`Cube.Core` は project reference では `PrivateAssets="all"` とし、ビルドした DLL と XML documentation をメイン NuGet パッケージへ直接収録します。

## 実行時データフロー

### 書庫の読み取りと展開

1. 利用者がパスまたは `Stream` から `ArchiveReader` を構築します。
2. `SevenZipLibrary.Acquire()` が native library の lease を取得し、形式に対応する `IInArchive` を生成します。
3. open callback が入力、パスワード、文字コードを 7-Zip へ提供し、reader がエントリ情報を `ArchiveEntity` として公開します。
4. `Save` または `Extract` は対象 index を昇順かつ重複なしへ正規化し、`ExtractCallback` を通して出力先ファイルまたは指定された Stream へ書き込みます。solid 書庫では選択対象より前の entry にも skip callback が来るため、callback は列挙位置ではなく archive index から対象を直接解決します。
5. callback が filter、パス安全性、進捗、per-file イベント、属性・時刻の確定を担当します。filesystem 出力では destination から親までを再解析ポイント非追跡で開き、削除・改名を許可しないハンドルを出力 Stream または属性設定の完了まで保持します。
6. dispose 時に COM object、入力 Stream の所有権、native library lease を解放します。

### 新規書庫の作成

1. `ArchiveWriter` の構築時と保存直前に、形式と `CompressionOption` の組み合わせを検証します。
2. `Add` がファイル、ディレクトリ、または Stream 入力を収集し、filter と安全な相対パスを適用します。Stream 入力は 7-Zip の file-oriented callback に合わせて一時ファイルを介します。
3. writer が `IOutArchive` を生成し、圧縮 property と `UpdateCallback` を設定して `UpdateItems` を呼びます。
4. callback がエントリ metadata、入力 Stream、パスワード、進捗、per-file イベントを 7-Zip へ提供します。
5. 通常保存は出力 Stream を完了させ、`VolumeSize` 指定時は完成物を後処理で複数 volume へ分割します。
6. `AtomicSave` 指定時は同一 volume の一時ファイルへ完成させてから最終パスへ置換し、`FlushToDisk` 指定時は OS の flush を追加します。

### 書庫の更新と削除

1. writer は既存書庫のエントリと、追加・置換・改名・`Remove` 要求から `UpdatePlan` を構築します。
2. `UpdateCallback` は plan の各 entry を keep / replace / add / rename / remove として 7-Zip へ提示します。
3. パス更新では元ファイルを backup し、新しい書庫の確定後に既存設定に従って backup を保持または削除します。
4. 置換または書き戻しが失敗した場合は rollback を試み、rollback 自体の失敗は `ArchiveUpdateException` に元パスと backup パスを保持して通知します。
5. `LastBackupPath`、`BackupPaths`、`LastTempPath` は異常時の復旧に必要な実パスを利用者へ公開します。

## 重要な不変条件

- `ArchiveReader` と `ArchiveWriter` の同一 instance は、常に 1 operation だけが触れます。スレッド固定は要求せず、`lock`、`SemaphoreSlim`、`await` などで直列化された受け渡しを許容します。
- x64 / arm64 以外のプロセスでは、native library を誤ってロードせず `PlatformNotSupportedException` で fail-fast します。
- COM object の lifetime は `StrategyBasedComWrappers` と参照カウント付き lease で明示管理し、DLL unload 後に wrapper や finalizer が native codeへ触れない順序を守ります。
- ライブラリは独自にログを出力しません。公開操作中の失敗は例外として呼び出し元へ返し、進捗と意図的なスキップは既存の progress / event API で通知します。
- finalizer 経路は全処理を例外境界内で実行し、未処理例外や出力をプロセスへ漏らしません。
- 7-Zip の multi-thread 圧縮では `SetCompleted` が並行に呼ばれるため、進捗状態、入力 Stream 一覧、一時ディレクトリはそれぞれの lock で保護します。一方、`GetStream` と `SetOperationResult` は全形式で 7-Zip 側が直列化し、「`GetStream(k)` → `SetOperationResult(k)` → `GetStream(k+1)`」の順で交互に呼ばれます。ただし呼び出しスレッドは対にならない（7z の LZMA2 ブロック MT では k の終了が k+1 を開くスレッドから届く）ため、処理中 entry はスレッド単位ではなくインスタンスで 1 つの「最後に開いた entry」として保持し、`SetOperationResult` のイベントと例外をその entry に結び付けます。ZIP MT の `SetOperationResult` は入力を圧縮スレッドへ渡した時点で呼ばれるため、`FileCompressed` は 7-Zip が当該 entry の入力を受け取った時点を表します。7z では空ファイルとディレクトリ、ZIP では新規ディレクトリに対して `GetStream` / `SetOperationResult` が呼ばれず、イベントも発火しません。
- callback 内の失敗は共通の exception stack へ保存します。`CallbackBase.ErrorHandler` は callback の構築時に生成し、その操作の COM Stream 間で共有します。書庫の入力 Stream（分割ボリュームを含む）は open 用 callback で作られるため、展開・更新の間だけ通知先をその操作の callback へ付け替え、通知済み状態を操作ごとにリセットします。エントリごとの delegate 割り当てを避けながら、例外を同じ同期済み stack へ通知します。stack が空の場合だけ純粋な利用者キャンセルとして扱い、I/O 失敗を `OperationCanceledException` に変換しません。
- 後始末の例外リストは最初の失敗時に確保します。成功時の割り当てを避けつつ、残りのリソース解放を続行し、単一の失敗は元の例外と stack trace、複数の失敗は処理順の `AggregateException` で通知します。finalizer では例外を外へ返しません。
- 圧縮進捗の completed value は書庫全体の累積値です。並行処理による後退値を再加算せず、単調な最大値として採用し、total を上限にします。
- `UpdateCallback` が開いた入力 Stream は callback の dispose まで保持します。これは multi-thread 圧縮時の COM 違反を防ぐ一方、処理中の write lock とメモリ使用量が対象ファイル数に比例するトレードオフです。
- ロック中ファイルは `Add` 時に共有読み取り可能か確認し、保存時の `UpdateCallback.Open` で通常オープンが共有・ロック違反になった場合に一時コピーを作成します。`FileShare.ReadWrite | FileShare.Delete` でも読めない完全排他ファイルはコピーできません。`SkipInaccessibleFiles` は `Add` 時点のアクセス不能（列挙できないディレクトリを含む）または再解析ポイントだけをスキップし、保存中の I/O 失敗は通常の保存失敗として通知します。`Add` が途中で失敗した場合、その呼び出しで追加した項目は取り消します。保存時のオープンでも、開いたファイルが再解析ポイントでないこと、および `Add` 時のパスと同じファイル（ボリュームシリアル番号とファイル ID）であることを検証し、`Add` 後の symlink / junction 差し替えを保存失敗として拒否します。途中の親ディレクトリの差し替えは検出対象外です。
- 再帰的な追加・copy・move は再解析ポイントを追跡せず、delete はリンク自体だけを削除します。展開先では destination から対象までを `FILE_FLAG_OPEN_REPARSE_POINT` で開き、親ハンドルでは `FILE_SHARE_DELETE` を許可しません。ハンドルを操作完了まで保持することで、検査後の junction / symlink 差し替えを含む再解析ポイント経由の書き込みを拒否します。さらに出力ファイルのハンドル取得直後（内容の切り詰め前）に `GetFinalPathNameByHandle` で実体が展開先 root の最終パス配下にあることを検証し、作成直後の親ディレクトリを junction 化される競合も拒否します。この競合時は展開先外に空ファイルが残る可能性があります（既存ファイルは切り詰めません）。最終パスを取得できないファイルシステムでは fail-closed で保存を失敗させます。
- 展開先は書庫内パスをそのまま信頼せず、destination 外へ書き出さないことを優先します。`SafePath` は escape 後に空となるセグメント（`...` や空白のみ等）を除去し、rooted / UNC 形式の名前を生成しません。名前を持たないエントリ（FNAME なし gzip などを Stream から開いた場合）には既定名 `[No name]`（2 件目以降は `[No name](n)`）を与え、何も出力せずに成功することを防ぎます。展開対象の index は native 呼び出し前に範囲検査します。filter 比較は locale に依存しない ordinal 規則を使います。
- 更新時の rename は null / 空文字だけを明示的な削除要求として扱い、非空入力が安全化後に空となる場合は `ArgumentException` で拒否します。`Add` との置換判定は rename 後のパスで行い（同じパスを `Add` すれば `Add` の内容が優先）、rename によって出力に同名エントリが生じる場合は書き込み前に `ArgumentException` で拒否します。rename したエントリのパス以外のプロパティ（時刻・属性・ディレクトリ種別）は元書庫から引き継ぎます。更新元の open 失敗は reader と同様に callback 例外、`S_FALSE`、負の HRESULT を区別します。
- ZIP の文字コードは host locale に依存させず、必要な入力では `ArchiveOption.CodePage` または `Encoding` を明示します。
- `AtomicSave` と分割書き出しなど、同時に成立しない option は、パスと Stream のどちらの保存・更新入口でも native 呼び出し前に `Validate` で拒否します。
- `packages.lock.json` は通常 restore で生成し、CI と公開前検証では `--locked-mode` で manifest と依存 graph の一致を要求します。

## 採用済み設計判断

### Source-generated interop を使う

NativeAOT と trimming に対応するため、COM は generated interop、P/Invoke は `LibraryImport` を使います。従来の runtime-generated interop より lifetime と HRESULT の扱いが明示的になる代わりに、callback interface、wrapper、例外伝播を自前で厳密に管理します。

### Native binary を NuGet に同梱する

x64 / arm64 の `7z.dll` を両方とも runtime asset として同梱し、利用者による別途インストールを不要にします。パッケージサイズと対応 OS を限定する代わりに、managed assembly と検証済み native binary の組み合わせを固定できます。

RID 指定ビルドや publish ではアセンブリ隣の `7z.dll` を使用し、RID なしの利用では `SevenZipLibrary` が `runtimes/win-{x64,arm64}/native/` も探索します。

リポジトリ内の `Libraries/Core/Native/{x64,arm64}/7z.dll` はローカル開発用の fallback です。公開 workflow は release branch 上で 7-zip.org から最新版を取得し、HTTPS と配信元 host allowlist を検査します。upstream binary が未署名の場合は署名を必須にせず、取得元制限と SHA256 の job summary 記録で追跡可能性を確保します。

7-Zip のライセンス情報をバイナリと合わせて配布するため、公開 workflow は DLL と同じ配布物から `License.txt` を取得し、見つからない場合は失敗します。取得したファイルは `Libraries/Core/Native/License.txt` に配置し、NuGet パッケージの `licenses/7-Zip/License.txt` に収録します。

### I/O を `Io` / `IoController` に集約する

ファイル操作を共通層へ集約し、production の OS I/O とテスト用 controller を同じ呼び出し面で扱います。global configuration を持つため、テスト teardown では標準 controller へ必ず戻します。

### クラッシュ耐性を option として提供する

atomic rename、disk flush、backup 保持は安全性を高めますが、追加 I/O、同一 volume 制約、書き込み時間の増加があります。そのため常時強制せず、用途に応じて `CompressionOption` で選択します。

### SFX を提供しない

SFX module の同梱と executable 連結は archive library の責務から外し、通常の archive 作成に集中します。SFX が必要な利用者は 7-Zip の module を別途管理します。

## ビルド・配布フロー

- `Directory.Build.props` が .NET 11 target、共通 version、AOT compatibility、lockfile 生成、Platform 別 output を定義します。
- `Cube.FileSystem.SevenZip.slnx` が x64 / arm64 の project mapping を明示し、solution build から Platform 指定を各 project へ伝播します。
- managed assembly は AnyCPU で build し、NuGet package には `runtimes/win-x64/native/7z.dll` と `runtimes/win-arm64/native/7z.dll` を収録します。
- `.github/workflows/publish.yml` は `release/**` push を契機に native binary 取得、locked restore、Release build、実 DLL を使う test、pack、package layout 検査（両 RID の `7z.dll` と `licenses/7-Zip/License.txt` の存在と取得元との SHA256 一致）、NuGet publish の順で実行します。.NET SDK は `publish.yml` と `dependency-validation.yml` で同じ版に完全固定し、`packages.lock.json` と同時に更新します。
- 製品 version は `Directory.Build.props` の `Version` を単一の正本とします。

## 検証資産

公開 API の統合テストに加え、次の設計契約を個別に回帰検証します。

- path / Stream の圧縮・展開・更新と所有権
- 暗号化、文字コード、NFC/NFD、Zip Slip・再解析ポイント越境防止と差し替え競合
- atomic save、backup、rollback、分割書き出し
- dispose 後の呼び出し、constructor 失敗時、finalizer safety
- lock 中ファイルの一時コピーと skip 通知
- callback の並行呼び出しと、スレッドを跨いで届く `SetOperationResult` と最後に開いた entry の対応、solid 書庫の部分展開、進捗値の単調最大・上限制御
- NuGet package layout（`publish.yml` の公開前検査で両 RID の native asset とライセンスを確認。arm64 の `7z.dll` のロードは arm64 ホストが無いため自動テスト対象外）
