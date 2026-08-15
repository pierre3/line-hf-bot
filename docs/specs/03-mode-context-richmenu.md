# 仕様: モードコンテキスト + リッチメニュー + 画像セッション + i18n

- 状態: 実装済み（3a 4ゲート全PASS=15〜18 / 3b 実装済み・ゲート予定）。3b の img2img payload は `{inputs=base64, parameters.prompt}`（HF image-to-image）に確定。
- 対象: 拡張フェーズ / 対話モデルの再設計（モード状態・リッチメニュー・画像セッション・多言語）
- 関連: `docs/specs/01-line-hf-bot.md`、`docs/specs/02-image-provider-integration.md`（image-to-image の payload 分岐を再利用）、`scripts/richmenu/`（メニュー画像生成）
- 依存: **3b（画像編集=image-to-image）は spec 02 実装後**。3a は spec 02 非依存。

## 1. 目的 / スコープ
現状は毎回 `/image <prompt>` 等のコマンド入力が必要で、QuickReply もコマンドのショートカット止まりで UX 改善が薄い。
本仕様で **per-user のモード状態**を導入し、**素のメッセージを現在モードで解釈**する対話モデルへ再設計する。
モード切替は **リッチメニュー**（常時表示のタブ）で行い、生成結果には **画像セッション操作（🔄再生成・✏️編集）** を付ける。
あわせて配布(Docker Hub)向けに **英語デフォルト＋日本語切替**の i18n を入れる。

### スコープ外
- 会話をまたいだ永続状態（DB）。状態はメモリ保持（再起動で既定へ）。
- per-user の言語切替 UI（初版は **アプリ設定 `App__Locale`** による運用者選択。ユーザー個別切替は将来）。
- グループ/複数人トークでのモード共有（1:1 前提。group/room は既定モードのみ）。
- 動画セッション操作（🔄/✏️ は画像のみ。動画は `App__VideoEnabled` 準拠のまま）。

## 2. 機能要件

### 2.1 モード状態（3a）
- **`UserStateStore`（新規, メモリ）**: userId → `{ Mode(chat|image|video), AwaitingEdit(bool), LastPrompt, LastImageId }`。`ChatHistoryStore` と同型（**per-user・グローバル上限なし・`/reset` でクリア・再起動で消失**）。多数 userId によるメモリ増加対策は既存 `ChatHistoryStore` と同じ扱い（別途の全体上限は本仕様の対象外）。
- **既定モード = chat**。未登録ユーザは chat。
- **生成後もモード保持**（明示切替まで維持）。連続生成しやすさを優先。
- **素メッセージの解釈**: 現在モードに従い chat/image/video の生成へ。`/image` `/video` `/reset` `/help` は**明示上書き**として従来どおり有効（モードは変えない一時実行）。
- **`/reset`**: チャット履歴クリア＋**モードを既定(chat)へ＋画像セッション(LastPrompt/LastImageId/AwaitingEdit)クリア**。
- 動画モードは `App__VideoEnabled` を尊重（false 時は素メッセージでも「準備中」を返す）。

### 2.2 リッチメニュー（3a）
- SDK の RichMenu API（作成・画像アップロード(blob)・alias・デフォルト設定）を使用。新パッケージ不要。
- **3メニュー方式**: chat/image/video の各アクティブ状態を表す3枚（`scripts/richmenu/out/<locale>/richmenu-{chat,image,video}.png`, 2500×843）。各メニューに3タブのタップ領域。
- タブのタップ = **`richmenuswitch` アクション**（クライアント即時切替＋postback 送出）。alias `richmenu-chat|image|video` を各 richMenuId に割当。
- postback `data`（例 `action=mode&value=image`）で **サーバのモード状態を更新**（§2.4）。
- **デフォルトリッチメニュー = chat**（`setDefaultRichMenu`）。
- **provisioning**: アプリ起動時に**冪等**実行（alias 存在で既存判定 → 無ければ作成＋画像アップロード＋alias 登録＋デフォルト設定）。`App__RichMenuEnabled`（既定 true）で無効化可能。使用画像は `App__Locale` のロケール分。
- メニュー画像は**コンテナに同梱**する。生成物 `scripts/richmenu/out/<locale>/*.png` を **`LineHfBot/Assets/richmenu/<locale>/*.png` にコピー**し、csproj に `<Content Include="Assets/richmenu/**" CopyToOutputDirectory="PreserveNewest" />`（相当）を追加して publish / Docker イメージに含める（`LineHfBot/Assets/` は新規作成）。再生成は `scripts/richmenu/build-richmenu-images.ps1 -Locale en|ja`。

