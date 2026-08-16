# 仕様レビュー — text-to-video を fal-ai プロバイダ経由に対応（動画プロバイダ統合 / spec06） (2026-08-16)
Verdict: PASS
委譲分析: なし（自前）
対象: `docs/specs/06-video-fal-provider.md`

## 受入基準チェックリスト
- [x] 受入基準が明確・検証可能（§5 AC 1-10。submit本文・router書換・poll・SSRF再取得・allowlist拒否・トークン漏洩拒否・タイムアウト・既定値一致・`ToRouterUrl`回帰・既存テスト回帰まで具体的）
- [x] 未解決点ゼロ（§6 全項目 [x]。TBD/「たぶん」なし。スコープ外＝image-to-video/replicate・wavespeed/生成パラメータ/`VideoEnabled`既定変更を明示）
- [x] テスト可能性（各ACがユニットテストで検証可能。happy path の `video.url` 抽出も AC#3/#4 でカバー。実 fal E2E は課金回避のため operator 委任＝ワイヤ形式は huggingface_hub 実装で確定済みのため妥当）
- [x] スコープ整合（テキスト/画像/動画・非同期処理・メディア配信・設定外部化が CLAUDE.md/コードと一貫。`IVideoService`契約不変→`WorkProcessor`/配線無改修が実コードと一致）
- [x] 非機能（秘密情報＝HFトークンは router のみ／失敗通知＝`HfHttp.EnsureSuccessAsync`＋`WorkProcessor`汎用エラー／SSRF＝`MediaRefetch` allowlist＋`AllowAutoRedirect=false`／`PublicBaseUrl`＝`PrepareMediaAsync`必須 を明記）

## 既存コードとの整合検証（裏取り済み）
- `HuggingFaceVideoService`（`Ai/VideoService.cs`）は現状 `{inputs}` 同期＋`MediaResponse.ReadAsync`。fal `{prompt}`→poll→`video.url`→`MediaRefetch` への書換は妥当。ワイヤ形式は huggingface_hub `FalAITextToVideoTask`（body `{"prompt": inputs}`／result `output["video"]["url"]`）と一致。
- 二重 `fal-ai/` プレフィックス（`.../fal-ai/fal-ai/wan/...`）は spec05 の稼働実装（`ImageEditEndpoint`＝`.../fal-ai/{model}` × Model=`fal-ai/qwen-image-edit`）と同型。AC#1 が結果 URL を明記しており曖昧さなし。
- `ToRouterUrl`／submit・poll の共通化元（`ImageEditService.cs` private 実装）と抽出先 `Ai/FalQueue.cs` の責務分割（URL 抽出は各サービス）が合理的。`images[0].url` と `video.url` の相違点を正しく特定。
- `MediaResponse` は `ImageService.cs` でも使用中 → video からの除去で orphan 化しない（確認済み）。
- `IVideoService` typed client の `AllowAutoRedirect=false`（`Program.cs:57-59`）と `/dev/video`（`Program.cs:187`）の「無改修」記述はコードと一致。

## 指摘
| # | 重大度 | 箇所 | 問題 | 必要な対応 |
|---|---|---|---|---|
| 1 | Minor | §4 反映リスト | 反映先に `BotOptions.cs` が明記されていない。`HuggingFaceOptions.VideoEndpoint` の**コード既定**は現状 hf-inference テンプレ、`VideoModel` の既定は空文字（`Wan-AI/...` は appsettings.json 由来）。appsettings.json は上書きするため実行時は動くが、コード既定が陳腐化し spec05 の整合基準（記録32 は BotOptions.cs 一致を確認）から乖離する | §4 の「既定」表に合わせ `BotOptions.cs` の `VideoModel`/`VideoEndpoint` 既定も更新対象に明記（反映リストへ `BotOptions.cs` を追加） |
| 2 | Minor | §5 / §6 | 実 fal E2E を operator 委任とする方針は妥当だが、happy path（`video.url`→`video/mp4` 返却）のユニット検証が AC に含まれる旨をテスト観点として明示すると引き継ぎが明確になる | 次ゲート引き継ぎとして「canned fal 応答での `video.url` 抽出＋`MediaRefetch` 経路のユニットテスト」を実装ゲートで確認（仕様変更不要） |

## 判定理由
Blocker 0・Major 0。受入基準（AC 1-10）は検証可能で、fal 非同期キュー・router 書換によるトークン漏洩防止・`MediaRefetch` による SSRF・タイムアウト・既定値整合まで具体的。ワイヤ形式は huggingface_hub `FalAITextToVideoTask` 実装で確定済み（推測でない）。`IVideoService` 契約不変により `WorkProcessor`/配線無改修という主張、`AllowAutoRedirect=false`、`MediaResponse` の非 orphan 化、`/dev/video` 無改修はいずれも実コードで裏取り済み。スコープはユーザー要求・CLAUDE.md（動画は provider 統合が必要・既定オフ）と一貫。残 Minor 2 件は既定値の反映先明記とテスト観点の引き継ぎで、実装フェーズ内で吸収可能。実装フェーズへ進める品質。
