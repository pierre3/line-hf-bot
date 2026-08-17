# 仕様: 画像→チャット（vision / VQA）— ユーザー送信写真への質問応答

- 状態: ドラフト（仕様ゲート待ち）
- 対象: ユーザーが送った写真について**テキストで質問し、AI が答える**（Visual Question Answering）機能を追加する
- 関連: `docs/specs/04-user-image-edit.md`（受信写真の取得・保存・`AwaitingEdit` フロー）、`docs/specs/01-line-hf-bot.md`（チャット＝SK/HF `/v1/chat/completions`）
- 背景: これまで受信写真は**編集入力（image-to-image）専用**だった。同じ受信写真を「編集」だけでなく「質問対象」にもできるようにし、チャットの価値を広げる。チャットに使う HF router は **OpenAI 互換** (`/v1/chat/completions`) で、vision 対応モデルなら `image_url`（base64 data URI）content part を受け付ける。

## 1. 目的 / スコープ
ユーザーが写真を送ったとき、**「✏️ 編集」/「💬 この画像について質問」を QuickReply で選ばせ**、質問を選べば次のテキストを質問として vision モデルに投げ、回答を返す。

- 新サービス `IVisionService.AnswerAsync(byte[] image, string mediaType, string question, ct)` を追加（署名の権威は §5）。既存の画像/動画/編集サービスと同じく **`HttpClient` で HF router の `/v1/chat/completions`（OpenAI 互換）を直接呼ぶ**（画像は base64 data URI の `image_url` content part）。SK の HuggingFace コネクタは vision（マルチモーダル `ImageContent`）対応が不確実なため**採用しない**（SK 例は OpenAI/Ollama のみ。2026-08-17 Context7 確認）。
- 受信写真フロー（spec04）の**取得・保存は不変**（`LineContentService` / `MediaStore` / 上限・タイムアウト・`external` 拒否）。分岐（編集 or 質問）だけ追加する。

### スコープ外
- **ボット生成画像への質問**（`LastImageId` の生成物）。初版は**ユーザー送信写真のみ**（決定 §6）。後続で生成結果 QuickReply に「質問」を足す余地は残す。
- **マルチターン追跡**（同じ画像への連続質問／画像を会話履歴に保持）。初版は**ワンショット**（1 質問→1 回答、`ChatHistoryStore` とは独立＝読まない・書かない）。
- vision パラメータ（温度・最大トークン等）のユーザー指定。初版は既定のみ。
- 画像 URL 参照や複数画像同時質問。1 枚のみ。

## 2. 確定したワイヤ形式（OpenAI 互換 chat completions・HF router）
既存チャットが SK 経由で叩いている `POST {ChatEndpoint}/v1/chat/completions` と同じエンドポイント形式。vision はマルチモーダル content part を使う。

1. **request**: `POST https://router.huggingface.co/v1/chat/completions`（`Authorization: Bearer hf_***`、`Content-Type: application/json`）
   ```json
   {
     "model": "<VisionModel>",
     "messages": [
       { "role": "user", "content": [
         { "type": "text", "text": "<question>" },
         { "type": "image_url", "image_url": { "url": "data:image/png;base64,<...>" } }
       ]}
     ]
   }
   ```
   - `model` は `HuggingFace__VisionModel`。画像は `MediaStore` に保存済みの受信写真バイトを base64 data URI 化（MIME は保存時の content-type、不明なら `image/png`）。
   - system プロンプトは付けない（ワンショット・履歴独立）。回答言語は質問テキストに追随（既存チャット同様）。
2. **response 200**: `{ "choices": [ { "message": { "role":"assistant", "content":"<answer>" }, ... } ], ... }`
   - `choices[0].message.content` を回答テキストとして取り出す。**空/欠落は `IVisionService` 内で `EmptyAnswer` 文字列に変換して返す**（＝ChatService と同じ「表示可能文字列を返す」契約。判定はサービス側）。
