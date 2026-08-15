# 実装レビュー — ユーザー画像の受信 → 画像編集（image-to-image / spec04） (2026-08-15)
Verdict: PASS（初回 FAIL → 修正 → 再判定で PASS）
委譲分析: dotnet-claude-kit:code-review（Roslyn MCP 接続あり: detect_antipatterns / get_diagnostics）
対象: git diff a012f10..HEAD（ブランチ feat/user-image-edit）
経緯: 4a10d5a（初回=FAIL） → 8b15e79（Major#1 修正=PASS）

## 経緯サマリ
- 初回 4a10d5a: Major#1（AC#7「タイムアウト → ImageReceiveFailed」未達）で FAIL。
  取得タイムアウトの OperationCanceledException が HandleReceiveImageAsync/ProcessAsync の
  `when (ex is not OperationCanceledException)` を両方すり抜け、GenerationWorker でログ止まり＝
  ユーザー無応答（ReceiveImage は事前 ack を出さないため体感が特に悪い）。
- 修正 8b15e79: FetchImageAsync が自前タイムアウトを検出し TimeoutException（非OCE）へ変換。
  Worker 層テスト（指摘#3）も追加。再判定で PASS。

## チェックリスト / 指摘
| # | 重大度 | 箇所 | 問題 | 対応状況 |
|---|---|---|---|---|
| 1 | Major | `Line/LineContentService.cs:42-68` → `Queue/WorkProcessor.cs:145` | AC#7 タイムアウト時 ImageReceiveFailed 未達（OCE が両 catch フィルタをすり抜けユーザー無応答）。 | 解消（8b15e79）。FetchImageAsync が `when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)` でフェッチ・タイムアウトのみ捕捉し TimeoutException（非OCE）へ変換 → 既存 catch が ImageReceiveFailed を返す。アプリ停止由来 OCE はそのまま伝播。ChatService.cs:39 と同一手法。 |
| 2 | Minor | `Queue/WorkProcessor.cs:129` | idempotency の TryMarkNew を取得前に実施。取得失敗でも処理済みマークされ再配信で再取得しない。webhook 即200のため worker 失敗での再配信は基本無く、既存 PrepareMediaAsync と同一。 | 受容可（現状維持）。AC#10 の二重取得防止は満たす。 |
| 3 | Minor | `LineHfBot.Tests`（Worker 層） | HandleReceiveImageAsync の直接テスト欠如。 | 解消（8b15e79）。WorkProcessorReceiveImageTests 追加＝成功／ImageTooLarge・状態不変／Timeout→ImageReceiveFailed・状態不変／一般失敗→ImageReceiveFailed。 |
| 4 | Minor | `Queue/WorkProcessor.cs:76` | 二次通知の empty-catch（AP007 error）。コメント付き意図的ベストエフォート、既ゲート受容済み。 | 受容可。 |

## 受入基準（§4）照合
1 受信→取得→保存→LastImageId/AwaitingEdit/LastPrompt=null・ImageReceived: PASS（SetReceivedImage 原子更新／WorkProcessorReceiveImageTests.Success）
2 続く非コマンドテキスト→img2img: PASS（既存 AwaitingEdit 分岐再利用、変更なし）
3 モード非依存: PASS（HandleImageReceiveAsync はモード非参照）
4 上限超過→ImageTooLarge・状態不変: PASS（ReadCappedAsync→ImageTooLargeException、TooLarge テスト）
5 external→ImageSourceUnsupported・URL非取得・状態不変: PASS（Dispatcher で enqueue 前に拒否、テスト有）
6 AwaitingEdit 中の新画像→上書き＋再プロンプト: PASS（同経路・SetReceivedImage 上書き）
7 取得失敗（APIエラー/タイムアウト）→ImageReceiveFailed・状態不変・非握り潰し: PASS（8b15e79 でタイムアウト経路も達成、テスト有）
8 i18n en/ja: PASS（4文言＋help 1行）
9 webhook 即200: PASS（enqueue のみ、取得は worker）
10 idempotency: PASS（ProcessedEventStore.TryMarkNew）
11 SetReceivedImage 単一ロック原子性: PASS（UserStateStore.cs:83-92、テスト有）
12 既存回帰なし: PASS（57件緑＝既存53＋新規4、diagnostics 0/0）

## 重点確認結果
- correctness: PASS。状態遷移・原子性・冪等・エラー処理いずれも達成。失敗経路は Save/SetReceivedImage 未到達で状態不変。タイムアウト判定フィルタはアプリ停止 OCE と誤認しない。
- 並行性: PASS。ConcurrentDictionary＋per-user lock、新 SetReceivedImage も既存パターン踏襲。
- リソース保護: PASS。ReadCappedAsync は書込前判定でメモリ有界（~maxBytes+81920）、cts/stream とも using で dispose。
- 規約整合: PASS。modern C#、DI は ILineMessenger と同型（captive 依存なし）、コメント/ログ英語・文言 en/ja、秘密情報非露出、external 拒否で SSRF 面なし。
- MCP: get_diagnostics 0/0/0（LineContentService.cs 個別も 0/0）、detect_antipatterns 新規指摘なし（既存 catch 系のみ）。

## 判定理由
Blocker 0・Major 0。初回 FAIL の Major#1（AC#7 タイムアウト非通知）は 8b15e79 で解消され、
フェッチ・タイムアウトは TimeoutException 経由で ImageReceiveFailed をユーザー通知（状態不変）する
ことをコードとテストで実証。指摘#3 の Worker 層テストも追加され、残りは受容可の Minor 2件のみ。
受入基準 12 項目すべて充足、diagnostics クリーン、57件緑。次工程（セキュリティレビュー）へ進行可。
