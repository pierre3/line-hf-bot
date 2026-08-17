# ドキュメントレビュー — spec07 画像→チャット (vision/VQA) (2026-08-17)
Verdict: PASS
委譲分析: なし（自前）

## 整合チェックリスト
- [x] 設定整合(既定値): `VisionModel=Qwen/Qwen2.5-VL-7B-Instruct` / `VisionEndpoint=https://router.huggingface.co/v1/chat/completions` / `VisionTimeoutSeconds=60` / `VisionEnabled=true` が BotOptions.cs・appsettings.json・.env.example・README(EN/JA)・CLAUDE.md で完全一致
- [x] 必要キーの記載漏れなし / 存在しないキーの記載なし（Vision 3キー + App__VisionEnabled）
- [x] 既定 `VisionEnabled=true` により受信写真 UX が spec04 から変わる点が明記（CLAUDE.md 設定節、README/JA Features・Configuration 表）
- [x] provider 依存で利用不可時は「質問」ボタンが汎用 Error になる点を明記（README EN/JA・CLAUDE.md）
- [x] fal ではなくチャットと同じ HF Inference クレジットである点を明記（README/JA・CLAUDE.md・.env.example）
- [x] エンドポイント整合: `/webhook` `/media/{id}` `/health` が Program.cs と一致（Vision は新規 HTTP エンドポイントを追加しない）
- [x] コメント/XMLドキュメント整合: VisionService.cs（/v1/chat/completions 直叩き・base64 data URI・ChatService 準拠のエラー契約）、UserStateStore.cs（PendingAction enum・SetReceivedImage の VisionEnabled 分岐）が CLAUDE.md と一致
- [x] DI 登録: `AddHttpClient<IVisionService, HuggingFaceVisionService>`(Program.cs) 実在
- [x] 言語ルール: コメント/ログは英語、公開ドキュメント EN+JA 両方更新、UserMessages.cs に en/ja 集約、JA は自然
- [x] 秘密情報の非混入: .env.example・README ともプレースホルダのみ
- [x] specs 一覧に「07 画像 Q&A(vision/VQA)」を EN/JA 追記
- [x] tech stack に vision 追記（README「image / video / vision」/ CLAUDE.md）

## 指摘
| # | 重大度 | 箇所 | 問題 | 対応 |
| --- | --- | --- | --- | --- |
| 1 | Minor | README.md L110 / README.ja.md L110（Commands 表「a photo」行） | 既定 `VisionEnabled=true` では写真受信時に「✏️編集 / 💬質問」を提示するが、Commands 表が spec04 時代の「どう編集しますか？」のまま。同 README 内の Features 節・Configuration 表と食い違い | **解消**（本レビューで Commands 表を既定 vision オン挙動へ更新、EN/JA 両方） |

- Blocker / Major: なし。

## 判定理由
spec07 の追加設定（`HuggingFace__VisionModel`/`VisionEndpoint`/`VisionTimeoutSeconds`、`App__VisionEnabled`）が実コード既定値・appsettings.json・.env.example・README(EN/JA)・CLAUDE.md の全てで過不足なく一致。重点確認 (1)既定値一致 (2)既定 true の UX 変更明記 (3)provider 依存で汎用 Error (4)fal 非依存=チャット同等クレジット (5)言語ルール (6)秘密情報非混入 をいずれも充足。エンドポイント・XMLドキュメント・コメント・DI 登録も実装と齟齬なし。唯一の Minor#1（Commands 表の内部矛盾）は本レビューで修正済み。

spec07 は4ゲート全 PASS（37 仕様 / 38 実装 / 39 セキュリティ / 40 ドキュメント）。

## 追記（2026-08-17・実機検証後の既定変更）
本ゲート時点の既定値（`VisionModel=Qwen/Qwen2.5-VL-7B-Instruct`・`VisionTimeoutSeconds=60`）は、実機 LINE テストで判明した provider 都合により変更した（コード/appsettings/.env.example/README(EN/JA)/CLAUDE.md/spec07§6・§6.1・テストを一括更新済み・整合維持）:
- `Qwen2.5-VL-7B` は HF 上 **Featherless のみ配信**で、auto ルーティングだと `400 model_not_supported`、pin しても `503 capacity_exhausted`/コールドで慢性的に不安定だった。
- → 既定を **`Qwen/Qwen2.5-VL-72B-Instruct:ovhcloud`**（provider pin 付き・実機で ~10 秒応答・日本語に強い・非 gated）に変更、`VisionTimeoutSeconds` を **60→120**（VL コールドスタート耐性）に。
- README(EN/JA)・spec07§6.1 に vision トラブルシュート（pin 必須／provider 有効化／capacity 時は別 provider へ・動作確認済み代替）を追記。`503` 自動リトライは初版見送り（慢性キャパには効かず別 provider 切替が確実）。
