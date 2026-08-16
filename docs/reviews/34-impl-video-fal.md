# 34 実装レビュー — 動画 fal-ai プロバイダ経由（spec 06）

- 日付: 2026-08-16
- 対象: spec 06（`docs/specs/06-video-fal-provider.md`）
- ゲート: 実装（impl-review-gate）
- 委譲分析: dotnet-claude-kit:code-review（Roslyn MCP）
- 判定: **PASS**

## 対象変更
- `LineHfBot/Ai/FalQueue.cs`（新規・internal static）: fal 非同期キューの共通ヘルパー（`SubmitAsync`／`PollUntilCompletedAsync`／`GetResultAsync`／`ToRouterUrl`）。spec05 の `HuggingFaceImageEditService` private 実装から抽出。
- `LineHfBot/Ai/VideoService.cs`: hf-inference 同期 → fal キュー方式へ全面書換。submit `{prompt}`→poll→`video.url` 抽出（`Object` 判定つき fail-closed）→`MediaRefetch`。`IVideoService` 契約不変。
- `LineHfBot/Ai/ImageEditService.cs`: 共通 `FalQueue` 利用へリファクタ（挙動不変。`images[0].url` 抽出は service に残置）。
- `LineHfBot/Configuration/BotOptions.cs`: `VideoModel`＝`fal-ai/wan/v2.2-5b/text-to-video`、`VideoEndpoint`＝`https://router.huggingface.co/fal-ai/{model}?_subdomain=queue`。XML doc 更新。
- `LineHfBot/appsettings.json` / `.env.example`: 同既定へ。`App__VideoEnabled` は既定 false 維持（fal 有料・opt-in）。
- テスト: 新規 `VideoServiceTests`（5件）。`ImageEditServiceTests` の `ToRouterUrl` 参照を `FalQueue.ToRouterUrl` へ。`MediaServiceTests` の旧同期 video テスト除去。

## 委譲分析の結果
- detect_antipatterns（LineHfBot）: 17件検出、うち spec06 変更ファイル（FalQueue/VideoService/ImageEditService/BotOptions）は **0件**。残りはすべて既存（Program.cs・GenerationWorker・WorkProcessor・LineMessenger・RichMenuManager・HfHttp）で本変更と無関係。
- get_diagnostics（LineHfBot project, all）: Errors 0 / Warnings 0 / Info 0。
- テスト裏取り: `dotnet test` = 64 件全緑（60→ video +5・旧同期 video −1）。build 0-0。

## 受入基準（§5）照合
| # | 基準 | 結果 |
|---|---|---|
| 1 | submit が `{prompt}` を fal エンドポイントへ Bearer 付き POST | PASS（VideoService:33／test Submit_posts_prompt_with_auth、URL 完全一致検証） |
| 2 | status/response URL を router 書換、HF トークンは router 以外へ送らない | PASS（FalQueue.ToRouterUrl＋GetJsonAsync が書換後 URL にのみ Bearer／test Polls_via_router...） |
| 3 | COMPLETED まで poll→`video.url` 取得 | PASS（PollUntilCompletedAsync／ExtractVideoUrl） |
| 4 | fal.media を allowlist 経由・Authorization 無しで再取得、`video/mp4` | PASS（MediaRefetch／test refetch without authorization） |
| 5 | allowlist 外の結果 URL を拒否 | PASS（test Result_url_on_disallowed_host_is_rejected） |
| 6 | queue.fal.run 以外の status/response URL を拒否 | PASS（ToRouterUrl が fail-closed で例外／共有テスト） |
| 7 | VideoTimeoutSeconds で打ち切り | PASS（linked CTS＋CancelAfter、既存 OCE 経路） |
| 8 | 既定値がドキュメント・appsettings と一致 | PASS（BotOptions＝appsettings.json＝.env.example／test Video_defaults_match_docs） |
| 9 | `FalQueue.ToRouterUrl` は queue.fal.run 始まりのみ書換・他ホスト/http 拒否 | PASS（ImageEditServiceTests が FalQueue.ToRouterUrl を参照・回帰なし） |
| 10 | 既存 ImageEditServiceTests 緑・旧同期 video テストは fal 方式へ置換 | PASS（64件緑） |

## 指摘
| # | 重大度 | 箇所 | 問題 | 必要な対応 |
|---|---|---|---|---|
| 1 | Minor | FalQueue.cs:44 | 初回 poll 前に固定 1s Task.Delay（結果が即 COMPLETED でも 1 周期待つ） | 現状維持で許容（fal は非同期キューで即完了は稀）。対応不要 |
| 2 | Minor | FalQueue.cs:82 | `ToRouterUrl` の `StartsWith(Ordinal)` は大小文字区別 | fail-closed（安全側）で意図的。対応不要 |
| 3 | Minor | MediaRefetch.cs:40 | 応答 `ReadAsByteArrayAsync` に明示的サイズ上限なし | 信頼済み allowlist ホスト＋linked CTS timeout で実効抑制。既存共通挙動、image-edit と同条件。security ゲートで再確認推奨 |

- Blocker / Major: なし。

## 判定理由
共通化リファクタは image-edit の payload・URL 抽出・トークン漏洩ガードを不変に保ち（`ToRouterUrl` は共有テストで回帰なし）、video は `video.url` 抽出のみタスク固有として service 側に正しく分離。重点確認項目をすべて確認:
- トークン漏洩ガード＝`ToRouterUrl` が `https://queue.fal.run/` 始まりのみ受理し router へ書換、HF トークンは `router.huggingface.co` にのみ送出。想定外ホストは fail-closed で例外。
- SSRF＝最終 `video.url` は `MediaRefetch`（https 限定・allowlist ラベル境界一致・空=全拒否・no-auth・timeout）＋ typed client `AllowAutoRedirect=false`（Program.cs:57-59）でリダイレクト迂回封じ。allowlist 外拒否をテスト実証。
- poll 終了条件＝COMPLETED で return、IN_QUEUE/IN_PROGRESS 継続、他終端は例外。全体は VideoTimeoutSeconds(既定300) linked CTS で打ち切り。
- fail-closed JSON パース＝`video` の `Object` 判定・`url` の非空判定を満たさなければ例外（test Result_without_video_url_throws）。`GetString` 欠如時の空文字も ToRouterUrl で例外化。
- dispose＝HttpRequestMessage/HttpResponseMessage/JsonDocument/CTS すべて using。
diagnostics 0/0/0・テスト 64 件緑・変更ファイルの antipattern 0 も裏取り。差し戻し対象なし。

## 次ゲート
- security-review-gate（SSRF／トークン漏洩の重点再確認。指摘#3 の再取得サイズ上限も併せて評価推奨）。
