# レビュー記録 43 — spec08 image-to-video（作業中画像を fal-ai で動画化）セキュリティゲート

- 日付: 2026-08-17
- ゲート: セキュリティ（4段階中3）
- 対象: spec08（docs/specs/08-image-to-video.md §5 AC1-12）
- 前提: build 0/0、test 94 緑、実装ゲート PASS（記録42）
- 委譲: dotnet-claude-kit:security-scan（6層）。42crunch / claude-security は不適用（新規 HTTP API 面・OpenAPI なし。新規 dev エンドポイントは IsDevelopment ゲート下）
- 判定: **PASS**

## 6層サマリ
| 層 | 結果 |
|---|---|
| 1 パッケージ脆弱性 | PASS（`dotnet list package --vulnerable --include-transitive` 0 件、新規依存なし）|
| 2 シークレット | PASS（`.env` は `.gitignore`＝`.env.*` 除外・`!.env.example`、`.env.example`／`appsettings.json` の ChannelSecret/ChannelAccessToken/ApiKey は空値）|
| 3 OWASP パターン | PASS（injection/危険デシリアライズ/弱い暗号なし。SSRF 面は共有 `FalQueue`/`MediaRefetch` を再利用＝新規面ゼロ）|
| 4 認証/アクセス制御 | PASS（webhook 署名検証不変・新規本番 endpoint なし・`/media/{id}` は GUID.N 128bit ＝推測不可）|
| 5 CORS | PASS（変更なし）|
| 6 データ保護/ログ | PASS（HF トークン／参照画像 base64／モーション指示の非ログ化）|

## 重点確認（CLAUDE.md 脅威モデル）
1. **HF トークン送信先の非退行**: `ImageToVideoService` は submit/poll/result を共有 `FalQueue` に委譲。`FalQueue.ToRouterUrl` は `https://queue.fal.run/` 始まりのみ受理し `router.huggingface.co/fal-ai/…?_subdomain=queue` へ書き換え、`Bearer <ApiKey>` は router 宛の submit/poll/result にのみ付与。provider が返す任意ホストへトークンは送られない。回帰なし。
2. **SSRF 面**: 最終動画取得は `MediaRefetch.FetchAsync`（https 限定・allowlist ラベル境界一致・空 allowlist は全拒否＝fail-closed・**Authorization ヘッダ無し**）。allowlist ホストの追加なし（既定 `fal.media;replicate.delivery` 流用）。typed client は `AllowAutoRedirect=false`（`Program.cs` L63-65、他 fal サービスと同様＝リダイレクト経由の allowlist 迂回防止）。新規 SSRF 面ゼロ。
3. **秘密情報の非ログ化**: 参照画像は `data:{mime};base64,{...}` として submit body の `image_url` に載るのみ。`ImageToVideoService`／`FalQueue`／`MediaRefetch` にログ文なし。`WorkProcessor` のログは kind/user/eventId/messageId のみで画像本文・prompt・トークンを含まない。
4. **Webhook 署名検証・冪等性**: `/webhook` は生ボディ→`ParseAsync(body, signature)`→署名不正 401 / payload 不正 400 のまま不変。i2v は検証後の `DispatchAsync` 分岐追加のみ。生成前に `PrepareMediaAsync`→`processedEvents.TryMarkNew(WebhookEventId)` で重複配信を排除（既存経路踏襲）。
5. **gate（三重防御）**: `App__VideoEnabled=false` のとき ① QuickReply は Animate ボタンを出さない、② dispatcher の `action=animate` postback は early-return で `NotYetImplemented`、③ `WorkProcessor` の `case WorkKind.ImageToVideo` が `VideoEnabled` 判定＝false なら `NotYetImplemented`。arm 後に無効化されても③が最終防御。
6. **DoS 制御済み**: `VideoTimeoutSeconds`（既定300、下限5s）linked CTS で打ち切り、Queue 有界（既定100）、`TryMarkNew` 冪等、`MaxIncomingImageBytes`（10MB）で参照画像上限。

## 指摘
| # | 重大度 | 箇所 | 脆弱性/リスク | 必要な対応 |
|---|---|---|---|---|
| 1 | Low(Informational) | `Ai/HfHttp.cs` EnsureSuccessAsync | fal 非2xx 時に HF 応答本文（500字トランケート）を例外に含め、`WorkProcessor` 最上位 catch がログ。HF/fal 由来の本文でトークン非含・自前 submit body（image_url）非エコー | 残置（受容）。既存 `VideoService`/`ImageEditService` と同一挙動＝本変更由来ゼロ。上限 500 字でログ肥大も抑制済み |
| 2 | Low(Informational) | `ImageToVideoService.cs` L36 | 参照画像の base64 data URI 化で submit ボディ +約33%（受信写真は最大 ~13.3MB 相当）。fal 有料・クレジット重 | 残置（受容）。`MaxIncomingImageBytes`／Queue 有界／`VideoEnabled` 既定 OFF・opt-in で上限とコストが制御下。ドキュメントに有料注記あり |

- Critical / High / Medium: なし。

## 結論
Critical/High/Medium なし。差し戻し事項なし。トークン送信先（router 限定）・SSRF（共有ガード再利用・新規ホストなし）・秘密情報の非ログ化・署名検証と冪等性の非退行・`VideoEnabled` 三重ゲートをいずれも確認。指摘2件はともに Informational で既存慣行と同水準・受容可。ドキュメントレビュー（ゲート4）へ進行可。