### 2.3 画像セッション操作（3a: 再生成 / 3b: 編集）
- 画像生成の結果メッセージに **QuickReply** を付与: `[🔄 再生成] [✏️ 編集] [💬 チャットへ]`（すべて postback）。
- **🔄 再生成（3a）**: `LastPrompt` を text-to-image で再実行（別シード＝異なる出力）。spec 02 非依存。
- **✏️ 編集（3b）**: postback で `AwaitingEdit=true` に。**次の非コマンドテキストを編集指示**として、`LastImageId` の画像＋指示を **image-to-image（`Qwen/Qwen-Image-Edit`）** で処理。**送信 payload（参照画像＋編集指示）は 3b で新規定義**する。spec 02 から流用するのは**応答形式判定（生バイト/JSON-URL）と SSRF allowlist のみ**であり、text-to-image の `{inputs}` 送信形式は img2img には使えない（**spec 02 実装後**）。
- `AwaitingEdit` 中に モード切替 / スラッシュコマンド が来たらキャンセル。
- `💬 チャットへ`: モードを chat に戻す（リッチメニューも chat へ）。
- **既存 `QuickReplies.Default` の去就**: モード切替はリッチメニューが担うため、既存の「画像/動画/リセット/使い方」message-action QuickReply（`Line/QuickReplies.cs`）は**廃止**（役割重複回避）。画像結果は本節の `[🔄][✏️][💬]` postback QR に置換。chat 応答は QR なし、video 結果は `[💬 チャットへ]` のみ付与。

### 2.4 PostbackEvent 処理（3a）
- 現在 `MessageEvent` のみ処理。**`PostbackEvent` を受信・振り分け**（`MessageDispatcher` 拡張 or 併設ハンドラ）。
- `data` の action で分岐: `mode`（モード切替）/ `regen`（再生成）/ `edit`（編集開始）。
- 生成系（regen/edit）は既存の非同期キュー経由。モード切替は即時（状態更新＋必要なら短い ack）。

### 2.5 i18n（3a）
- **`App__Locale`（en|ja, 既定 en）**。配布デフォルトは英語。
- `UserMessages` を**ロケール対応**に（en/ja の2セット。現行 日本語文言は ja として維持し、en を新規追加）。
- リッチメニュー provisioning は `App__Locale` のロケール画像を使用（§2.2）。
- ログ/コメントは英語のまま（[[language-and-docs-conventions]] 準拠）。ユーザー向け文言のみロケール切替。

## 3. 設定（追加）
> ※ **3b の編集モデル/エンドポイント既定は後日 spec05 で fal-ai に変更**（`hf-inference` は image-to-image 非対応と判明したため）。下表の `Qwen/Qwen-Image-Edit` / `hf-inference/models/{model}` は当時の記録。**現行の既定は `docs/specs/05-image-edit-fal-provider.md` §4 を参照**。

| キー | 既定 | 説明 |
|---|---|---|
| `App__Locale` | `en` | ユーザー向け文言＋リッチメニュー画像のロケール（en/ja） |
| `App__RichMenuEnabled` | `true` | 起動時のリッチメニュー provisioning を行うか |
| `HuggingFace__ImageEditModel` | `Qwen/Qwen-Image-Edit` | image-to-image（編集）モデル。3b |
| `HuggingFace__ImageEditEndpoint` | `https://router.huggingface.co/hf-inference/models/{model}` | 編集エンドポイント（`{model}` 置換、プロバイダ依存）。3b |
| `HuggingFace__ImageEditTimeoutSeconds` | `120` | 編集タイムアウト。3b |

`.env.example` / README(EN/JA) / CLAUDE.md の設定表に追記。
加えて **CLAUDE.md の言語ルール本文を改訂**する: 現行「エンドユーザー向け文言（LINE 返信など）は日本語」を、**「ユーザー向け文言の既定は `App__Locale` 依存（配布既定 en / ja 切替可）」**へ拡張（[[language-and-docs-conventions]] の配布時方針を本文に反映）。
さらに **CLAUDE.md「アーキテクチャ要点」のモード/QuickReply 記述も改訂**する: 現行「通常テキスト=チャット」「結果に QuickReply を付与」を、本仕様のモードコンテキスト（現在モードで素メッセージを解釈）＋リッチメニュー切替＋画像結果の `[🔄][✏️][💬]` に合わせて更新（3a 実装後に記述が設計と食い違わないようにする）。

