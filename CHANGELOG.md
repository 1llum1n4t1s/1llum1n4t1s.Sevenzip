# 変更履歴

Git のバージョン記録・コミット差分と既存の変更履歴をもとに、確認できた版ごとの変更点をまとめています。「Git 記録日」は公開日ではありません。番号の欠番だけから未確認のリリースは補っていません。

## 未リリース

## [1.0.88] — Git 記録日: 2026-08-30

- SuperLightLoggerを1.0.15へ更新

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/29090bb53b0089d12a1b5149697d793864318081) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/23cbf670d6143c5913ae7cf265d65a7c9a88eeef...29090bb53b0089d12a1b5149697d793864318081)。

## [1.0.87] — Git 記録日: 2026-08-30

- アーカイブ更新と展開処理の安全性を強化
- SuperLightLoggerを1.0.14へ更新
- NuGet公開をTrusted Publishingへ移行

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/23cbf670d6143c5913ae7cf265d65a7c9a88eeef) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/e4e84fc224abc0f745f11dd940fe95c8b212d5b0...23cbf670d6143c5913ae7cf265d65a7c9a88eeef)。

## [1.0.86] — Git 記録日: 2026-08-29

- アーカイブ処理のパス安全性を強化
- 依存パッケージを更新
- SuperLightLogger を 1.0.12 へ更新

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/e4e84fc224abc0f745f11dd940fe95c8b212d5b0) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/3ae6a25ee11d31e08db480d00d360f93c6c76482...e4e84fc224abc0f745f11dd940fe95c8b212d5b0)。

## [1.0.85] — Git 記録日: 2026-08-06

- SuperLightLogger を 1.0.11 へ更新

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/3ae6a25ee11d31e08db480d00d360f93c6c76482) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/d7496ca9796cfc3adf096ce62c51cfe2bc136809...3ae6a25ee11d31e08db480d00d360f93c6c76482)。

## [1.0.84] — Git 記録日: 2026-07-27

- 終了処理中の未保護の例外と、ネイティブライブラリの二重解放・参照カウントの競合を修正。
- 同じインスタンスへのアクセスを直列化していれば、異なるスレッドから利用できることを明確化。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/d7496ca9796cfc3adf096ce62c51cfe2bc136809) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/bfcf4953470a77e1fd0d2366db3774b1c98f7fd1...d7496ca9796cfc3adf096ce62c51cfe2bc136809)。

## [1.0.83] — Git 記録日: 2026-07-26

- ZIP テストの文字コードをサンプルに合わせて明示し、環境のロケールによる失敗を修正。公開処理のテストを再有効化。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/bfcf4953470a77e1fd0d2366db3774b1c98f7fd1) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/cca44f7e7a6dc50fb22144905057634aa4c818ef...bfcf4953470a77e1fd0d2366db3774b1c98f7fd1)。

## [1.0.82] — Git 記録日: 2026-07-26

- 展開対象の順序や重複による展開漏れ、圧縮失敗の情報欠落を修正。
- パスワード問い合わせのデッドロック防止と例外通知を改善し、I/O 障害と書庫破損を区別。
- 破棄済みインスタンスの操作、不正な日時、展開先への属性注入、更新失敗時のデータ保護を改善。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/cca44f7e7a6dc50fb22144905057634aa4c818ef) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/f9873a8efdb71a1c6a4dd6e271216cae542ad04f...cca44f7e7a6dc50fb22144905057634aa4c818ef)。

## [1.0.81] — Git 記録日: 2026-07-26

- NuGet 直接依存を最新の安定版へ更新

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/f9873a8efdb71a1c6a4dd6e271216cae542ad04f) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/f4d30c4511d866af0081f5bb13abe231707d5c58...f9873a8efdb71a1c6a4dd6e271216cae542ad04f)。

## [1.0.80] — Git 記録日: 2026-07-01

- 依存ライブラリとベンダー同梱 7z.dll を最新化

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/f4d30c4511d866af0081f5bb13abe231707d5c58) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/c206b18f91ab8860fdfd8394d076ecc4bb8b33db...f4d30c4511d866af0081f5bb13abe231707d5c58)。

## [1.0.79] — Git 記録日: 2026-06-12

- 圧縮進捗が途中で 100% に張り付く問題を修正し、並行した進捗通知でも値が逆行しないよう改善。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/c206b18f91ab8860fdfd8394d076ecc4bb8b33db) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/c929ca4a14c67d3ca4d418fbf732086f53e2b0b4...c206b18f91ab8860fdfd8394d076ecc4bb8b33db)。

## [1.0.78] — Git 記録日: 2026-06-11

