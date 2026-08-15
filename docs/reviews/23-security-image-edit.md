# セキュリティレビュー — 画像編集（image-to-image / Qwen-Image-Edit / spec03 3b） (2026-08-15)
Verdict: PASS
委譲分析: dotnet-claude-kit:security-scan（6層: 脆弱パッケージ / シークレット / OWASPパターン / 認証 / CORS / データ保護）

> 静的解析ベースのレビュー。既知パターンは検出するが、ペネトレーションテスト・動的解析・ビジネスロジック欠陥の網羅は保証しない。

## 指摘
| # | 重大度 | 箇所 | 脆弱性/リスク | 必要な対応 |
|---|---|---|---|---|
| 1 | Low | `Ai/ImageEditService.cs:33` / `Queue/WorkProcessor.cs:108` | 編集指示（`parameters.prompt`）に長さ上限なし＝過大入力による DoS 面。ただし LINE 1メッセージ長・キュー上限(100)・per-request timeout(既定120s)・ワーカ数2 で実効的に抑制。参照画像は自前 `MediaStore` の生成物バイトのみで攻撃者が任意サイズを注入不可。既存の chat/image プロンプトと同一姿勢。 | 受容可（記録）。将来、指示・プロンプト長の共通クランプを入れると堅牢。 |
| 2 | Low | `Program.cs:76-79`（`/media/{id}`） | 認証なしで TTL 内メディアを配信（IDOR 面）。id は `Guid.NewGuid("N")` で推測・列挙困難、かつ LINE が公開HTTPSで取得する設計上「公開前提」。3b で経路変更なし。 | 受容可（記録20/12 と同姿勢）。 |
| 3 | Info | `Messaging/MessageDispatcher.cs:50-67` / `State/UserStateStore.cs` | `AwaitingEdit` の非アトミック test-and-clear（実装ゲート Minor#1 引継ぎ）。セキュリティ的悪用（多重生成＝資源/課金増幅）を評価: LINE はユーザ単位で直列配信、かつ生成前段 `ProcessedEventStore.TryMarkNew(WebhookEventId)` で再配信を冪等排除、キュー境界(100)で上限あり。増幅経路にならず実害無視可。 | 受容可（記録）。厳密化は任意（`TryConsumeAwaitingEdit` 集約）。 |
| 4 | Info | 全 typed client（`Program.cs:53-61`） | 情報: SSRF 統制の中核（allowlist/https/no-auth/no-redirect）が3経路共通で維持されていることの確認記録。 | 対応不要。 |

## 重点確認結果（依頼 5 項目）
1. **プロンプトインジェクション/入力検証** — 問題なし。編集指示は `JsonContent.Create(new { inputs, parameters = new { prompt = instruction } })` でシリアライザ生成のため JSON 構造破壊なし（手組み文字列連結なし）。指示長の上限なしは DoS 面として Low#1 に記録（実効抑制あり）。指示テキスト・プロンプト・ApiKey はいずれもログ非出力（構造化ログは userId/kind/eventId/mode のみ）。
2. **SSRF 回帰** — 問題なし。img2img 応答の JSON-URL 再取得は 3 サービス共通の `MediaResponse.ReadAsync` → `MediaRefetch.FetchAsync` を通過し、(a) scheme=https 限定、(b) allowlist ラベル境界一致（空=全拒否のフェイルクローズ、`fal.media` は `cdn.fal.media` を許可し `evilfal.media` を拒否）、(c) Authorization 非同送（HF資格情報を第三者ホストに送らない）、(d) per-request timeout を継承。加えて `IImageEditService` の typed client に `AllowAutoRedirect=false`（`Program.cs:59-61`）が設定され、3xx による allowlist 迂回を封じる。spec02 記録20 の統制が 3b 経路でも有効。
3. **参照画像の取り違え/権限** — 問題なし。`RefImageId` は `MessageDispatcher.HandleTextAsync` で `snapshot.LastImageId`（userId キーのサーバ側 per-user 状態）からのみ設定され、`WorkItem` にピン留めされる。ユーザのテキスト・postback から任意 id を注入する経路は存在しない（postback は `action`/`value` のみ解釈、`edit` アクションも自ユーザ状態を参照）。`MediaStore` id は GUID で他ユーザ id の推測・横断アクセス不可。別ユーザ混線なし。
4. **AwaitingEdit の並行競合（セキュリティ観点）** — 実害度 無視可（Info#3）。非アトミック消費だが、直列配信＋eventId 冪等排除＋キュー境界で多重課金的増幅に至らない。
5. **秘密情報非出力・脆弱パッケージ・回帰** — 問題なし。ApiKey は `Bearer` ヘッダ（Image/Video/ImageEdit 各サービス）と SK コネクタ設定のみで使用、ログ・例外メッセージ・コミットに非露出。`.env` は `.gitignore` 済み・`.env.example` はプレースホルダのみ。`dotnet list package --vulnerable --include-transitive`＝脆弱0（3b で新規パッケージ導入なし）。署名検証（`Program.cs:86-120`、記録17）・3a/spec02 の SSRF 統制に回帰なし。

## 層別結果
| 層 | 判定 | 所見 |
|---|---|---|
| 1. 脆弱パッケージ | PASS | CVE 0（新規パッケージなし） |
| 2. シークレット検出 | PASS | ハードコード無し。`.env` gitignore、`.env.example` はプレースホルダ |
| 3. OWASP コードパターン | PASS | JSON はシリアライザ生成（インジェクション無し）、SSRF 統制維持、RefImageID は非ユーザ制御 |
| 4. 認証 | PASS | webhook=HMAC署名検証（回帰なし）、`/dev/*` は Development 限定、3b で認証面変更なし |
| 5. CORS | PASS(N/A) | ブラウザ向け API 無し・CORS 未設定 |
| 6. データ保護 | PASS | PII/指示/ApiKey のログ非出力、メディアは TTL キャッシュ |

## 判定理由
Critical/High/Medium 0、Low 2・Info 2、脆弱パッケージ 0。差し戻すべき重大事項なし。3b の新経路（編集指示＋自前 `MediaStore` 参照画像を base64 で HF image-to-image に送信）は、(1) ペイロードがシリアライザ生成でインジェクション面なし、(2) 応答 JSON-URL 再取得が spec02 の必須 SSRF 統制（https/allowlist ラベル境界/フェイルクローズ/no-auth/timeout）＋`AllowAutoRedirect=false` を共通 `MediaResponse` 経由で確実に通過、(3) 参照画像 id が完全にサーバ側 per-user 状態由来で他ユーザ横断・任意注入不可、(4) 秘密情報のログ/コミット非露出、を満たす。残る指摘は入力長上限なし（実効抑制あり）・GUID 保護済み `/media` IDOR・AwaitingEdit の理論的競合（増幅経路なし）で、いずれもドキュメントレビューをブロックしない。PASS。
