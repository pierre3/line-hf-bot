# 仕様: vision フォローアップ / 会話型 vision（生成画像への質問＋マルチターン）

- 状態: 実装済み（仕様ゲート PASS 相当 → 実装・テスト 2026-08-18）
- 対象: (Part 1) 生成/編集画像にも「💬 質問」を出す。(Part 2b) vision を**会話型**にし、同じ画像への追い質問を文脈込みで継続できるようにする。
- 関連: `docs/specs/07-image-vision-vqa.md`（送信写真へのワンショット質問＝基盤／`PendingAction.VisionQuestion`／`WorkKind.Vision`／`Ai/VisionService.cs`）、`docs/specs/08-image-to-video.md`（`QuickReplyFactory` の gate 注入・作業中画像モデル）、`docs/specs/03-mode-context-richmenu.md`（モード状態・postback）
- 背景: spec07 で送信写真への**ワンショット**質問（VQA）が入った。質問経路（`action=ask`→`PendingAction.VisionQuestion`→`WorkKind.Vision`、`LastImageId` の画像に対して回答）は既に完成しているが、(a) 「💬 質問」ボタンは**送信写真の選択 QuickReply にしか無く**、生成/編集画像には出ない。(b) 回答は QuickReply 無しで push され、1問で終わる（次の指示語「その車の色は?」は解決できない）。本 spec はこの 2 点を埋める。

## 1. 目的 / スコープ

### Part 1｜生成・編集画像への質問（低コスト）
生成画像・編集結果の QuickReply（`ImageResult`）に **「💬 質問」ボタン**を追加する。`App__VisionEnabled=true` のときのみ表示（Animate が `VideoEnabled` で gate されているのと同じ作法）。既存の `action=ask` ハンドラは `LastImageId` を参照して動くため、**新しい質問ロジックは不要**（生成画像も `LastImageId` を設定済み）。

### Part 2b｜会話型 vision（マルチターン）
vision 回答後、**同じ画像に対する追い質問を文脈込みで継続**できる「vision セッション」を導入する。
- セッション中は、**素（非コマンド）メッセージを同じ画像への追い質問**として解釈し、直前までの Q&A をモデルに渡す（指示語が解決できる）。
- 回答には QuickReply（`✏️編集` / `🎬動画にする`（動画有効時）/ `💬チャットへ`）を付け、離脱導線を明示する。追い質問は**そのまま入力**すればよい（ボタン不要）。
- セッションは **モード切替 / スラッシュコマンド / 新しい画像受信 / 再生成 / 編集・動画の arm / `/reset`** で終了する（既存の「保留アクション解除」と同じ地点に相乗り）。
- 画像は既存の `MediaStore` TTL（既定10分）で保持。**期限切れ＝セッション終了**（`VisionImageExpired`）。

### スコープ外
- **動画・音声への質問**（VL モデルは静止画のみ想定）。対象は画像のみ。
- **複数画像の同時文脈**（1セッション＝1画像）。新しい画像を受信/生成したらセッションは切り替わる。
- **セッションの永続化**（インメモリのみ・再起動で消失。プロジェクト方針どおり）。
- vision の**プロバイダ・フォールバック**（503/model_not_supported の自動切替）は別テーマ（本 spec では扱わない）。
- 生成画像結果の `💬 質問` は `App__VisionEnabled=false` 時は非表示（送信写真の選択 UI と同じ扱い）。

## 2. 確定済みの前提（spec07 から流用）

- vision は OpenAI 互換 `/v1/chat/completions` 直叩き（`Ai/VisionService.cs`）。画像は base64 data URI の `image_url` content part。Bearer は router のみ。**新規プロトコル面・SSRF 面は無い**（結果 URL の再取得なし）。
- エラー契約は `HuggingFaceChatService` 準拠: `AnswerAsync` は表示可能文字列を返す（OCE→`Timeout` / 空→`EmptyAnswer` はサービス側変換）。非2xx のみ送出し、`WorkProcessor` 最上位 catch（OCE 除外）が `Error` に変換。**この契約は維持する**。
- vision モデルは **provider を pin（`model:provider`）** し HF 設定で有効化が必要（spec07 の教訓）。既定 `Qwen/Qwen2.5-VL-72B-Instruct:ovhcloud`。本 spec は設定を変えない。

