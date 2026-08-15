# 実装レビュー — 全体統合点検（LineHfBot 全体）(2026-08-15)
Verdict: PASS
委譲分析: dotnet-claude-kit:code-review（Roslyn MCP は本環境で未接続のため、スキルの手動レビュー優先順＋`dotnet build` 診断でフォールバック実施。確度はやや低下）

## 前提・スコープ
- 対象: `Program.cs`, `Ai/`, `Chat/`, `Configuration/`, `Line/`, `Media/`, `Messaging/`, `Queue/`, `Text/`（全ソース20ファイル）。差分ではなく全体の統合点検。
- ビルド: `dotnet build`（Debug）成功、**警告0 / エラー0**。
- 委譲エンジンの注記: Roslyn MCP（`detect_antipatterns` / `get_diagnostics` 等）が本セッションで利用不可のため、code-review スキルの「手動レビュー優先順（データ/セキュリティ/並行性/外部連携/正当性）」に沿って実施。アンチパターン自動検出が無いぶん、判定確度は通常より低い。

## 指摘
| # | 重大度 | 箇所(file:line) | 問題 | 必要な対応 |
|---|---|---|---|---|
| 1 | Minor | Queue/ProcessedEventStore.cs:22-28 | `TryGetValue`→`Set` が非アトミックで TOCTOU。同一 eventId を複数 worker が同時処理すると重複が通過し得る | 実害は低（LINE 再配信は時間差）。厳密化するなら `GetOrCreate`/ロックで原子化。次工程に影響なし |
| 2 | Minor | Chat/ChatHistoryStore.cs:42-51 | `Append` が `GetOrAdd` 後にロックするため、同時 `Reset`（`TryRemove`）と競合すると孤立リストへの追記になり追記が失われ得る | 会話履歴のみで軽微。厳密化は任意 |
| 3 | Minor | Ai/VideoService.cs:46 | プロバイダ JSON 内の URL を認証済み `HttpClient` で再フェッチ（軽微な SSRF 面）。スキーム検証なし | `VideoEnabled` 既定オフ・URL は HF プロバイダ由来で低リスク。https スキーム検証の追加を推奨。詳細判断はセキュリティゲートへ引き継ぎ |
| 4 | Minor | Queue/ProcessedEventStore.cs:11 | 冪等 TTL が 10 分ハードコードで設定不可（メディア TTL とは別系統） | 設定化は任意。現状仕様と整合 |
| 5 | Minor | Queue/GenerationWorker.cs:52 | `TODO(messaging increment): push a failure notice` が陳腐化。実際は `WorkProcessor.ProcessAsync` が既にユーザーへ Error 通知済み | TODO を削除/更新（コメント整合） |

Blocker: 0 / Major: 0 / Minor: 5

## 規約適合（CLAUDE.md）
- モダン C#: primary constructor・collection expression（`[]`, `[new TextMessage{...}]`）・`sealed`・record を全面採用。適合。
- `IHttpClientFactory`: `AddHttpClient<IImageService,...>` / `<IVideoService,...>` の typed client で利用。適合。
- `TimeProvider`: 壁時計依存の処理が無く（タイムアウトは `CancellationTokenSource.CancelAfter`、期限は `IMemoryCache` の絶対期限、`DateTime.Now` 不使用）、注入不要。規約違反なし（依存が存在しないため適用対象外）。
- 外部 I/O 失敗のユーザー通知: チャットはタイムアウト/空応答を個別文言で通知、`WorkProcessor` が例外を捕捉し `UserMessages.Error` を送信、Push 失敗は上位へ伝播。握りつぶしなし。適合。
- 秘密情報の非出力: `appsettings.json` の秘密キーは空プレースホルダ、`.env`/`.env.*` は `.gitignore` 済み。ログに API キー・トークンを出さない。`DescribeLineError` は LINE エラーの message/property のみ。適合。
- 非同期処理と即時 200: 署名検証後に enqueue して即 `Results.Ok()`、生成は Channel + `BackgroundService`、完了後 Push。適合。
- メディアの公開 URL 配信: `/media/{id}`（GUID id で列挙防止）＋ TTL キャッシュ。動画プレビューは `/assets/video-preview.png`。適合。
- 署名検証: `WebhookRequestParser.ParseAsync(body, signature)` を生バイトに対して実行、`WebhookSignatureException`→401。`ChannelSecret` は起動時に非空検証（`ValidateOnStart`）。適合。
- 言語ルール: コメント/ログは英語、ユーザー向け文言は日本語（`UserMessages` / `SystemPrompt`）。適合。

## 良い点
- タイムアウトを全 HF 呼び出しでリンク CTS により厳密適用。`HttpClient.Timeout=Infinite` と役割分担が明確。
- worker は per-item スコープ＋例外分離で停止しない設計。キュー満杯時は drop してユーザー通知。
- 画像/動画は `ProcessedEventStore` で再配信の冪等化。返信トークン失効に備えた reply→push フォールバック。
- 起動時オプション検証で秘密欠落を fail-fast。Dev 専用診断エンドポイントは Development 限定。

## 判定理由
Blocker・Major ともに 0。ビルドはクリーン（警告0/エラー0）。署名検証・秘密情報の取り扱い・外部 I/O 失敗のユーザー通知・即時 200＋非同期処理・公開 URL 配信といった重点項目はいずれも規約どおり実装されており、正しく動作する。残る指摘はすべて Minor（並行性の理論的レース、TTL ハードコード、陳腐化 TODO、動画 URL の SSRF 面）で、次工程（セキュリティゲート）を妨げない。したがって **PASS**。
指摘 #3（動画 URL 再フェッチ）はセキュリティゲートで SSRF 観点の確認を推奨する。委譲エンジン（Roslyn MCP）未接続のためアンチパターン自動検出は未実施であり、その分だけ本判定の確度は通常より低い点を付記する。
