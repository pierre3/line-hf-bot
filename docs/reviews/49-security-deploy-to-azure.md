# レビュー記録 49 — Deploy to Azure ボタン（infra/ IaC）セキュリティゲート

- 日付: 2026-08-21
- ゲート: セキュリティ（4段階中3）
- 対象: `infra/main.bicep` / `infra/azuredeploy.json` / `infra/createUiDefinition.json` と README・ACA ドキュメントの Deploy ボタン追加
- 前提: C# 変更なし（IaC + ドキュメントのみ）。`az bicep build` / `az bicep lint` クリーン（0 warning）
- 委譲: dotnet-claude-kit:security-scan は .NET コード専用のため IaC(Bicep/ARM/portal JSON) に非適用 → 手動 IaC 秘密情報レビュー + 秘密情報コミット sweep にフォールバック
- 判定: **PASS**

## 重点確認
1. **秘密情報の宣言と注入**: 3 シークレット（`lineChannelSecret` / `lineChannelAccessToken` / `huggingFaceApiKey`）は Bicep `@secure()` / ARM `securestring`。Container App `secrets` 配列に格納し env へは **`secretRef` のみ**で注入。平文 env `value` は非秘密（PublicBaseUrl/Locale/VideoEnabled/VisionEnabled）のみ。
2. **outputs への露出なし**: テンプレート outputs は `appUrl` / `lineWebhookUrl` のみで秘密を含まない。createUiDefinition の outputs は secure 値を securestring パラメータへ受け渡すだけ。
3. **マスク入力**: 3 シークレットすべて `Microsoft.Common.PasswordBox`（`hideConfirmation:true`）。
4. **Ingress**: `external: true` は LINE Webhook 受信に必須で意図的。targetPort 8080 のみ公開。単一レプリカ（min=max=1）はメモリ状態設計上の要件。
5. **ハードコード資格情報なし**: `hf_*` / `Bearer` / JWT / hex トークンの sweep でヒットはパラメータ名/参照のみ。ドキュメントはプレースホルダのみ。
6. **サニティ**: image 既定は公開 Docker Hub `docker.io/pierre3/line-hf-bot:latest`。Log Analytics の `listKeys().primarySharedKey` は ACA managed env ログ設定の標準手法。Deploy ボタン URL は正規 `portal.azure.com/#create/Microsoft.Template` 経由で raw GitHub の template/createUiDefinition を参照。

## 指摘（Informational・ブロックせず／対応済み）
- `createUiDefinition.json` appName の validationMessage「2-32 chars」と regex `^[a-z][a-z0-9-]{1,30}[a-z0-9]$`（実質 3-32）の不一致 → regex を `{0,30}` に修正し 2-32 に整合済み。

## 結論
Critical/High/Medium なし。差し戻し事項なし。ドキュメントレビュー（ゲート4）へ進行可。
