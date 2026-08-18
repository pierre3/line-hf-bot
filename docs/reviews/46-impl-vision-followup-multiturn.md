# 実装レビュー — vision フォローアップ / 会話型 vision（spec09） (2026-08-18)
Verdict: PASS
委譲分析: dotnet-claude-kit:code-review（Roslyn MCP: get_diagnostics / detect_antipatterns）

## チェックリスト / 指摘
| # | 重大度 | 箇所 | 問題 | 必要な対応 |
|---|---|---|---|---|
| 1 | Minor | `Queue/WorkProcessor.cs:210-232`（HandleVisionAsync） | `GetVisionHistory` 読取と `AppendVisionTurn` 追加の間に、同一ユーザーの別メッセージが並行処理（Workers=2）されると history 読取と append の順序が入れ替わり得る（TOCTOU）。個々の操作は lock 下で原子的だが複合操作は非原子。インメモリ設計に内在する既存特性で、同一ユーザーが同時に2発 push する状況は稀・被害はターン蓄積順の乱れのみ。 | 対応不要（設計内）。将来 per-user 直列化や read-append 一体 API 化を検討余地として記録。 |
| 2 | Minor | `Messaging/MessageDispatcher.cs`（priority 2） | セッションアクティブ中に空白のみ（trim 後空文字）の素メッセージが来ると空 `Text` の `WorkKind.Vision` を enqueue する。現行の各モード（chat/image）も空入力を素通しするため挙動は一貫。 | 対応不要（既存挙動と一貫）。 |
| 3 | Minor | `Queue/WorkProcessor.cs:220-223` | 失敗判定を `messages.Timeout`/`EmptyAnswer` との文字列一致（Ordinal）で行う。ロケール一致の UserMessages インスタンスに対して正しく機能し en/ja 両方をテスト固定済みだが、将来文言変更や構造化未対応の脆さは残る。spec 決定⑤で「現契約踏襲・構造化は将来改善」と明記済み。 | 対応不要（spec 合意どおり）。将来 `VisionService` の構造化シグナル化を改善余地として残す。 |

## 受入基準（§6）充足状況
- AC1（ImageResult に Ask ボタン・VisionEnabled gate、順序 Regenerate/Edit/Ask/[Animate]/Chat）: ✓ `QuickReplyFactory.ImageResult` + `QuickReplyFactoryTests`（enabled/disabled/両有効）。
- AC2（action=ask→Pending=VisionQuestion→次素メッセージが Vision, RefImageId=LastImageId）: ✓ priority 1 分岐（既存 ask ハンドラ不変）+ `Pending_vision_question_routes_plain_text_to_Vision`。
- AC3（マルチターン組み立て・画像は最初の user ターンのみ・以降 text のみ・末尾に今回質問）: ✓ `VisionService.BuildMessages` + `Multiturn_attaches_image_to_first_user_turn_only`。空 history 時は今回質問に画像添付も確認。
- AC4（セッション継続・モード非依存）: ✓ priority 2（`snapshot.VisionActive`）+ `Active_vision_session_routes_plain_text_to_Vision`（image モードでもセッション優先）。
- AC5（履歴 cap `VisionMaxTurns`）: ✓ `AppendVisionTurn`（`Math.Max(1,maxTurns)`＋先頭破棄）+ store/worker 両テスト。
- AC6（画像切替でリセット）: ✓ `GetVisionHistory` の id 不一致→空 + `Different_image_resets_history`/`GetVisionHistory_returns_empty_for_a_different_image`。
- AC7（失敗ターン非蓄積・セッション未開始、en/ja）: ✓ `succeeded` 判定 + `First_turn_failure_does_not_open_session`/`Empty_answer_first_turn_does_not_open_session`（両 Theory で en/ja）。
- AC8（初回成功ターンのみヒント）: ✓ `succeeded && firstTurn` gate + `First_success_...with_hint` / `Followup_...omits_hint` / 失敗初回はヒント無しをテスト。
- AC9（VisionAnswer QR を成功/失敗問わず付与）: ✓ `PushTextAsync(..., quickReplies.VisionAnswer)` 常時 + 各テストで PushQuickReplies 非 null。
- AC10（セッション終了点）: ✓ store 側（`SetMode`/`SetLastImage`/`SetReceivedImage`/`Reset` で `ClearVision`）+ dispatcher 側（`regen`/`edit` arm/`animate` arm/スラッシュで `ClearVisionSession`）。`SetLastImageId`・`SetPending` は非 Clear（decision⑤/ask 再 arm）をテストで固定。
- AC11（期限切れ Clear）: ✓ `mediaStore.TryGet` 失敗時 `ClearVisionSession`＋`VisionImageExpired` + `Expired_reference_image_notifies_clears_session_and_does_not_call_vision`。
- AC12（既定値 8 の三者一致）: ✓ `AppOptions.VisionMaxTurns=8` / `appsettings.json:35` / `.env.example:63` / README(EN/JA) / CLAUDE.md すべて 8・min 1 で一致。
- AC13（回帰なし・5引数化のみ）: ✓ `dotnet test` = 131 緑（build 0/0）。既存 vision/dispatcher/quickreply/receive image テストは 5 引数化に合わせて更新のみ。

## 判定理由
Blocker/Major は 0 件。委譲した Roslyn 分析で本変更ファイルの診断は 0/0/0、antipattern も spec09 由来の新規指摘なし（検出された AP005/AP007/AP009/AP003 はすべて Program.cs・LineMessenger・HfHttp 等の既存 catch か、テストの `new HttpClient()`/CancellationToken 欠如という慣行的なもので、本 spec のプロダクションコードに新規混入していない）。

規約適合を確認: モダン C#（record struct `VisionTurn`、collection expression、primary constructor 踏襲）、`IHttpClientFactory`/`TimeProvider` 系は不変、秘密情報のログ露出なし（コメント/ログは英語、ユーザー文言は `UserMessages` en/ja 集約）。エラー契約は spec07（`HuggingFaceChatService` 準拠）を維持＝`AnswerAsync` は表示可能文字列を返し非2xx のみ送出、最上位 catch（OCE 除外）が Error 変換。

重点確認項目もクリア:
- **スレッド安全性**: 追加した `GetVisionHistory`/`AppendVisionTurn`/`ClearVisionSession` はすべて既存 `UserState` オブジェクトロック下で操作。`GetVisionHistory` は `ToArray()` コピーを返し呼び出し側にリスト mutation を露出しない。`Snapshot` はロック下でスカラをコピー。
- **ルーティング優先順位**: スラッシュ先取り→(1)Pending→(2)セッション→(3)モード の順が spec §4.3 と一致。旧「pending ブロックで slash fall-through」から「slash 先取り」への再構成でも、pending+slash は従来同様「pending 解除＋コマンド実行」（＋vision Clear 追加）で挙動等価（`Slash_command_cancels_pending_*` が実証）。
- **失敗判定の文字列一致**: 現契約（表示可能文字列）踏襲。en/ja 両ロケールで `Timeout`/`EmptyAnswer` 一致をテスト固定（AC7）。
- **セッションリーク防止**: `SetLastImageId`（編集チェーン）は edit postback が事前に Clear 済みのため非 Clear が正当（decision⑤）。`SetPending(ask 再 arm)` 非 Clear は worker が image id で継続/リセット判定するため安全（新画像なら `GetVisionHistory` が空を返し新被写体）。

残る指摘は Minor 3 件のみで、いずれもインメモリ設計に内在する既存特性か spec で明示合意済みの受容事項。次工程（セキュリティレビュー）に影響しないため PASS とする。