3. **非2xx**: 既存 `HfHttp.EnsureSuccessAsync`（本文つき例外）で surface（ログに HF 側理由＝モデル非対応等が残る）。この例外はサービスから送出され、`WorkProcessor.ProcessAsync` の最上位 catch（OCE 以外を捕捉）で受けてユーザーには `Error` を送る。
4. **タイムアウト**: `VisionTimeoutSeconds`（既定 60）を linked CTS で打ち切り。**OCE は `IVisionService` 内で捕捉し `Timeout` 文字列を返す**（ChatService と同一パターン: `catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)`）。外側（ホスト停止）のキャンセルは OCE のまま伝播し最上位 catch が除外＝停止時は通知しない。**ImageEditService/VideoService の「OCE を送出する」パターンは vision では採用しない**（最上位 catch が OCE を除外するため無応答になる。record 26 で spec04 実装時に踏んだ罠の再来を回避）。

## 3. UX / フロー
### 3.1 写真受信時の分岐（`App__VisionEnabled=true` のとき）
- 受信→（spec04 のまま）ワーカーが取得・`MediaStore` 保存。**即・編集アームはしない**。`LastImageId=保存id` / `LastPrompt=null` / `Pending=None` を原子的にセット。
- 返信: 新文言「写真を受け取りました。どうしますか？」＋ QuickReply **[✏️ 編集] [💬 この画像について質問]**（どちらも postback）。
- **[✏️ 編集]** タップ → 既存 `action=edit` postback をそのまま利用（`LastImageId` を確認→`Pending=Edit`→「どう編集しますか？」）。以降は spec04 の編集フロー。
- **[💬 質問]** タップ → 新 `action=ask` postback（`LastImageId` 確認→`Pending=VisionQuestion`→新文言「この画像について何を聞きますか？」）。
- 次の**非コマンドテキスト**が来たら `WorkKind.Vision`（`RefImageId=LastImageId`, `Text=質問`）を enqueue。スラッシュコマンド／モード切替／再生成 postback は **pending をキャンセル**（既存の編集キャンセルと同一規約）。
- **注意（choices 未タップで素テキストを送った場合）**: どちらのボタンも押さずに素メッセージを送ると、`Pending==None` のため**現在モード（chat/image/video）で解釈**され、編集にも質問にもならない（spec04 の「素テキスト＝編集」からの挙動変化。spec04 §2.3 のレース注記と同じく「ボタン選択後に指示/質問を送る」前提）。画像は `LastImageId` に残るので、後から [編集]/[質問] postback を押せば再アームできる。

### 3.2 `App__VisionEnabled=false` のとき（フォールバック＝spec04 の挙動維持）
- 受信→保存→**即・編集アーム**（`Pending=Edit`）＋既存文言「画像を受け取りました。どう編集しますか？」。QuickReply なし。vision 関連の文言・ボタンは一切出さない。

### 3.3 Vision 実行（`HandleVisionAsync`）
- 冪等: `processedEvents.TryMarkNew(WebhookEventId)`（質問テキストイベントの id）。
- 参照画像は TTL キャッシュにあるため期限切れの可能性 → `MediaStore.TryGet(RefImageId)` 失敗時は新文言「その画像はもう使えません。もう一度写真を送ってください」。
- ack: reply トークンで「画像を確認しています… 🔍」（vision は遅めのため ack→push。生成系と同じ方針）。
- `visionService.AnswerAsync(reference.Bytes, reference.ContentType, question, ct)` → 戻り文字列（回答／`Timeout`／`EmptyAnswer` のいずれも表示可能）を **Push**（reply トークンは ack で消費済み）。QuickReply は付けない（初版）。非2xx 例外時は最上位 catch が `Error` を push。
- PublicBaseUrl は**不要**（メディア再配信しない）。

## 4. 状態モデルの変更（`UserStateStore`）
`AwaitingEdit`(bool) を **`PendingAction` enum** に置き換える（2 つのフラグ併存による不整合を避ける）。
- `enum PendingAction { None, Edit, VisionQuestion }`
- `Snapshot` の `bool AwaitingEdit` → `PendingAction Pending`。
- `SetAwaitingEdit(userId, bool)` → `SetPending(userId, PendingAction)`。既存呼び出し（モード切替=None／再生成=None／編集ボタン=Edit）を機械的に置換。
- `SetReceivedImage(userId, imageId, PendingAction pending)`：`LastImageId=imageId` / `LastPrompt=null` / `Pending=pending` を単一ロックで原子的にセット（enabled→`None`、disabled→`Edit`）。
- `MessageDispatcher.HandleTextAsync` の分岐：`Pending==Edit`→ImageEdit（既存）、`Pending==VisionQuestion`→Vision（新）、いずれも先頭で `Pending` を None に戻してから処理（既存の「次の 1 通で解決」規約と同一）。