- 圧縮・展開インスタンスの初期化失敗や終了時の資源解放を改善し、項目列挙時のメモリ漏れと破棄後の例外を修正。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/c929ca4a14c67d3ca4d418fbf732086f53e2b0b4) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/e4aa5b886b137df841058a8da915982aaf1b4f20...c929ca4a14c67d3ca4d418fbf732086f53e2b0b4)。

## [1.0.77] — Git 記録日: 2026-06-10

- ArchiveReader ctor 失敗時のリソースリークと finalizer 競合クラッシュを修正

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/e4aa5b886b137df841058a8da915982aaf1b4f20) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/e4a055f1fd366c1119675f9d0adc02f983bb19c7...e4aa5b886b137df841058a8da915982aaf1b4f20)。

## [1.0.76] — Git 記録日: 2026-06-07

- SkipInaccessibleFiles オプション追加（ロック中ファイルのスキップ続行対応）

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/e4a055f1fd366c1119675f9d0adc02f983bb19c7) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/8f0e1a15081559f1a91bba1b9c8174f47c60e3d7...e4a055f1fd366c1119675f9d0adc02f983bb19c7)。

## [1.0.75] — Git 記録日: 2026-06-06

- 依存ライブラリ更新

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/8f0e1a15081559f1a91bba1b9c8174f47c60e3d7) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/d48c9e7c1fdca2947ab238a7c789a2460d018805...8f0e1a15081559f1a91bba1b9c8174f47c60e3d7)。

## [1.0.74] — Git 記録日: 2026-06-06

- 書庫オープン失敗・中断判別のエラーハンドリング堅牢化

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/d48c9e7c1fdca2947ab238a7c789a2460d018805) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/a04f42e4ad1a6c342e9651f1888095f32786cdb5...d48c9e7c1fdca2947ab238a7c789a2460d018805)。

## [1.0.73] — Git 記録日: 2026-04-29

- 7-Zip 26.01 同梱 + リリースワークフロー統合

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/a04f42e4ad1a6c342e9651f1888095f32786cdb5) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/0af9d03098cb4d75391444905ad533f68110ad0e...a04f42e4ad1a6c342e9651f1888095f32786cdb5)。

## [1.0.72] — Git 記録日: 2026-04-29

- 7-Zip 自動更新ボットのダウンロード元を公式サイト (7-zip.org) に変更

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/0af9d03098cb4d75391444905ad533f68110ad0e) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/18d8a25d6172fa86bfd0856218df5473b26ec36e...0af9d03098cb4d75391444905ad533f68110ad0e)。

## [1.0.71] — Git 記録日: 2026-04-29

- テスト基盤パッケージ更新 (Microsoft.NET.Test.Sdk 18.5.1, NUnit3TestAdapter 6.2.0)

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/18d8a25d6172fa86bfd0856218df5473b26ec36e) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/ec71fc0bf0093d6e04248242cd1e37fc03603e1d...18d8a25d6172fa86bfd0856218df5473b26ec36e)。

## [1.0.70] — Git 記録日: 2026-04-19

- COM オブジェクトの二重解放と例外時の解放漏れを修正。
- 保持したバックアップの一覧を取得できるようにし、保存前の空き容量確認と更新失敗時の保護を強化。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/ec71fc0bf0093d6e04248242cd1e37fc03603e1d) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/df97bbb047d35dc3d6d982e331e0166ddb5f52b0...ec71fc0bf0093d6e04248242cd1e37fc03603e1d)。

## [1.0.69] — Git 記録日: 2026-04-19

- ディスクへの書き込み同期、原本を保護する一時ファイル経由の保存、更新時のバックアップ保持を任意に有効化できるよう追加。
- 直近のバックアップの保存先を取得できる LastBackupPath を追加。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/df97bbb047d35dc3d6d982e331e0166ddb5f52b0) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/ede5e36fa679a8ace8cb2f88dda71f57f9ab8aa2...df97bbb047d35dc3d6d982e331e0166ddb5f52b0)。

## [1.0.68] — Git 記録日: 2026-04-19

- 不正なパスワードを拒否し、同一ストリーム更新でメモリ不足になった場合の出力先保護を追加。
- ストリームから書庫を開く際の sourceHint を追加し、COM オブジェクトの管理を改善。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/ede5e36fa679a8ace8cb2f88dda71f57f9ab8aa2) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/41438dfbb26a6ee318d9fdb8d577a91d426f8cc2...ede5e36fa679a8ace8cb2f88dda71f57f9ab8aa2)。

## [1.0.67] — Git 記録日: 2026-04-19

