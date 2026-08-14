# 仕様: LINE × Hugging Face AI チャットボット (v1)

- 状態: 承認済み（仕様ゲート PASS / 2026-08-14。指摘 #1・#2 反映済み）
- 対象: 初期構築（MVP）
- 関連: `CLAUDE.md`、実装プラン `~/.claude/plans/foamy-fluttering-haven.md`

## 1. 目的 / スコープ
LINE から Hugging Face のモデルを使い、**テキストチャット・画像生成・動画生成**ができるボットを
ASP.NET (.NET 10, Minimal API) で実装する。個人／小規模利用向けで、ローカル Docker 実行＋トンネル公開で
手軽に使えることを主眼とする（クラウドホストも可能）。

### スコープ外（v1では作らない）
- 音声・ファイル・スタンプ入力への対応（テキストメッセージのみ扱う）
- 永続ストレージ（DB）、マルチインスタンス水平スケール
- 課金・レート制限のユーザー別管理（グローバルな流量制御のみ）

## 2. 機能要件

### 2.1 モードとコマンド
- 通常テキスト → **チャット**（会話履歴を保持）
- `/image <prompt>` → **画像生成**
- `/video <prompt>` → **動画生成**
- `/reset` → 会話履歴クリア
- `/help` → 使い方表示
- 生成結果メッセージに **QuickReply**（「🔄 再生成」「💬 チャットに戻る」等）を付与

### 2.2 チャット
- LINE userId 毎に会話履歴をメモリ保持（件数上限あり、既定20往復程度）。
- Semantic Kernel の HuggingFace チャット補完で応答生成。
- 応答は Push で返す（§3 の非同期フロー）。

### 2.3 画像 / 動画生成
- HF Inference Providers へ HTTP 呼び出し（生バイト取得）。SK の KernelFunction/Plugin としてラップ。
- 取得バイトを **メモリ内 TTL キャッシュ**（既定10分）に保存し `GET /media/{id}` で配信。
- LINE へは公開 URL（`${PublicBaseUrl}/media/{id}`）を持つ `ImageMessage` / `VideoMessage` を Push。
- 動画は `previewImageUrl` が必須のため、簡易プレビュー（固定プレースホルダ画像で可）を用意。

## 3. 非同期処理アーキテクチャ（確定）
LINE 公式が Webhook の非同期処理を推奨。生成は数秒〜数分かかり reply トークンは短命なため、以下を採用:

```
POST /webhook → 署名検証 → (有効なら) enqueue → 即 2xx 応答
                                   │
                     BoundedChannel<WorkItem>(容量100)
                                   │  worker×2 (BackgroundService)
                                   ▼
                 種別処理(chat / image / video) → Push で結果送信
```

### 3.1 確定した設計判断
- **署名検証は同期**（enqueue 前）。失敗は 401/400 で弾き、enqueue しない。
- **並列モデル**: 単一チャネル + **worker 2本**（head-of-line blocking を緩和しつつ HF 同時負荷を抑制）。
- **バックプレッシャ**: `BoundedChannel(100)`。満杯時は **drop** し「混雑しています。少し待って再送してください」を返す（無限待ちで LINE 側タイムアウトを避ける）。
- **冪等性**: `webhookEventId` を TTL 付きで記録し、**生成系（image/video）のみ**重複をスキップ（LINE の再送 `deliveryContext.isRedelivery` 対策）。チャットは対象外。
- **例外隔離**: worker ループは 1 件ごとに try/catch。失敗はユーザーへ Push 通知し、ループは継続。
- **DI スコープ**: `BackgroundService`（singleton）内で `IServiceScopeFactory` により **work item ごとにスコープ生成**。
- **ack と push 節約**: 即時の「生成中…⏳」は **reply トークン**で無料 ack、最終結果のみ **Push**。満杯 drop 時の「混雑しています」通知も reply トークンで返す。
- **外部呼び出しタイムアウト**: HF 呼び出しは種別ごとに上限タイムアウトを設ける（既定 chat 60s / image 120s / video 300s、設定可）。超過時は当該 worker を解放し、ユーザーへ失敗を Push 通知（`HttpClient`/resilience レイヤで担保）。長時間応答で worker が占有され続ける事態を防ぐ。
- **冪等スキップの通知**: 再送により生成系がスキップされた場合はユーザー無通知（意図的挙動）。
- **Graceful shutdown**: `stoppingToken` を尊重。処理中の生成は停止時 abandon 前提（必要なら `ShutdownTimeout` 延長）。
- **永続性**: in-memory のため**クラッシュ／再起動でキュー内容と処理中は消失（at-most-once）**。個人利用として許容。将来 Redis/Storage Queue に差替可能な抽象（`IWorkQueue`）で実装。