## 5. 追加/変更する構成要素
- `Ai/VisionService.cs`（新）: `IVisionService.AnswerAsync(byte[] image, string mediaType, string question, ct) → Task<string>`（**表示可能文字列を返す契約＝ChatService に準拠**）+ `HuggingFaceVisionService(HttpClient, IOptions<HuggingFaceOptions>, UserMessages)`。body 構築→POST→`HfHttp.EnsureSuccessAsync`→`choices[0].message.content` 抽出。**空/欠落→`messages.EmptyAnswer` を返す**。timeout=linked CTS、**OCE（内部タイムアウト）→`messages.Timeout` を返す**（§2.4 のガード条件）。非2xx の `HttpRequestException` はそのまま送出（ワーカー最上位 catch→`Error`）。※雛形は **ChatService.cs**（ImageEditService ではない）。
- `Queue/WorkItem.cs`: `WorkKind.Vision` 追加（`Text`=質問、`RefImageId`=参照画像 id を流用）。
- `Queue/WorkProcessor.cs`: `case WorkKind.Vision → HandleVisionAsync`。
- `State/UserStateStore.cs`: §4 の enum 化。
- `Messaging/MessageDispatcher.cs`: `action=ask` postback、`Pending==VisionQuestion` 分岐、pending キャンセル箇所を enum に更新。写真受信ワーカー応答の分岐は WorkProcessor 側。
- `Line/QuickReplyFactory.cs`: `ReceivedImageChoices`（[編集][質問]）。
- `Text/UserMessages.cs`（en/ja 両方）:
  - `ImageReceivedChoose`（「写真を受け取りました。どうしますか？」）
  - `VisionPrompt`（「この画像について何を聞きますか？ 例: ここに何が書いてある？」）
  - `VisionThinking`（「画像を確認しています… 🔍」）
  - `VisionImageExpired`（「その画像はもう使えません。もう一度写真を送ってください。」）
  - `LabelAsk`（「💬 この画像について質問」/「💬 Ask about this」）
  - `Help` に「写真を送ると、編集するか内容を質問できます」を 1 行追記。
  - `EmptyAnswer`/`Timeout`/`Error` は再利用。
- `Program.cs`: `AddHttpClient<IVisionService, HuggingFaceVisionService>`（SSRF 迂回防止は不要＝外部 URL 再取得なしだが、他サービスと揃え `AllowAutoRedirect=false` にしてよい。既定タイムアウトは Infinite でサービス側 CTS 制御）。
- `Configuration/HuggingFaceOptions.cs`・`AppOptions.cs`: §7 のキー追加。

## 6. 設定変更（新規キー）
| キー | 既定 | 説明 |
|---|---|---|
| `HuggingFace__VisionModel` | `Qwen/Qwen2.5-VL-72B-Instruct:ovhcloud` | vision 対応チャットモデル。**provider を pin（`model:provider`）**する（auto ルーティングだと `model_not_supported` になり得るため。§6.1 参照）。operator が差し替え可（pin 先 provider の有効化が必要）。 |
| `HuggingFace__VisionEndpoint` | `https://router.huggingface.co/v1/chat/completions` | OpenAI 互換 chat completions のフル URL（`ChatEndpoint` は SK が `/v1/chat/completions` を付与する base のみのため、direct 呼び出し用に別途フル URL を持つ）。 |
| `HuggingFace__VisionTimeoutSeconds` | `120` | vision 呼び出しの打ち切り（VL のコールドスタート耐性で image-edit と同値）。 |
| `App__VisionEnabled` | `true` | 写真受信時の [質問] 分岐と vision フローの有効化。`false` で spec04 の即・編集挙動に戻す（vision 文言/ボタンを一切出さない）。 |