## 3. 会話型 vision のメッセージ組み立て（マルチターン）

vision エンドポイントはステートレスなので、**毎リクエストで会話全体を再送**する。画像は文脈に**1枚だけ**含める（標準的なマルチモーダルチャットの形）。

```
messages = [
  { role: "user",      content: [ {type:text, text: Q1}, {type:image_url, image_url:{url: dataUri}} ] },
  { role: "assistant", content: A1 },
  { role: "user",      content: [ {type:text, text: Q2} ] },     // 2ターン目以降は text のみ
  { role: "assistant", content: A2 },
  ...
  { role: "user",      content: [ {type:text, text: Qn} ] },     // 今回の質問
]
```
- **画像は最初の user ターンにのみ**添付する。履歴が空（1ターン目）なら画像は今回の質問に添付する。
- 履歴の各 `(Q, A)` はセッションに蓄積。**再送のたびに画像トークン＋テキストが積み上がる＝クレジット消費が増える**ため、ターン数に上限を設ける（§5）。

## 4. 実装方針

### 4.1 状態（`State/UserStateStore.cs`）
- 新レコード `public readonly record struct VisionTurn(string Question, string Answer);`（State 名前空間）。
- `UserState` に `string? VisionImageId` と `List<VisionTurn> VisionTurns` を追加（単一のアクティブセッション）。`VisionImageId==null` はセッション非アクティブ。
- 追加 API（すべて既存のオブジェクトロック下）:
  - `IReadOnlyList<VisionTurn> GetVisionHistory(string userId, string imageId)` — アクティブかつ `VisionImageId==imageId` なら蓄積ターン、そうでなければ空配列（画像が変わったら文脈リセット）。
  - `void AppendVisionTurn(string userId, string imageId, VisionTurn turn, int maxTurns)` — `VisionImageId=imageId` に設定し `turn` を追加。`maxTurns` 超過分は先頭から破棄（直近を保持）。`imageId` が既存 `VisionImageId` と異なる場合はターンを作り直す（新しい被写体）。
  - `void ClearVisionSession(string userId)` — `VisionImageId=null` / `VisionTurns` クリア。
- **セッションを終了（Clear）する地点**（既存の「保留解除」に相乗り）:
  - `SetMode`（モード切替）/ `regen` / `edit` の arm / `animate` の arm / スラッシュコマンド実行 / `SetReceivedImage`（新規受信）/ `SetLastImage`（新規生成）/ `Reset`。
  - **`SetPending(VisionQuestion)`（ask の再 arm）では Clear しない**（worker が画像 id で継続/リセットを判断）。
  - **`SetLastImageId`（編集結果のチェーン更新）は Clear 対象外**。編集は必ず `edit` postback（→`ClearVisionSession`）でアームされ、その時点でセッションは解除済みのため（リークしない）。
- `Snapshot` に `bool VisionActive`（＝`VisionImageId!=null`）と `string? VisionImageId` を追加（dispatcher のルーティング判定用）。

### 4.2 サービス（`Ai/VisionService.cs`）
- シグネチャ変更:
  ```csharp
  Task<string> AnswerAsync(byte[] image, string mediaType,
      IReadOnlyList<VisionTurn> history, string question, CancellationToken ct);
  ```
  （spec07 の 4 引数版は廃止し 5 引数へ。呼び出しは worker のみ。）
- §3 の messages を組み立てる。`history` 空なら画像は `question` の user ターンに添付、非空なら `history[0].Question` の user ターンに添付し以降は text のみ、末尾に今回の `question`（text のみ）。
- エラー契約・タイムアウト・`ExtractContent` は spec07 のまま。

