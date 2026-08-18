# レビュー記録 47 — spec09 会話型 vision（フォローアップ/マルチターン）セキュリティゲート

- 日付: 2026-08-18
- ゲート: セキュリティ（4段階中3）
- 対象: spec09（docs/specs/09-vision-followup-multiturn.md §2・§4）。変更は**未コミット**（`git status --short` の 6 ソース + テスト + docs）
- 委譲: dotnet-claude-kit:security-scan（6層）を主分析として実行。42crunch / claude-security は不適用（新規 HTTP API 面・OpenAPI・新規エンドポイントなし＝差分は内部配線・状態・メッセージ組み立てのみ）
- 判定: **PASS**

## 対象ソース
- `LineHfBot/Ai/VisionService.cs`（`AnswerAsync` 5 引数化＝履歴再送・`BuildMessages`）
- `LineHfBot/State/UserStateStore.cs`（vision セッション: `VisionImageId`/`VisionTurns`・`GetVisionHistory`/`AppendVisionTurn`/`ClearVisionSession`）
- `LineHfBot/Queue/WorkProcessor.cs`（`HandleVisionAsync` 継続/失敗非蓄積/初回ヒント）
- `LineHfBot/Messaging/MessageDispatcher.cs`（slash 先取り→Clear、priority1 pending / priority2 session / priority3 mode）
- `LineHfBot/Line/QuickReplyFactory.cs`（`ImageResult` に Ask、新規 `VisionAnswer`）
- `LineHfBot/Configuration/BotOptions.cs`（`VisionMaxTurns` 既定 8）/ `Text/UserMessages.cs` / `appsettings.json` / `.env.example`

## 6層サマリ
| 層 | 結果 |
|---|---|
| 1 パッケージ脆弱性 | PASS（`dotnet list package --vulnerable --include-transitive` 0 件、新規依存なし）|
| 2 シークレット | PASS（差分は int 設定 `VisionMaxTurns` のみ。ハードコード秘密なし・`.env.example` は空値方針維持）|
| 3 OWASP パターン | PASS（SQL/`Html.Raw`/危険なデシリアライズなし。IDOR/SSRF 新規面なし＝後述）|
| 4 認証/アクセス制御 | PASS（webhook 署名検証・エンドポイント定義とも**無変更**＝回帰なし）|
| 5 CORS | PASS（変更なし）|
| 6 データ保護/ログ | PASS（token/画像本文/質問・回答文の非露出）|

## 重点確認（CLAUDE.md 規約）
1. **署名検証の回帰なし**: 本差分は `/webhook` の受信/署名検証パス（生ボディ→ParseAsync→401/400）に一切触れていない。変更は署名検証後の `MessageDispatcher.HandleTextAsync` のルーティング分岐と状態管理のみ。
2. **トークン漏洩なし**: `VisionService.AnswerAsync` の Bearer は `opt.VisionEndpoint`（router 既定 `/v1/chat/completions`）にのみ付与（`VisionService.cs:49-50`）。送信先はオペレーター設定の固定 URL でユーザー入力に影響されない。**結果 URL の再取得は無い**ため新規の送信先も無い。ログは `kind/user/eventId` のみ（`WorkProcessor.cs:90,198`）で質問文 `item.Text`・回答・画像・token を出さない。
3. **SSRF 新規面なし**: 会話履歴（Q&A テキスト）＋画像を base64 data URI で `VisionEndpoint`（router 固定）へ送るだけ。外部 URL の取得経路は増えていない。画像はユーザー自身の作業中画像（MediaStore、受信画像は spec04 の取得上限/タイムアウト済み）。
4. **メディア id 注入（IDOR）なし**: フォローアップの `RefImageId` は priority2 で `snapshot.VisionImageId`、priority1 で `snapshot.LastImageId`（`MessageDispatcher.cs`）＝いずれもサーバ側ユーザー状態。`VisionImageId` は `AppendVisionTurn(imageId=item.RefImageId)` でのみ設定され、その `imageId` は同一ユーザー自身の画像フローに由来（`SetLastImage`/`SetReceivedImage`/継続）。**ユーザー入力テキストが id になる経路は無い**ため、任意 id 注入で他ユーザーのメディアを引くことはできない。
5. **メモリ/DoS 上限**: `VisionMaxTurns`（既定 8）で per-user のターン数を上限化。`AppendVisionTurn` は `Math.Max(1, maxTurns)` で 0/負を 1 に丸め（`UserStateStore.cs`）、超過分は先頭から `RemoveRange` で破棄。**画像は `VisionTurns` に保存せず**毎回 MediaStore から取得＝ターンあたりの保持は Q&A 文字列のみで有界。期限切れ（`mediaStore.TryGet` 失敗）は `ClearVisionSession`＋`VisionImageExpired` で確実にセッション終了（`WorkProcessor.cs`）。
6. **並行性**: `UserStateStore` の read/append/clear は全て per-object `lock` 下。`GetVisionHistory` は `ToArray()` で防御コピーを返し、呼び出し側がロック外でリストの変異を見ない。
7. **脆弱パッケージなし**（層1）。

## 指摘（いずれも Informational・ブロックせず）
- **[Info] 履歴再送によるリクエスト増**（`VisionService.BuildMessages`）: フォローアップ毎に画像（base64、+33%）＋全 Q&A を再送。`VisionMaxTurns=8` と `VisionTimeoutSeconds`（下限5s の linked CTS）・`Queue.Capacity`(100) で上限あり。クレジット消費面は spec §3/§5 で明記済み・受容。
- **[Info] 同一ユーザー 2 ワーカー時の TOCTOU**（`WorkProcessor.HandleVisionAsync`）: `GetVisionHistory`→HF 呼び出し→`AppendVisionTurn` が原子的でないため、同一ユーザーが連続フォローアップを送ると文脈の取り違え/ターン重複が起こり得る。**セキュリティ影響ではなく UX/整合の軽微事項**（`Queue.Workers=2`。会話は同一ユーザーが逐次送るのが通常）。将来 per-user 逐次化の余地。
- **[Info] per-user 状態の非退避**: `ConcurrentDictionary` にユーザー毎の状態が蓄積し明示退避が無いが、本差分以前からの既存性質。今回の追加分（`VisionImageId`＋有界な `VisionTurns`）は上限内で軽微。

## 結論
Critical / High / Medium は 0 件。差し戻し事項なし。重点4点（署名検証・トークン漏洩・SSRF・メディア id 注入）および DoS/メモリ上限はいずれも満たす。ドキュメントレビュー（ゲート4）へ進行可。

（注: 本判定は静的解析ベースであり、ペネトレーションテストの代替ではない。）