- `appsettings.json` / `.env.example` / README(EN/JA) / CLAUDE.md に反映。
- **既定 ON の影響（要ドキュメント明記）**: `VisionEnabled=true` により**全ユーザーの写真受信 UX が spec04 から変わる**（即・編集 → [編集]/[質問] choices）。この既定挙動変更を CLAUDE.md/README に明記する（AC13 の既存テスト更新とセット）。
- ※ vision は **fal ではなくチャットと同じ HF Inference**（クレジット消費はチャット並みで fal ほど重くない）ため既定 ON とする。ただし vision 対応モデルの利用には**トークンの provider 権限/availability が必要**で、pin 先 provider が利用不可/混雑の環境では [質問]→**汎用 `Error`**（非2xx を surface）や `Timeout` に落ちる。この失敗時挙動と、対応モデルへの差し替え方法を README に明記。無効化は `App__VisionEnabled=false`。

### 6.1 provider 選択の実運用メモ（実機検証 2026-08-17）
既定 `VisionModel` を **provider pin 付き**（`model:provider`）にする理由と運用手順:
- **pin 必須**: pin なし（`Qwen/Qwen2.5-VL-7B-Instruct` 等）だと router の auto ルーティングが provider を選べず `400 model_not_supported`。→ `model:provider` で明示（`ImageEditModel`/`VideoModel` が fal を明示 pin するのと同じ）。
- **provider の有効化**: pin 先 provider を https://huggingface.co/settings/inference-providers で有効化しておく（fal を有効化するのと同じ）。
- **キャパ/コールド**: 無料/現行枠では provider がキャパ不足で `503 capacity_exhausted` やコールドスタートで遅延することがある（実機で Featherless の Qwen2.5-VL-7B が慢性キャパ不足だった）。→ 別 provider の VL に逃がす。動作確認済み: 既定 **`Qwen/Qwen2.5-VL-72B-Instruct:ovhcloud`**（~10秒で応答・日本語に強い・非 gated）、代替 `zai-org/GLM-4.5V:novita`・`google/gemma-3-27b-it:deepinfra`（gemma は license 同意要）・`Qwen/Qwen2.5-VL-7B-Instruct:featherless-ai`。
- タイムアウトは `VisionTimeoutSeconds`（既定 120）で VL コールドスタートを吸収。
- （見送り）`503 capacity_exhausted` の自動リトライは初版では入れない。慢性キャパ不足には効かず、別 provider 切替が確実なため。

## 7. セキュリティ観点
- **新たな SSRF 面はない**: 送信先は HF router のみ（Bearer 付き）。fal のような結果 URL 再取得（`MediaRefetch`）は発生しない。
- 送る画像は **spec04 で取得済みのユーザー自身の写真**（`MaxIncomingImageBytes` で上限済み）。base64 化で約 +33% になる点は許容（HF 側上限に依存。超過時は非2xx を surface）。
- HF トークンは **`/v1/chat/completions`（router）にのみ**送る（既存チャットと同じ送信先）。ログにトークン・画像本文を出さない（既存規約）。
- 質問テキストはユーザー入力そのまま（プロンプトインジェクションは vision 回答の範囲に閉じ、外部 I/O を誘発しない）。