### 4.3 配線（`Messaging/MessageDispatcher.cs`）
- **スラッシュ先取り**: `raw` がスラッシュコマンドなら、保留/セッションを解除してから通常のコマンド処理へ（現状の保留ブロックは slash で fall-through 済み。セッションも同時に Clear する）。
- **ルーティング優先順位**（`HandleTextAsync`）:
  1. `Pending != None`（既存）: `VisionQuestion`→`Vision`（1ターン目）/ `Animate`→`ImageToVideo` / それ以外→`ImageEdit`。`RefImageId=LastImageId`。
  2. **セッションアクティブ かつ 非スラッシュ**（新規）: `WorkKind.Vision` を enqueue（`Text=raw`、`RefImageId=snapshot.VisionImageId`）＝追い質問。
  3. それ以外: 現在モードで解釈（既存）。
- **postback**:
  - `ask`（既存）: `LastImageId` があれば `Pending=VisionQuestion`＋`VisionPrompt` 返信（変更なし。生成/編集画像からも同じ経路で到達）。
  - `mode` / `regen` / `edit` / `animate`: それぞれの arm 時に `ClearVisionSession` を呼ぶ（§4.1）。

### 4.4 ワーカー（`Queue/WorkProcessor.HandleVisionAsync`）
1. 冪等化（`processedEvents.TryMarkNew`、既存）。
2. 作業画像 = `item.RefImageId`。`mediaStore.TryGet` 失敗（期限切れ）→ `ClearVisionSession` ＋ `VisionImageExpired` を送って終了。
3. `history = userState.GetVisionHistory(userId, RefImageId)`（画像不一致なら空＝新被写体）。
4. ack（`VisionThinking`）を reply トークンで返す（既存）。
5. `answer = visionService.AnswerAsync(bytes, contentType, history, item.Text, ct)`。
6. **失敗ターンは文脈に入れない・セッションを開始しない**: `answer` が `messages.Timeout` / `messages.EmptyAnswer` と一致する場合は `AppendVisionTurn` を**呼ばない**（ゴミ文脈の防止）。この場合 `VisionImageId` は変化せず、**初回ターンで失敗したときはセッションが開始されない**（次の素メッセージは priority2 に乗らず現在モードで解釈される＝spec07 相当のワンショット挙動へフォールバック）。成功時のみ `AppendVisionTurn(userId, RefImageId, new(item.Text, answer), maxTurns)`。
7. push: `answer` を `VisionAnswer` QuickReply 付きで送る（成功/失敗を問わず＝作業中画像への Edit/[Animate]・離脱導線は常に有効）。`VisionFollowupHint`（`answer` 末尾に改行連結）は、**このターンで実際に `AppendVisionTurn` された（＝vision 成功）かつ push 前の history が空だった（＝初回成功ターン）**場合に**のみ**付す。**初回で失敗したターン（`Timeout`/`EmptyAnswer`）はセッション未開始のためヒントを付けない**（「続けて質問できます」と誤って約束しない）。2ターン目以降も付けない。

### 4.5 QuickReply（`Line/QuickReplyFactory.cs`）
- `ImageResult` に、`VisionEnabled=true` のとき `Item(LabelAsk, "action=ask")` を追加。順序: **Regenerate / Edit / Ask（vision時）/ Animate（video時）/ Chat**。→ `IOptions<AppOptions>` から `VisionEnabled` を読む（`VideoEnabled` と同様に追加）。
- 新規 `VisionAnswer`（vision 回答に付与）:
  - 項目: `✏️編集`（action=edit）/ `🎬動画にする`（action=animate、`VideoEnabled` 時のみ）/ `💬チャットへ`（action=mode&value=chat）。
  - いずれのボタンも既存ハンドラでセッションを Clear する（edit/animate は arm、mode は切替）。追い質問はボタン無しで入力継続。
