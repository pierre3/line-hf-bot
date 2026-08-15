# 31 — セキュリティレビュー: 画像編集 fal-ai プロバイダ経由 (spec 05)

- 日付: 2026-08-16
- 対象: ブランチ `fix/image-edit-fal-provider` / `git diff 96da177..HEAD`（実装 047a8a9 + dev nit 16240ce）
- 前段ゲート: 実装 = `docs/reviews/30-impl-image-edit-fal.md`（PASS）
- 委譲分析: dotnet-claude-kit:security-scan（6層）＋ 手動重点確認
- 判定: **PASS**

## スコープ
画像編集(image-to-image)を fal-ai プロバイダの非同期キュー経由に変更。
`LineHfBot/Ai/ImageEditService.cs` 全面書換、`BotOptions.cs`/`.env.example` の既定値更新、
dev 専用 `/dev/imageedit` 追加。新規 NuGet なし。

## レイヤ結果
| 層 | 結果 | 備考 |
|---|---|---|
| 1 脆弱パッケージ | PASS | `dotnet list package --vulnerable --include-transitive` 0件 |
| 2 シークレット | PASS | `.env.example` 空プレースホルダのみ／トークン非ログ |
| 3 OWASP パターン | PASS | SSRF は allowlist+AutoRedirect無効で防御 |
| 4 認証/エンドポイント | PASS | `/dev/imageedit` は IsDevelopment ガード内。署名経路不変 |
| 5 CORS | PASS | 変更なし |
| 6 データ保護 | PASS | 例外本文にトークン非混入（500字トランケート） |

## 重点確認
- トークン漏洩: `ToRouterUrl` が `https://queue.fal.run/` 始まりのみ受理（fail-closed）。
  トークンは router.huggingface.co にのみ送出。テストで evil ホスト/http/サブドメイン偽装の拒否を検証。
- SSRF: 最終 URL は `MediaRefetch`（https/allowlist/no-auth）＋ typed client `AllowAutoRedirect=false`。
  非 allowlist ホストが例外になることをテスト済。入力は自前 dataURI 化で外部 fetch なし。
- Webhook 署名: 非接触・回帰なし。ChannelSecret/Token は ValidateOnStart 継続。
- DoS: poll は linked CTS + CancelAfter(既定120s)、1s 間隔、未知 status で早期例外。dispose 適切。
- dev: `/dev/imageedit` は本番非公開。

## 指摘
- [Low/Info] 応答の `ReadAsByteArrayAsync` に上限なし（信頼済ホスト・timeout で緩和。画像/動画サービスと共通の既存挙動。将来 MaxResponseContentBufferSize 検討）。
- [Info] テスト fixture `hf_test_token`（許容）。

## 差し戻し項目
なし（Critical/High/Medium 0、脆弱パッケージ 0）。

## 次ゲート
ドキュメントレビュー（doc-review-gate）へ。