## 4. メディア配信
- `GET /media/{id}` は TTL キャッシュから `contentType` 付きで返す。無ければ 404。
- id は推測困難な値（GUID 等）とし、横断アクセスを防ぐ。
- `PublicBaseUrl` はトンネル URL 等を指す設定値。**LINE の画像/動画 URL は HTTPS 必須**のため `PublicBaseUrl` は https スキームであること。

## 5. 設定（すべて環境変数 / appsettings）
`Line__ChannelSecret`, `Line__ChannelAccessToken`, `HuggingFace__ApiKey`,
`HuggingFace__ChatModel` / `ImageModel` / `VideoModel`, `App__PublicBaseUrl`, `App__MediaTtlMinutes`,
`Queue__Capacity`(既定100), `Queue__Workers`(既定2), `Chat__MaxHistory`(既定20),
`HuggingFace__ChatTimeoutSeconds`(既定60) / `ImageTimeoutSeconds`(既定120) / `VideoTimeoutSeconds`(既定300)。
- 秘密情報はコミット禁止（`.env` は gitignore、`.env.example` のみ管理）。

## 6. 非機能要件
- **セキュリティ**: Webhook 署名検証必須。秘密情報をログ／例外／レスポンスに出さない。`/media/{id}` の id 推測防止。
- **エラー処理**: 外部 I/O（LINE / HF）失敗はユーザーに日本語で通知。握りつぶさない。
- **可観測性**: 主要イベント（受信・enqueue・drop・生成成功/失敗・Push）を構造化ログ。
- **健全性**: `GET /health` が 200。

## 7. 受入基準（検証可能）
1. **署名検証**: 正しい署名の Webhook は 2xx、署名不正は 401 を返す。（`line-webhook-test` スキルで確認）
2. **即応**: Webhook は生成完了を待たず即 2xx を返す（生成中もブロックしない）。
3. **チャット**: 「こんにちは」→ 数秒内に AI 応答が Push される。`/reset` で履歴クリア、`/help` 表示。
4. **画像**: `/image 猫` → 生成画像が `ImageMessage` として LINE に届き、`/media/{id}` が画像バイトを返す。
5. **動画**: `/video 走る猫` → 生成動画が `VideoMessage`（preview 付き）として届く。
6. **混雑時**: キュー満杯時は「混雑しています…」が返り、サーバは落ちない。
7. **冪等性**: 同一 `webhookEventId` の再送で画像/動画が二重生成されない。
8. **例外隔離**: 1 件の生成失敗後も後続リクエストが処理され続ける（サービス停止しない）。
9. **秘密情報**: ログ・`.env.example`・レスポンスにトークン等が出ない。
10. **health**: `GET /health` が 200。
11. **QuickReply**: 生成結果メッセージに再生成／チャット復帰の QuickReply が付く。
12. **履歴上限**: 会話履歴が上限（既定20往復）を超えると古いものから破棄される。
13. **メディア404**: 存在しない／期限切れの `/media/{id}` は 404 を返す。
14. **タイムアウト**: HF 呼び出しが上限を超えた場合、worker が解放され、ユーザーへ失敗が通知される。

## 8. 検証方法（end-to-end）
- `dotnet build` でコンパイル。`GET /health` 200。
- `line-webhook-test` スキルで署名付きイベントを投入（正=2xx / 不正=401）。
- HF トークン設定後、`/image`・`/video`・チャットの各イベントで Push 到達と `/media/{id}` を確認。
- `docker compose up` → Dev トンネル経由で実機 LINE から chat / `/image` / `/video` を送り実機確認。

## 9. 未解決点
なし（§3 の並列・満杯時・冪等性を含め確定済み）。
