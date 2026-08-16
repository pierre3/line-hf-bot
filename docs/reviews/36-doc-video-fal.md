# ドキュメントレビュー — 動画 text-to-video を fal-ai プロバイダ経由に対応 (spec 06) (2026-08-16)
Verdict: PASS
委譲分析: なし（自前）

## 整合チェックリスト
- [x] 設定整合: `VideoModel=fal-ai/wan/v2.2-5b/text-to-video` が `BotOptions.cs` / `appsettings.json` / `.env.example` / README EN / README JA / CLAUDE.md / spec06 §4 で一致
- [x] `VideoEndpoint=https://router.huggingface.co/fal-ai/{model}?_subdomain=queue` が同上すべてで一致
- [x] `VideoTimeoutSeconds=300` が `BotOptions.cs` / `appsettings.json` / `.env.example` / CLAUDE.md で一致
- [x] `App__VideoEnabled` 既定 false・opt-in・fal 有料 の記述が README EN / JA / `.env.example` / CLAUDE.md / spec06 §4 で一貫
- [x] 実装一致: `VideoService` が fal キュー(submit `{prompt}`→poll→`video.url`→`MediaRefetch`)、共通 `FalQueue`、`video.url` 抽出、トークン router のみ(`ToRouterUrl` は `https://queue.fal.run/` 始まりのみ受理) を確認（`VideoService.cs`, `FalQueue.cs`）
- [x] hf-inference は text-to-video 非対応の記述が正しい（CLAUDE.md, README, `.env.example`, `BotOptions.cs` XMLドキュメント）
- [x] エンドポイント `/webhook` `/media/{id}` `/health` が `Program.cs` と一致。`/dev/video` は Development 限定
- [x] コマンド（`docker compose up --build` / `devtunnel host -p 8080` / `dotnet run`）が実構成と一致
- [x] README 二言語対応（specs 一覧 01-06 を EN/JA 双方に追加）・言語ルール順守（公開ドキュメント EN+JA、コメント/XMLドキュメント英語、日本語は自然）
- [x] 秘密情報なし（`.env.example` は空プレースホルダのみ、実トークンなし）
- [x] コメント/XMLドキュメントが実挙動と矛盾しない（Minor #1 を本レビューで反映済み）

## 指摘
| # | 重大度 | 箇所 | 問題(実態との差異) | 必要な対応 |
| --- | --- | --- | --- | --- |
| 1 | Minor | `LineHfBot/Configuration/BotOptions.cs` `AppOptions.VideoEnabled` XMLドキュメント | 「text-to-video needs a provider-specific integration … until a provider is wired」＝プロバイダ未接続 scaffold を前提とした旧記述。spec06 で fal-ai 接続済みのため実態と矛盾（既定オフの理由は「fal が有料かつ遅い＝opt-in」に変化）。他ドキュメントは新理由に更新済みで、この1箇所だけ取り残し | XMLドキュメントを実態（fal 接続済み・opt-in 理由＝有料/遅い）へ更新 → **本レビューで反映済み** |

## 判定理由
既定値（`VideoModel` / `VideoEndpoint` / `VideoTimeoutSeconds` / `VideoEnabled`）は `BotOptions.cs` を起点に `appsettings.json`・`.env.example`・README(EN/JA)・CLAUDE.md・spec06 §4 まで過不足なく一致。機能記述（fal 非同期キュー・共通 `FalQueue`・`video.url` 抽出・SSRF/`MediaRefetch`・トークン router のみ・hf-inference 非対応）は実コード（`VideoService.cs` / `FalQueue.cs`）およびエンドポイント（`Program.cs`）と一致。README は EN/JA 二言語で対応し言語ルールを順守、`.env.example` に実トークンの混入なし。Blocker/Major はゼロ。唯一の Minor（開発者向け XMLドキュメント1行の取り残し）は本レビューで反映済みのため PASS。