- `ReceivedImageChoices`（受信写真・vision 有効時）は現状どおり（Edit / Ask / Animate）。

### 4.6 設定（`Configuration/BotOptions.cs`）
- `App__VisionMaxTurns`（既定 `8`）＝セッションに保持する Q&A ペア数の上限。**再送コンテキスト＝クレジット消費を抑える**ための上限（§3）。0/負は 1 に丸め（最低 1 ターン）。
  - 置き場所は `AppOptions`（`VisionEnabled` と同じ挙動系）。※`Chat__MaxHistory`（チャット履歴）とは別軸（vision は画像込みで単価が高い）。
- 既存キー（`VisionModel`/`VisionEndpoint`/`VisionTimeoutSeconds`/`VisionEnabled`）は**変更なし**。

### 4.7 文言（`Text/UserMessages.cs`、en/ja）
- 新規 `VisionFollowupHint`:
  - en: `You can keep asking about this image. Tap 💬 Chat when you're done.`
  - ja: `この画像について続けて質問できます。終わるときは 💬 チャットへ。`
- `Help` を 1 行更新（写真/画像について「続けて質問できる」旨を追記）。
- 既存 `VisionPrompt`/`VisionThinking`/`VisionImageExpired`/`LabelAsk`/`LabelBackToChat` は流用。

### 4.8 DI / dev（`Program.cs`）
- 追加登録は無し（`IVisionService` は登録済み、シグネチャ変更のみ）。
- 任意: dev `/dev/vision` があれば history 版に更新（無ければ追加は任意）。

## 5. 設定変更（既定・反映先）
| キー | 既定値 |
|---|---|
| `App__VisionMaxTurns` | `8` |

- 反映先: `Configuration/BotOptions.cs`（コード既定）/ `appsettings.json` / `.env.example` / `README.md` / `README.ja.md` / `CLAUDE.md`（コード既定＝表＝appsettings.json の三者一致）。
- ドキュメントに「会話型 vision は毎ターン画像＋履歴を再送するため、ターン数に比例してクレジットを消費する（上限 `VisionMaxTurns`）」を明記。

## 6. 受入基準（テスト可能）
1. **Part 1**: `VisionEnabled=true` のとき `ImageResult` QuickReply に `LabelAsk`（action=ask）を含む（Regenerate/Edit/Ask/[Animate]/Chat）。`false` では含まない（従来どおり）。
2. 生成/編集画像で `action=ask`→`Pending=VisionQuestion`→次の素メッセージが `WorkKind.Vision`（`RefImageId=LastImageId`）として enqueue される。
3. **マルチターン組み立て**: `history` 非空で `AnswerAsync` を呼ぶと、messages は「最初の user ターンにのみ画像を含み、以降は text のみ、末尾に今回の質問」の順になる。`history` 空なら画像は今回の質問ターンに付く。
4. **セッション継続**: vision 回答後、素メッセージ（非スラッシュ）が `WorkKind.Vision`（`RefImageId=VisionImageId`）として enqueue される（モードに関係なく）。
5. **文脈受け渡し**: worker は `GetVisionHistory(userId, RefImageId)` を渡し、回答後に `AppendVisionTurn` で `(question, answer)` を追加する。`VisionMaxTurns` を超えると先頭から破棄される。
6. **画像切替でリセット**: `RefImageId` がセッションの `VisionImageId` と異なる場合、`GetVisionHistory` は空を返す（前画像の文脈を持ち越さない）。
7. **失敗ターンは非蓄積・セッション未開始**: `answer` が `Timeout`/`EmptyAnswer` の文言と一致する場合、`AppendVisionTurn` は呼ばれず `VisionImageId` は変化しない（初回失敗なら以降の素メッセージは現在モードで解釈される）。判定は **en/ja 両ロケール**の `Timeout`/`EmptyAnswer` 文言に対して機能することをテストで固定する。
8. **初回ヒント**: セッションの**初回成功ターン**（`AppendVisionTurn` が行われ、かつ直前 history が空）の回答にのみ `VisionFollowupHint` が付与される。**失敗初回ターン・2ターン目以降は付かない**。`VisionAnswer` QuickReply（Edit/[Animate]/Chat）は成功/失敗を問わず付与される。
9. **回答 QuickReply**: vision 回答は `VisionAnswer` QuickReply（Edit / [Animate（video時）] / Chat）付きで push される。
10. **セッション終了**: `mode`切替 / スラッシュコマンド / `regen` / `edit` arm / `animate` arm / 新規受信画像（`SetReceivedImage`）/ 新規生成（`SetLastImage`）/ `/reset` のいずれでも `VisionImageId` がクリアされ、以降の素メッセージはセッション扱いされない。
11. **期限切れ**: 追い質問時に `RefImageId` が MediaStore に無ければ `VisionImageExpired` を返し、セッションをクリアする。
12. 既定値（`VisionMaxTurns=8`）がコード/`appsettings.json`/ドキュメントと一致。
13. 既存テスト（spec07 vision・dispatcher・quickreply・receive image）は緑（回帰なし）。`AnswerAsync` の 5 引数化に伴うテスト更新のみ許容。

