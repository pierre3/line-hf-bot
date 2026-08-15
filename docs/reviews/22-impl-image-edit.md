# 実装レビュー — 画像編集（image-to-image / Qwen-Image-Edit / spec03 3b） (2026-08-15)
Verdict: PASS
委譲分析: dotnet-claude-kit:code-review（Roslyn MCP 接続あり: detect_antipatterns / get_diagnostics）

## チェックリスト / 指摘
| # | 重大度 | 箇所 | 問題 | 必要な対応 |
|---|---|---|---|---|
| 1 | Minor | `State/UserStateStore.cs` / `Messaging/MessageDispatcher.cs:50-67` | AwaitingEdit の消費が「読み取り（Get）→ SetAwaitingEdit(false)」の非アトミックな test-and-clear。同一ユーザの webhook が並行到達した場合に理論上フラグを二重消費し得る（各 UserState への lock はあるが複合操作は跨がない）。LINE は通常ユーザ単位で直列配信するため実害は低い。 | 受容可。厳密化するなら「clear して直前値を返す」アトミックメソッド（例: `TryConsumeAwaitingEdit`）に集約。次工程に影響なし。 |
| 2 | Minor | `Queue/WorkProcessor.cs:72` | 二次通知失敗を握りつぶす `catch { /* ignore */ }`（antipattern AP007 が error 判定）。ただしコメント付きの意図的なベストエフォートで、主たる失敗は 68 行の `catch` でログ＋ユーザ通知済み。spec02 以前からの既存パターンで既ゲートで受容済み。 | 受容可。現状維持で問題なし。 |
| 3 | Minor | `LineHfBot.Tests`（Dispatcher 層） | 新規サービス/状態は手厚くテストされる一方、`MessageDispatcher` の AwaitingEdit 消費・スラッシュでのキャンセル経路の直接ユニットテストは無い（状態遷移は UserStateStore テストで間接的に担保）。 | 提案。ディスパッチャの分岐テストを追加すると回帰検知が強くなる（任意）。 |

## 重点確認結果（依頼 5 項目）
1. **img2img payload と共有化の回帰** — PASS。`ImageEditService` は `{ inputs: base64(referenceImage), parameters: { prompt: instruction } }` を送信（`ImageEditServiceTests.Sends_base64_image_and_prompt_payload` が base64 一致・`parameters.prompt` 一致・URL・Bearer を検証）。`ImageService`/`VideoService` は payload（`{inputs}`）不変のまま応答二分岐のみ `MediaResponse.ReadAsync` に委譲。既定 Content-Type は image/png・video/mp4・image/png と各サービスで正しく指定。JSON-URL 再取得は 3 サービスとも同一の SSRF ガード（allowlist ラベル境界・no-auth・timeout）を共有し、`AllowAutoRedirect=false` を全 typed client に設定（`Program.cs:53-61`）。ビルド 0 警告/0 エラー、EXIT=0（既存 36＋新規 9）。
2. **AwaitingEdit の状態遷移** — PASS（穴なし、上記 Minor#1 の理論的競合のみ）。消費: フラグ検出時に即 clear→非スラッシュなら `snapshot.LastImageId` をピン留めして `WorkKind.ImageEdit` を enqueue。キャンセル: スラッシュ入力はフラグ clear 後に通常コマンド処理へフォールスルー（AC#6 準拠）／`mode`・`regen` postback も `SetAwaitingEdit(false)`。クリア: `/reset`→`WorkProcessor` の `userState.Reset`（エントリ削除で AwaitingEdit も消滅、AC#7 準拠）。RefImageId は WorkItem にピン留めされ、dispatch 後に LastImageId が変わっても参照は固定。別ユーザ混線なし（状態は userId キー）。
3. **参照画像 TTL 失効** — PASS。`HandleImageEditAsync` が `RefImageId` 空 or `MediaStore.TryGet` 失敗を検出し `messages.EditImageExpired` をユーザ通知（握りつぶさない）。en/ja 双方に文言あり。
4. **スレッド安全** — PASS。`UserStateStore` は `ConcurrentDictionary` ＋ per-user オブジェクト lock。新メソッド `SetLastImageId`/`SetAwaitingEdit` も既存と同じ `GetOrAdd(static factory)` ＋ `lock(s)` パターンを踏襲。全フィールドアクセスが lock 下で一貫。
5. **外部 I/O 失敗通知・秘密情報非露出** — PASS。`WorkProcessor.ProcessAsync` の try/catch が失敗をログ＋`messages.Error` で通知。`HfHttp.EnsureSuccessAsync` が失敗時に throw→捕捉→通知。ApiKey は Bearer ヘッダのみで使用され、ログは構造化パラメータ（userId/kind/eventId）のみでトークン非出力。

## 判定理由
Blocker 0・Major 0。差し戻すべき重大事項なし。MCP get_diagnostics はソリューション全体で 0 error/0 warning/0 info、detect_antipatterns の新規指摘は無く（新規 2 ファイルは検出リストに不在）、既存の catch 系パターンのみ。img2img の payload はテストで正しさを実証、共有 `MediaResponse` リファクタは payload 不変のため image/video に回帰なし。AwaitingEdit の消費・キャンセル・/reset クリアは仕様 AC#5/6/7 と整合し、TTL 失効はユーザ通知される。残るのは Minor 3 件（並行 test-and-clear の理論的競合／既存の意図的 empty-catch／Dispatcher の直接テスト欠如）で、いずれも次工程（セキュリティレビュー）に影響しないため PASS。