## 8. 受入基準（テスト可能）
1. `VisionService` は `{model, messages:[{role:user, content:[{type:text,text:question},{type:image_url,image_url:{url:data URI}}]}]}` を `VisionEndpoint` へ **Bearer 付き** POST する。
2. data URI の MIME は保存メディアの content-type（不明時 `image/png`）で、本体は受信画像バイトの base64。
3. 応答 `choices[0].message.content` を回答として返す。**空/欠落は `IVisionService` が `EmptyAnswer` 文字列を返す**（判定はサービス側）。
4. 非2xx は本文つき例外（`HfHttp.EnsureSuccessAsync`）でサービスから送出→`WorkProcessor` 最上位 catch（OCE 以外）が受け、ユーザーには `Error`。
5. `VisionTimeoutSeconds` 経過で内部タイムアウト→**`IVisionService` が OCE を捕捉し `Timeout` 文字列を返す**（最上位 catch は OCE を除外するため、サービスが送出すると無応答になる＝サービス側変換が必須。ChatService と同一）。外側キャンセル（停止）は OCE 伝播＝通知しない。
6. `VisionEnabled=true`: 写真受信→保存→`Pending=None`＋`ImageReceivedChoose`＋QuickReply[編集,質問]。編集アームはしない。
7. `VisionEnabled=false`: 写真受信→保存→`Pending=Edit`＋`ImageReceived`（spec04 の挙動不変）。QuickReply/ vision 文言は出ない。
8. `action=ask` postback は `LastImageId` があれば `Pending=VisionQuestion`＋`VisionPrompt`、無ければ既存 `EditNoImage` 相当。
9. `Pending=VisionQuestion` の状態で非コマンドテキスト→`WorkKind.Vision`（`RefImageId=LastImageId`, `Text=質問`）を enqueue。スラッシュコマンド／モード切替 postback／再生成 postback は pending をキャンセル。
10. `HandleVisionAsync`: `RefImageId` が TTL 失効なら `VisionImageExpired`；成功時は `VisionThinking` を reply→回答を push。
11. `WebhookEventId` による冪等（同一質問イベント再配信で二重呼び出ししない）。
12. 既定値（VisionModel / VisionEndpoint / VisionTimeoutSeconds / VisionEnabled）がドキュメント・`appsettings.json` と一致。
13. `AwaitingEdit`→`Pending` enum 置換の回帰なし（既存の編集アーム/キャンセル規約は不変）。**ただし既定 `VisionEnabled=true` により受信写真の既定 UX が変わる**ため、spec04 の受信フロー既存テスト（受信直後に編集アーム＋`ImageReceived` を検証するもの）は **(a) `VisionEnabled=false` に固定して現行挙動を検証、かつ (b) 新 UX（`VisionEnabled=true`→`Pending=None`＋`ImageReceivedChoose`＋choices）を検証する新テストを追加**、の両方へ更新する（＝「素の enum 化のみで緑」ではない）。

## 9. 決定事項（2026-08-17）
- [x] vision は **HF router `/v1/chat/completions`（OpenAI 互換）を HttpClient 直叩き**（SK HF コネクタは vision 不確実のため不採用）。
- [x] 対象は**ユーザー送信写真のみ**。生成画像への質問は後続。
- [x] **ワンショット**（会話履歴と独立）。マルチターンは後続。
- [x] 受信写真時に **QuickReply で [編集]/[質問] 分岐**（`VisionEnabled=false` で spec04 の即・編集に戻る）。
- [x] `AwaitingEdit`(bool) を **`PendingAction` enum** 化（Edit / VisionQuestion を排他管理）。
- [x] 既定 `App__VisionEnabled=true`、既定 `VisionModel=Qwen/Qwen2.5-VL-7B-Instruct`（operator 差し替え可・provider 依存）。既定 ON で受信 UX が変わることを文書化＋既存テスト更新。
- [x] **エラー処理契約は ChatService に一本化**: `IVisionService.AnswerAsync` は表示可能文字列を返し、OCE→`Timeout`・空→`EmptyAnswer` を**サービス側で**変換。非2xx は例外送出（最上位 catch→`Error`）。ImageEditService/VideoService の OCE 送出パターンは不採用（record 26 の罠回避）。
- [x] 検証は**ユニットテスト + 4 ゲート**（実機 HF vision E2E は operator に委ねる。ワイヤ形式は OpenAI 互換で確定）。

## 10. 参考
- `docs/specs/04-user-image-edit.md`（受信写真の取得・保存・`AwaitingEdit`）。
- **雛形の役割分担**: エラー処理契約は `Ai/ChatService.cs`（OCE→Timeout・空→EmptyAnswer をサービス側で文字列化）、data URI 生成/HttpClient 直叩きは `Ai/ImageEditService.cs` を参照。`Ai/HfHttp.cs`（EnsureSuccess＋本文）。
- OpenAI 互換 chat completions のマルチモーダル content（`type:image_url`）＝HF router が受け付ける形式（既存チャットが同エンドポイントを SK 経由で使用）。
