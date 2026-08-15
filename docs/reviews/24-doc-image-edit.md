# ドキュメントレビュー — 画像編集 image-to-image・Qwen-Image-Edit (spec03 3b) (2026-08-15)
Verdict: PASS
委譲分析: なし（自前）

## 整合チェックリスト
- [x] 設定整合: ImageEditModel/ImageEditEndpoint/ImageEditTimeoutSeconds が BotOptions.cs の既定値と .env.example/README(EN/JA)/CLAUDE.md で一致
- [x] 既定値の正: Qwen/Qwen-Image-Edit / https://router.huggingface.co/hf-inference/models/{model} / 120 — 全ドキュメント一致
- [x] CLAUDE.md アーキ QuickReply が [🔄 再生成][✏️ 編集][💬 チャットへ] に更新。QuickReplyFactory.ImageResult と一致
- [x] CLAUDE.md の「✏️編集=次の非コマンドテキスト・image-to-image(Qwen-Image-Edit)・AwaitingEdit・モード切替/コマンドでキャンセル」が実装と一致。旧「3bで追加」未実装表記は解消
- [x] UserMessages help(en/ja) が 🔄/✏️ に言及。EditPrompt/EditingImage/EditNoImage/EditImageExpired が en/ja 揃う
- [x] QuickReplyFactory ラベル(LabelRegenerate/Edit/BackToChat)が en/ja 揃い、postback data(action=regen|edit|mode&value=chat)整合
- [x] spec03 status を「3b 実装済み」に更新。payload 確定 {inputs=base64, parameters.prompt} を明記。§2.3「img2img payload は 3b で新規定義」が ImageEditService.cs と一致
- [x] エンドポイント /webhook /media/{id} /health 実装と一致（Program.cs）
- [x] コマンド整合: dotnet test / docker compose 等 CLAUDE.md と一致。テスト 45件緑（既存36＋新規9、記録22で実証）
- [x] 秘密情報: .env.example はプレースホルダのみ、実トークンなし
- [x] README(EN/JA) Features 節の画像結果ボタン記述を [🔄][✏️][💬] に更新（Major #1、本レビューで反映済み）

## 指摘
| # | 重大度 | 箇所 | 問題(実態との差異) | 必要な対応 |
| --- | --- | --- | --- | --- |
| 1 | Major | README.md:14 / README.ja.md:14 | Features「画像結果には 🔄 再生成 ／ 💬 チャットへ」が 3b の ✏️編集 実装後も未更新で、実挙動 [🔄][✏️][💬] と齟齬 | EN/JA とも ✏️ 編集（image-to-image）を追記（**本レビューで反映済み**） |

## 判定理由
新3設定キー（ImageEditModel=Qwen/Qwen-Image-Edit、ImageEditEndpoint=…/models/{model}、ImageEditTimeoutSeconds=120）は
BotOptions.cs の既定値と .env.example/README(EN/JA)/CLAUDE.md/spec03§3 で過不足なく一致。CLAUDE.md アーキ記述・
spec§2.3 の img2img payload（base64 inputs + parameters.prompt）は ImageEditService.cs 実装と一致し、旧「3bで追加」
未実装表記も解消済み。help/QuickReply ラベルは en/ja 揃い。エンドポイント・コマンド（dotnet test=45件緑）・秘密情報も
整合。唯一の Major（README Features が新機能=✏️編集 を未反映）は本レビューで EN/JA とも [🔄][✏️][💬] に更新済み。
差し戻すべき項目なしで PASS。spec03 の 3b は 4ゲート（実装=22 / セキュリティ=23 / ドキュメント=24）すべて PASS。
