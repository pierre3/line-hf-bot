# レビュー記録 50 — Deploy to Azure ボタン ドキュメントゲート

- 日付: 2026-08-21
- ゲート: ドキュメント（4段階中4）
- 対象: README.md / README.ja.md（Deploy ボタン）、docs/deploy/azure-container-apps.md / .ja.md（One-click セクション追加・手動手順を Manual サブセクション化）、infra/ テンプレート群
- 前提: セキュリティゲート PASS（記録49）
- 判定: **PASS**

## 整合チェック
- 設定整合: Bicep/ARM のシークレット env が実コード（`BotOptions.cs` の `LineOptions`/`HuggingFaceOptions`）と一致＝`Line__ChannelSecret` / `Line__ChannelAccessToken` / `HuggingFace__ApiKey`。存在しない `FalAi__ApiKey` / `Google__ApiKey` / `OpenAi__ApiKey` は**混入なし**（fal-ai は HF router 経由で独立キーを持たない）。その他 env（PublicBaseUrl/Locale/VideoEnabled/VisionEnabled）と既定値（locale=en / videoEnabled=false / visionEnabled=true）も一致。
- 単一インスタンス要件（min=max=1）と常時課金を Bicep コメント・ARM・createUiDefinition・両 README・両 ACA doc すべてで明記。
- EN/JA 整合: 両 ACA doc は構成・手順が対応。両 README のボタン行も対応。
- ボタン URL: README.md と README.ja.md で完全一致。`raw.githubusercontent.com/pierre3/line-hf-bot/main/infra/{azuredeploy,createUiDefinition}.json` を URL エンコードで参照、`createUIDefinitionUri` パラメータ名・大文字小文字も正しい。
- 内部アンカー: EN の `#5-point-line-at-the-webhook`、JA の `#5-line-の-webhook-を向ける` ともに実在見出しへ解決。見出しレベルは h1→h2→h3 で一貫。
- エンドポイント整合: `/webhook`・`/media/{id}`・`/health`→`{"status":"ok"}` が実装と一致。秘密情報はプレースホルダのみ。

## 指摘
- doc gate の当初指摘「README の ACA リンクがバックスラッシュ区切り」は**誤検知**（実ファイルはフォワードスラッシュ）で修正不要と確認。
- JA One-click セクションの「LINE の Webhook を向ける」導線を EN 版と揃えてアンカーリンク化（対応済み）。

## 未実施（環境制約）
- ライブ ARM 検証（`az deployment group validate` / `what-if`）は本環境の社内プロキシ（MITM 証明書）が Azure 認証の SSL 検証を弾くため実行不可。オフライン検証（`az bicep build`/`lint` クリーン）は完了。リリース前に、プロキシ外またはコーポレート CA を信頼した環境で validate/what-if の実施を推奨。

## 結論
Blocker ゼロ。4 ゲート完了。