- ストリームを使った書庫の読み書き・更新・展開と、更新時の項目名変更に対応。
- 非同期パスワード問い合わせ、ZIP の詳細情報、分割書庫と圧縮オプションを拡充。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/41438dfbb26a6ee318d9fdb8d577a91d426f8cc2) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/bb3123ad50d084e2c1c731bc003c2042d6b91c65...41438dfbb26a6ee318d9fdb8d577a91d426f8cc2)。

## [1.0.66] — Git 記録日: 2026-04-16

- バージョン更新・SuperLightLogger 依存を 1.0.6 に更新

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/bb3123ad50d084e2c1c731bc003c2042d6b91c65) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/cc99d8aa27744db41ac62a45108bb2ab6578f0d5...bb3123ad50d084e2c1c731bc003c2042d6b91c65)。

## [1.0.64] — Git 記録日: 2026-04-16

- ビルド時 auto-update 機構を撤去し決定論的ビルドに回帰 (breaking change)

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/cc99d8aa27744db41ac62a45108bb2ab6578f0d5) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/68779712a8935493cd1ef52959057b34787f2025...cc99d8aa27744db41ac62a45108bb2ab6578f0d5)。

## [1.0.62] — Git 記録日: 2026-04-16

- buildTransitive/*.targets の XML コメント内に含まれる不正な連続ハイフン (--) を修正

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/68779712a8935493cd1ef52959057b34787f2025) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/eded2b115d189fb7465b5e1e4eb24d2788cf17d9...68779712a8935493cd1ef52959057b34787f2025)。

## [1.0.60] — Git 記録日: 2026-04-16

- ARM64 対応・AnyCPU 移行・ビルド時 Auto-update 機構追加・SFX 機能削除 (breaking change)

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/eded2b115d189fb7465b5e1e4eb24d2788cf17d9) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/7392ac265a4b1e6f6e7fca3016cd2dc9251d3eb8...eded2b115d189fb7465b5e1e4eb24d2788cf17d9)。

## [1.0.56] — Git 記録日: 2026-04-15

- 7-Zip 26.00 vendoring・x64 限定化・未使用プロジェクト削除

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/7392ac265a4b1e6f6e7fca3016cd2dc9251d3eb8) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/0424186fdd30da106dc1471fb2ce754abc1c94d6...7392ac265a4b1e6f6e7fca3016cd2dc9251d3eb8)。

## [1.0.54] — Git 記録日: 2026-04-11

- ArchiveReader CodePage 対応・README 新機能ドキュメント追加

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/0424186fdd30da106dc1471fb2ce754abc1c94d6) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/29287b1c6f518e91c42e8a030813426b5fb48bcc...0424186fdd30da106dc1471fb2ce754abc1c94d6)。

## [1.0.52] — Git 記録日: 2026-04-11

- ロックファイル対応・IoController FileShare オーバーロード追加

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/29287b1c6f518e91c42e8a030813426b5fb48bcc) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/52308ad8b4a86233a668f61fe74975187b357298...29287b1c6f518e91c42e8a030813426b5fb48bcc)。

## [1.0.50] — Git 記録日: 2026-04-11

- ロックファイル対応・IoController FileShare オーバーロード追加

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/52308ad8b4a86233a668f61fe74975187b357298) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/a7d4fb92447a64a201244c305bb0757c83d4d933...52308ad8b4a86233a668f61fe74975187b357298)。

## [1.0.48] — Git 記録日: 2026-04-06

- アーカイブ更新機能・コメント書式標準化・パフォーマンス最適化

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/a7d4fb92447a64a201244c305bb0757c83d4d933) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/a417c48b2ba44e7fe5b485d9d34ff0347b0c32e5...a7d4fb92447a64a201244c305bb0757c83d4d933)。

## [1.0.44] — Git 記録日: 2026-03-10

- 書庫形式の初期化をスレッドセーフにし、署名検出時のメモリ割り当てを削減。
- 圧縮方式とエラー処理の重複を整理し、対応方式の取得をキャッシュ化。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/a417c48b2ba44e7fe5b485d9d34ff0347b0c32e5) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/8ccc6beb789d80ccf410fa3f63a6c560b14b48c0...a417c48b2ba44e7fe5b485d9d34ff0347b0c32e5)。

## [1.0.42] — Git 記録日: 2026-02-10

- AOT対応

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/8ccc6beb789d80ccf410fa3f63a6c560b14b48c0) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/93b3e8378c7c82abaf79ed6ebb86942c7d0e1224...8ccc6beb789d80ccf410fa3f63a6c560b14b48c0)。

## [1.0.38] — Git 記録日: 2026-02-07

- tar.gz・tar.bz2・tar.xz の外側の圧縮層に圧縮オプションが適用されない問題を修正。
- 空の書庫の項目数を 0 として扱い、.tbz2 の内部ファイル名を .tar として認識するよう修正。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/93b3e8378c7c82abaf79ed6ebb86942c7d0e1224) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/1436f862ad9d959b0717b4aa9c8ed70abc63921c...93b3e8378c7c82abaf79ed6ebb86942c7d0e1224)。

## [1.0.36] — Git 記録日: 2026-02-03

- Aot対応延期

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/1436f862ad9d959b0717b4aa9c8ed70abc63921c) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/b897a455816ca998c0c30ba5753bb4dee6866a18...1436f862ad9d959b0717b4aa9c8ed70abc63921c)。

## [1.0.34] — Git 記録日: 2026-02-03

- Aot対応
- ファイル整理
- Remove .vscode from repository

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/b897a455816ca998c0c30ba5753bb4dee6866a18) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/56388e0ce2034c2014138a9d3617c37e79842d27...b897a455816ca998c0c30ba5753bb4dee6866a18)。

## [1.0.32] — Git 記録日: 2026-02-01

- ファイル整理、ライブラリ更新
- 警告消し

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/56388e0ce2034c2014138a9d3617c37e79842d27) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/c217f408338f942f2ec97350cac49f9e6d26636d...56388e0ce2034c2014138a9d3617c37e79842d27)。

## [1.0.30] — Git 記録日: 2026-01-29

- 対象フレームワークを net10.0-windows へ変更し、ソリューション構成とビルド前のクリーン処理を整理。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/c217f408338f942f2ec97350cac49f9e6d26636d) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/53c17588f10f094a4e71182aee6f09cf0e3671aa...c217f408338f942f2ec97350cac49f9e6d26636d)。

## [1.0.24] — Git 記録日: 2026-01-28

- 圧縮・展開の進捗報告方法を修正。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/53c17588f10f094a4e71182aee6f09cf0e3671aa) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/5420b79baf98bdd212343bffacb89d0ab261b2ea...53c17588f10f094a4e71182aee6f09cf0e3671aa)。

## [1.0.18] — Git 記録日: 2026-01-27

- 配布用のバージョン情報を更新。記録された差分は版番号の変更のみ。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/5420b79baf98bdd212343bffacb89d0ab261b2ea) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/ffdd936cbf9183a57b63e78ec5cde88f9cb6d6c9...5420b79baf98bdd212343bffacb89d0ab261b2ea)。

## [1.0.12] — Git 記録日: 2026-01-26

- 配布用のバージョンと AlphaFS の参照情報を更新。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/ffdd936cbf9183a57b63e78ec5cde88f9cb6d6c9) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/6e73a84c3c5e4f88c67a817cfb69aa5d2b6e0e14...ffdd936cbf9183a57b63e78ec5cde88f9cb6d6c9)。

## [1.0.8] — Git 記録日: 2026-01-26

- 対象フレームワークを net10.0-windows8.0 に調整し、NuGet パッケージへ README を同梱。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/6e73a84c3c5e4f88c67a817cfb69aa5d2b6e0e14) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/1c799a535c9457f9ddffdd2cd59071ad11cc1055...6e73a84c3c5e4f88c67a817cfb69aa5d2b6e0e14)。

## [1.0.6] — Git 記録日: 2026-01-25

- x64固定化、アイコン設定

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/1c799a535c9457f9ddffdd2cd59071ad11cc1055) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/d0fd386bc9adbc2deb39fae3cbfc96ee5af21435...1c799a535c9457f9ddffdd2cd59071ad11cc1055)。

## [1.0.5] — Git 記録日: 2026-01-25

- 署名削除、アイコン設定
- Remove .idea from repository tracking
- ファイル整理

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/d0fd386bc9adbc2deb39fae3cbfc96ee5af21435) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/6c2a4f0c3458134ca9f5319c2552d018cea19098...d0fd386bc9adbc2deb39fae3cbfc96ee5af21435)。

## [1.0.1] — Git 記録日: 2026-01-25

- ファイル整理
- コピーライト表記修正

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/6c2a4f0c3458134ca9f5319c2552d018cea19098) / [変更差分](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/compare/904723e34518aa3eb2ed42be2ffc9df473fb2627...6c2a4f0c3458134ca9f5319c2552d018cea19098)。

## [1.0.0] — Git 記録日: 2026-01-25

- 1llum1n4t1s.Sevenzip フォークのパッケージ情報を設定。

出典: [版の記録](https://github.com/1llum1n4t1s/1llum1n4t1s.Sevenzip/commit/904723e34518aa3eb2ed42be2ffc9df473fb2627)。