## 7. 決定事項 / 要確認（ドラフト）
- [x] Part 1（生成/編集画像への質問ボタン）＋ Part 2b（会話型 vision）を実装（ユーザー選択、2026-08-18）。
- [x] 会話モデル = **画像は最初の user ターンに 1 枚**、毎リクエストで全履歴を再送（ステートレス API のため）。
- [x] セッションは**スティッキー**（アクティブ中は素メッセージ＝追い質問）。離脱は QuickReply の `💬チャットへ` / リッチメニュー / スラッシュ / 新画像。→ 素メッセージがチャットに流れなくなる**挙動変更**を初回ヒントと QuickReply で明示。
- [x] **決定①（2026-08-18）**: `App__VisionMaxTurns` 既定 `8`（Q&A ペア数）。operator が env で調整可。
- [x] **決定②（2026-08-18）**: 回答 QuickReply = **Edit / Animate（video時）/ Chat**。`🔄再生成` は元 prompt の無い受信写真で無効になり得るため除外。
- [x] **決定③（2026-08-18）**: 初回ヒント（`VisionFollowupHint`）は**回答本文の末尾に改行連結**（push 1 回・初回ターンのみ）。
- [x] **決定④（2026-08-18・仕様ゲート指摘1 反映）**: 初回ヒント/継続導線は「**実際に `AppendVisionTurn` された初回成功ターン**」に gate する。初回で vision 失敗（`Timeout`/`EmptyAnswer`）時はセッションを開始せず、ヒントも付けない（spec07 相当のワンショットへフォールバック）。`VisionAnswer` QR は成功/失敗とも付与。
- [x] **決定⑤（2026-08-18・指摘2/3 反映）**: `SetLastImageId`（編集チェーン）は `edit` arm で解除済みのため Clear 対象外（明文化）。失敗判定の文字列一致は現契約（spec07「表示可能文字列」）踏襲＝en/ja 両文言をテストで固定。将来 `VisionService` の構造化シグナル化は改善余地として残す。

## 8. 参考
- `docs/specs/07-image-vision-vqa.md`（vision 基盤・エラー契約・provider pin の教訓）。
- `Ai/VisionService.cs`（OpenAI 互換 messages 構築）/ `State/UserStateStore.cs`（`PendingAction`・作業中画像）/ `Line/QuickReplyFactory.cs`（gate 注入）/ `Queue/WorkProcessor.HandleVisionAsync`。
- OpenAI 互換 multimodal chat: user ターンに `content: [{type:text},{type:image_url}]`、assistant は text。履歴はクライアントが毎回再送（router/HF はステートレス）。