## 4. 受入基準（テスト可能）
1. 既定モードは chat。未登録ユーザの素テキスト → チャット補完。
2. モードタブ postback（`mode=image`）→ 当該ユーザのモードが image に更新される（**サーバ状態更新は postback 受信で検証可**）。表示リッチメニューの image ハイライト切替は alias によるクライアント側効果のため**実機確認**で検証（`line-webhook-test` では不可）。
3. image モードで素テキスト「dog」→ text-to-image 生成。結果に QuickReply `[🔄][✏️][💬]` が付く。
4. 🔄再生成 postback → `LastPrompt` で再実行し新規画像を配信（3a、spec02非依存）。
5. ✏️編集 postback → `AwaitingEdit=true`。次テキストが編集指示として image-to-image（Qwen-Image-Edit）で処理される（**3b/ spec02後**）。
6. スラッシュコマンドは現在モードに関係なく従来動作（モードは変えない）。
7. `/reset` でチャット履歴クリア＋モード=chat＋画像セッションクリア。
8. リッチメニュー provisioning は冪等（再起動で重複作成しない。alias で既存判定）。
9. `App__Locale=en` で英語文言＋en画像、`=ja` で日本語文言＋ja画像に切替わる。
10. 動画モードは `App__VideoEnabled=false` で素テキストでも「準備中」を返す。
11. モード/セッション状態は per-user メモリで、再起動後は既定に戻る。
12. `PostbackEvent` が処理される（従来 MessageEvent のみ→回帰なし）。

## 5. 実装フェーズ
- **3a（本命・spec02非依存）**: UserStateStore、素メッセージのモード解釈、PostbackEvent 処理、リッチメニュー provisioning（en/ja）、🔄再生成、i18n（UserMessages en/ja、App__Locale）、/reset 統合。
- **3b（spec02後）**: ✏️編集 = image-to-image（Qwen-Image-Edit）。spec02 の生バイト/JSON-URL 分岐・SSRF allowlist を再利用。

## 6. 決定事項（旧・未解決点／2026-08-15 確定）
- [x] 既定モード=chat／生成後もモード保持／状態はメモリ（再起動で既定へ）。
- [x] 画像セッション（🔄再生成・✏️編集）を提供。編集モデル既定 `Qwen/Qwen-Image-Edit`。
- [x] リッチメニュー採用（3メニュー＋alias richmenuswitch）。画像は自前生成、コンテナ同梱、起動時冪等 provisioning、`App__RichMenuEnabled` で無効化可。
- [x] i18n はアプリ設定 `App__Locale`（en 既定/ja）。per-user 切替は将来。
- [x] 編集UX: ✏️で AwaitingEdit にし、次の非コマンドテキストを編集指示に。モード切替/コマンドでキャンセル。
- [x] 実装順: **spec 03(3a) → spec 02 → 3b(編集)**。
- [x] 3b の spec02 依存範囲は「応答分岐＋SSRF のみ」。img2img 送信 payload は 3b で新規定義（text-to-image `{inputs}` は流用不可）。
- [x] i18n の en 既定化に伴い **CLAUDE.md 言語ルール本文を改訂**（ユーザー文言の既定を locale 依存へ）。
- [x] モード切替はリッチメニューへ集約し、既存 `QuickReplies.Default`（message-action）は**廃止**。画像結果は `[🔄][✏️][💬]` postback QR に置換。
- [x] UserStateStore は `ChatHistoryStore` と同型（per-user/上限なし/reset でクリア/再起動で消失）。全体メモリ上限は本仕様対象外。
- [x] メニュー画像は `out/<locale>` → `LineHfBot/Assets/richmenu/<locale>` にコピーし csproj Content で publish 同梱。

## 7. 参考（実装の流用元 / 連携）
- 状態ストア: `ChatHistoryStore`（per-user メモリ、上限管理）。
- 生成/配信: `WorkProcessor` / `MediaStore` / `LineMessenger`（QuickReply は実装済み。postback アクションは新規）。
- 編集(3b)の HF 呼び出し: spec 02 の `ImageService` 応答分岐＋SSRF allowlist を再利用。
- メニュー画像: `scripts/richmenu/build-richmenu-images.ps1`（en/ja, 2500×843, tabハイライト）。
