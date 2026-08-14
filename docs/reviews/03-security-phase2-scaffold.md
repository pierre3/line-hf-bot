# セキュリティレビュー — phase2-scaffold (2026-08-14)
Verdict: **PASS**
委譲分析: `dotnet-claude-kit:security-scan`（利用可）＋固有重点の手動確認。
脆弱パッケージは `dotnet list package --vulnerable --include-transitive` で **0 件**。
対象: 実装済み範囲（Options / /health / /webhook / Channel キュー＋worker / dispatcher）

## 判定サマリ
Critical/High/Medium の実在リスクなし。署名検証は生ボディで実施し失敗を 401/400 で確実に弾く。
秘密情報はコード・ログ・例外・レスポンスに露出なし（appsettings は空プレースホルダ、`.gitignore` が `.env` 系を除外）。
DoS は BoundedChannel の上限＋即2xx＋非同期委譲で緩和。インジェクションシンクなし。

## 指摘
| # | 重大度 | 箇所 | リスク | 対応方針 |
|---|--------|------|--------|----------|
| 1 | Low | Program.cs / BotOptions.cs | ChannelSecret 未設定だと空鍵で署名検証され、空鍵署名の偽装 webhook を受理し得る（運用者の設定ミス前提） | **起動時に必須値バリデーション**（ValidateOnStart）を追加。→ 次増分で対応（impl #4 と同一） |
| 2 | Info | StubWorkProcessor.cs / MessageDispatcher.cs | ユーザーテキスト全文を Information でログ（構造化渡しでインジェクション耐性あり） | 将来 PII/長文対策として長さ制限・制御文字サニタイズ |
| 3 | Info | Program.cs | webhook ボディを MemoryStream に全読み込み（Kestrel 既定30MB 上限に依存） | 必要なら webhook 用 `MaxRequestBodySize` を設定 |

## フォローアップ
指摘#1 は実装レビュー #4 と同根。次のチャット増分で `ValidateOnStart` を実装する。
