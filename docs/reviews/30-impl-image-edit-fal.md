# 30 実装レビュー — 画像編集 fal-ai プロバイダ対応（spec 05）

- 日付: 2026-08-16
- 対象: spec 05（`docs/specs/05-image-edit-fal-provider.md`）／ブランチ `fix/image-edit-fal-provider` / コミット `047a8a9`（前 main `96da177`）
- 差分範囲: `git diff 96da177..HEAD`
- ゲート: 実装（impl-review-gate）
- 委譲分析: dotnet-claude-kit:code-review（Roslyn MCP）
- 判定: **PASS**

## 対象変更
- `LineHfBot/Ai/ImageEditService.cs`: fal 非同期キュー方式へ全面書き換え（submit→poll→result→refetch）。契約 `IImageEditService.GenerateAsync(byte[],string,ct)` 不変。
- `LineHfBot/Configuration/BotOptions.cs`: 既定 `ImageEditModel=fal-ai/qwen-image-edit`、`ImageEditEndpoint=https://router.huggingface.co/fal-ai/{model}?_subdomain=queue`。
- `LineHfBot/Program.cs`: dev 専用 `/dev/imageedit`（生成→編集の E2E プローブ）。
- `LineHfBot.Tests/ImageEditServiceTests.cs`: fal 方式へ全面書き換え。
- ドキュメント（`.env.example` / README EN・JA / CLAUDE.md）反映。

## 委譲分析の結果
- detect_antipatterns（ImageEditService.cs）: 0件
- get_diagnostics（LineHfBot project, all）: Errors 0 / Warnings 0 / Info 0
- テスト: 60件緑 / build 0-0 / 実機 E2E 成功（有効 PNG 1.45MB）

## 受入基準（§5）照合
| # | 基準 | 結果 |
|---|---|---|
| 1 | submit が `{prompt,image_url(dataURI),image_urls}` を Bearer 付き POST | PASS（test: Submit_posts_...） |
| 2 | status/response URL を router 書き換え、HF トークンは router 以外へ送らない | PASS（ToRouterUrl＋test） |
| 3 | COMPLETED まで poll→`images[0].url` 取得 | PASS |
| 4 | fal.media を allowlist 経由・Authorization 無しで再取得 | PASS（MediaRefetch） |
| 5 | allowlist 外の結果 URL を拒否 | PASS（test: disallowed host） |
| 6 | queue.fal.run 以外の status/response URL を拒否 | PASS（test: 非 queue / サブドメイン偽装 / http） |
| 7 | ImageEditTimeoutSeconds で打ち切り | PASS（linked CTS） |
| 8 | 既定値がドキュメントと一致 | PASS（test: defaults_match_docs） |
| 9 | 既存テスト回帰なし | PASS（60件緑） |

## 指摘
- Minor: 初回 poll 前の固定 1s 遅延（許容・現状維持）。
- Minor: `/dev/imageedit` の `prompt ?? ...` は必須クエリ束縛によりデッド（dev 専用・影響なし）→ 本レビュー後に `string? prompt` へ修正済み。
- Minor: `ToRouterUrl` の `StartsWith(Ordinal)` は大小文字区別（fail-closed で安全側、対応不要）。
- Blocker / Major: なし。

## 判定理由
トークン漏洩ガード（末尾スラッシュ込み前置一致で host 偽装を封じ、書き換え後は権威部固定）、poll 終了条件とタイムアウト linked CTS、JSON の fail-closed パース、リソース dispose、typed HttpClient＋AllowAutoRedirect=false による SSRF 強化、規約整合をいずれも確認。差し戻し対象なし。

## 次ゲート
- security-review-gate（Webhook 署名は本変更対象外だが、SSRF/トークン漏洩の重点再確認を推奨）。
