# 仕様レビュー — 画像→チャット vision/VQA (spec07) (2026-08-17)
Verdict: FAIL（初回）→ PASS（再判定 2026-08-17）
委譲分析: なし（自前）

## 受入基準チェックリスト（初回）
- [x] AC1 リクエスト形状（model/messages/text+image_url、Bearer）— ワイヤ形式明確・テスト可能
- [x] AC2 data URI MIME（保存 content-type、既定 image/png）— 明確
- [ ] AC3 空/欠落→EmptyAnswer「ワーカーで判定」— §2.4「既存チャットと同じ扱い」（＝サービスで判定）と矛盾（Blocker #1）
- [ ] AC5 タイムアウト→Timeout — §5 が引く先行例（ImageEditService）は OCE を投げるだけで、WorkProcessor 最上位 catch は OCE を除外。到達不能の恐れ（Blocker #1）
- [x] AC4 非2xx→本文つき例外→Error — 明確
- [x] AC6 VisionEnabled=true 受信フロー（Pending=None＋choices）— 明確
- [x] AC7 VisionEnabled=false フォールバック（spec04 挙動）— 意図は明確だが AC13 と整合しない（Major #1）
- [x] AC8 action=ask postback — 明確
- [x] AC9 Pending=VisionQuestion→WorkKind.Vision／キャンセル規約 — 明確
- [x] AC10 HandleVisionAsync（TTL 失効→VisionImageExpired、ack→push）— 明確
- [x] AC11 WebhookEventId 冪等 — 明確
- [x] AC12 既定値のドキュメント一致 — 実装項目として明確
- [ ] AC13 spec04 既存テストが「enum 化後も緑」— 既定 VisionEnabled=true による受信 UX 変更を考慮しておらず不正確（Major #1）

## 指摘
| # | 重大度 | 箇所 | 問題 | 必要な対応 | 反映 |
|---|---|---|---|---|---|
| 1 | Blocker | §2.4 / §5 / §10 / AC3 / AC5 | エラー処理の参照テンプレートが二重で矛盾。§2.4「既存チャットと同じ扱い」（ChatService は OCE を内部捕捉し Timeout、空も EmptyAnswer を文字列で返す）に対し、§5/§10 が引く ImageEditService/VideoService は OCE を送出。`WorkProcessor.ProcessAsync` の最上位 catch は `when (ex is not OperationCanceledException)` で OCE を除外するため、§5 の雛形どおりだと Vision タイムアウト時にユーザー無応答となり AC5 破綻（record 26 で spec04 実装時に踏んだ既知の罠の再来）。AC3 の空判定箇所も未確定。 | `IVisionService.AnswerAsync` の契約を ChatService に一本化＝**サービス側で OCE→Timeout・空→EmptyAnswer を文字列で返す**。AC3/AC5/§2.4/§5/§10 を整合。 | 済（§2.2-2.4 / §3.3 / §5 / AC3/AC4/AC5 / §9 決定に「ChatService 準拠・雛形は ChatService.cs」を明記。AnswerAsync 戻り＝表示可能文字列、OCE/空はサービス変換、非2xx のみ送出→最上位 catch→Error） |
| 2 | Major | AC13 / §3.1 / §6（VisionEnabled 既定 true） | 既定 VisionEnabled=true で写真受信の既定挙動が spec04「即・編集アーム＋ImageReceived」から「Pending=None＋choices」へ変わる。spec04 受信フロー既存テストは既定のままでは緑にならず、AC13 の「enum 化のみで緑」は不正確。 | AC13 を訂正＝spec04 受信テストは (a) VisionEnabled=false 固定で現行検証＋(b) 新 UX の新テスト追加。既定挙動変更を CLAUDE.md/README 反映対象として §6 に明示。 | 済（AC13 を (a)+(b) 両更新へ書換。§6 に「既定 ON で全ユーザーの受信 UX が変わる」明記＋要ドキュメント反映） |
| 3 | Minor | §3.1 / UX | VisionEnabled=true で choices 提示後、ボタン未タップで素テキストを送ると現在モードで解釈され編集/質問にならない（spec04 の素テキスト＝編集からの挙動変化）。仕様に明記なし。 | 「ボタン選択後に指示/質問を送る」前提を文書化（spec04 §2.3 のレース注記と同様）。 | 済（§3.1 に注意書き追記。後から postback 再アーム可も明記） |
| 4 | Minor | AC3 / UserMessages.EmptyAnswer | 再利用する EmptyAnswer 文言はチャット前提で VQA としてはやや不自然。実害小。 | 許容可（vision 専用文言は任意）。 | 受容（初版は EmptyAnswer 再利用のまま） |
| 5 | Minor | §6（VisionModel 既定・provider 依存） | 既定 ON だが VisionModel が token の provider で利用不可の環境では [質問]→Error に落ちる。失敗時 surface を仕様側でも明確化推奨。 | §6 に失敗時挙動（汎用 Error）と差し替え方法を明記。 | 済（§6 に「利用不可環境では [質問]→汎用 Error、差し替え/無効化(VisionEnabled=false)」を明記） |

## 判定理由（初回 FAIL）
スコープ整合（ユーザー送信写真のみ・ワンショット・SK 不採用の根拠＝Context7 確認）、ワイヤ形式（OpenAI 互換 content part）、UserStateStore の AwaitingEdit→PendingAction 置換の影響範囲（Snapshot/SetPending/SetReceivedImage/Dispatcher 分岐・キャンセル箇所）は §4 で網羅的に押さえられており良好。§9 も全 [x] 決定済み。

FAIL の根拠は Blocker #1：エラー処理の参照テンプレートが ChatService（サービスが OCE/空を文字列化）と ImageEditService（OCE 送出）で二重化し、WorkProcessor 最上位 catch が OCE を除外する現実装と組み合わさると AC5（タイムアウト→Timeout）が到達不能。record 26 で一度踏んで修正した既知の罠であり、仕様段階で契約確定が必須。加えて Major #1（既定 VisionEnabled=true が spec04 の受信既定 UX を変え AC13 が不正確）は親から明示された「受信写真フローの分岐変更が spec04 の挙動を壊さないか」に直接該当。

## 反映（2026-08-17）
上表の Blocker #1 / Major #1 / Minor #3 / Minor #5 を spec07 に反映済み（Minor #4 は受容）。エラー契約を ChatService に一本化し、AC13 を既存テスト更新方針へ訂正、既定 ON の UX 変更を文書化対象として明示。

再ゲート結果: **PASS**。Blocker#1（ChatService 準拠でエラー契約一本化、OCE→Timeout/空→EmptyAnswer をサービス側変換、非2xx のみ送出→最上位 catch→Error）・Major#1（AC13 を VisionEnabled=false 固定の現行検証＋新 UX 新テストへ訂正、§6 に既定 ON の UX 変更明記）・Minor#3/#5 反映を確認、`ChatService.cs` 実挙動と一致。残 Minor 2＝§1 の AnswerAsync 署名を 3 引数(mediaType 追加)へ表記統一／§10 の雛形役割(ChatService=エラー契約, ImageEditService=data URI)併記——いずれも本反映で解消済み・非ブロッキング。
