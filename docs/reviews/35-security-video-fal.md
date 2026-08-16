# 35 — セキュリティレビュー: 動画 fal-ai プロバイダ経由 (spec 06)

- 日付: 2026-08-16
- 対象: text-to-video を fal-ai 非同期キュー経由に対応（動画プロバイダ統合）
  - 新規 `LineHfBot/Ai/FalQueue.cs`（internal static, 共通キューヘルパー）
  - `LineHfBot/Ai/VideoService.cs`（fal キュー方式へ全面書換）
  - `LineHfBot/Ai/ImageEditService.cs`（FalQueue 利用へリファクタ・挙動不変）
- 前段ゲート: 実装 = `docs/reviews/34-impl-video-fal.md`（PASS。引継ぎ Minor#3 = refetch サイズ上限なし）
- 委譲分析: dotnet-claude-kit:security-scan（6層）＋ 本プロジェクト固有の重点確認（署名検証／トークン漏洩／SSRF）
- 判定: **PASS**（Critical/High/Medium 0）

## スコープ
hf-inference が text-to-video 非対応のため、動画生成を fal-ai プロバイダの非同期キュー
（submit → poll → result → SSRF ガード再取得）へ移行。共通処理を `FalQueue`（internal static）に
抽出し image-edit と共有。`IVideoService` 契約不変で上位（`WorkProcessor.HandleVideoAsync`・
typed client・`/dev/video`）は無改修。新規 NuGet なし。`App__VideoEnabled` 既定 false（opt-in／fal 有料）。

## レイヤ結果
| 層 | 結果 | 備考 |
|---|---|---|
| 1 脆弱パッケージ | PASS | `dotnet list package --vulnerable --include-transitive` = LineHfBot / LineHfBot.Tests とも 0 件 |
| 2 シークレット | PASS | `.env.example` は空プレースホルダのみ。`.env`/`.env.*` は `.gitignore` 済。ApiKey/token を source・log に非出力 |
| 3 OWASP パターン | PASS | SSRF はトークン非同送＋allowlist＋AutoRedirect 無効で多層防御（下記重点確認）。注入面なし（body はシリアライザ生成） |
| 4 認証/エンドポイント | PASS | `/dev/video` は `app.Environment.IsDevelopment()` ガード内（Program.cs:126）。video 経路は署名検証後 `DispatchAsync`→queue→`HandleVideoAsync` で不変。`VideoEnabled` ゲートあり |
| 5 CORS | PASS | 変更なし |
| 6 データ保護 | PASS | ログは userId/kind/eventId/messageId のみ（`WorkProcessor.cs:74,147,180`）。例外本文は HF 応答由来を 500 字トランケート（`HfHttp`）で request の Authorization ヘッダは非混入 |

## 重点確認（本プロジェクト固有）
- **Webhook 署名検証**: 本変更で非接触。動画経路は既存の検証後（記録17）`DispatchAsync`→`ChannelWorkQueue`→
  `GenerationWorker`→`WorkProcessor.HandleVideoAsync` で不変。回帰なし。
- **トークン漏洩（HF token は router のみ）**: `FalQueue.ToRouterUrl` は `https://queue.fal.run/`
  （末尾スラッシュ込）で始まる URL のみ受理し `router.huggingface.co/fal-ai/…?_subdomain=queue` へ書換。
  他ホスト/http/サブドメイン偽装（例 `queue.fal.run.evil.com`）は `Ordinal` 前方一致で不成立→例外＝
  fail-closed（プロバイダが返す任意 URL に Bearer を送らない）。submit/poll/result は書換済み router URL に対してのみ
  `Authorization: Bearer` を付与。最終メディア取得（`MediaRefetch`）は **Authorization ヘッダなし**。
- **SSRF / 出力の安全性**: 最終 `video.url` は `MediaRefetch.FetchAsync` で
  (1) 絶対 URI 必須 (2) **https 限定** (3) allowlist **ラベル境界一致**（`fal.media` は `cdn.fal.media` を許可、
  `evilfal.media` を拒否／空=全拒否 fail-closed） を通過。加えて typed client は `AllowAutoRedirect=false`
  （Program.cs:57-59）で 3xx による allowlist 迂回を封鎖。`video.url` は Object かつ非空を fail-closed 抽出
  （欠落時は例外）。入力の外部 URL を無検証で扱う経路なし（submit body は `{prompt}` のみ）。
- **DoS 面**: poll は ~1s 間隔＋`VideoTimeoutSeconds`(300) の linked CTS で必ず打ち切り、未知 status は早期例外
  （無限ポーリング不成立）。生成は `Queue.Capacity`(100)/`Workers`(2) で有界。`VideoEnabled` 既定 false。

## 指摘
| # | 重大度 | 箇所 | 脆弱性/リスク | 必要な対応 |
|---|---|---|---|---|
| 1 | Low | `Ai/MediaRefetch.cs:40` `FetchAsync` の `ReadAsByteArrayAsync` | 明示的サイズ上限なし（実装ゲート引継ぎ Minor#3）。動画は画像より本体が大きく、悪性/誤動作の allowlist ホストが巨大応答を返すとメモリ圧迫の可能性 | 受容可（非ブロック）。緩和＝信頼済 allowlist（`fal.media`/`replicate.delivery`）＋linked-CTS(300s) タイムアウト＋Workers 2 で並行有界＋video 既定オフ。将来 `MaxResponseContentBufferSize` かサイズキャップ付きストリーミング読取りを推奨 |
| 2 | Info | `Ai/FalQueue.cs:44` | 初回 poll に 1s 固定遅延（機能面の軽微事項。セキュリティ影響なし） | 対応不要 |
| 3 | Info | テスト fixture のダミー token（`hf_*`） | テスト用の明白なフェイク値 | 対応不要（許容） |

## 判定理由
Critical/High/Medium は 0。脆弱パッケージ 0。トークン漏洩ガード（`ToRouterUrl` fail-closed・HF token は
router のみ・最終取得は no-auth）と SSRF 統制（https/allowlist ラベル境界/空=全拒否/`AllowAutoRedirect=false`）
は image-edit と共通コード（`FalQueue`/`MediaRefetch`）で、記録31・34 で実証済みの防御を video 経路でも
そのまま引き継ぐ。署名検証は非接触・回帰なし。ログ・例外にトークン非混入。残 Low#1 は信頼済 allowlist＋
タイムアウト＋既定オフで実効抑制されており緩和策が明確なため PASS を妨げない（記録・将来対応として残置）。
差し戻すべき Critical/High/Medium なし。

## 次ゲート
ドキュメントレビュー（doc-review-gate）へ。
