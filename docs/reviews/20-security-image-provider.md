# セキュリティレビュー — 画像 Provider 統合（案A / spec02） (2026-08-15)
Verdict: PASS
委譲分析: dotnet-claude-kit:security-scan（security-auditor サブエージェント経由。`dotnet list package --vulnerable --include-transitive` 実行・restore 成功）

## 対象差分（feat/mode-richmenu-spec / spec02）
新設された「provider が JSON で返すメディア URL を自前で再取得する」経路が主対象。
新規: `LineHfBot/Ai/MediaRefetch.cs`, `LineHfBot/Ai/MediaUrlExtractor.cs`, `LineHfBot.Tests/`（36件）
変更: `LineHfBot/Ai/ImageService.cs`, `LineHfBot/Ai/VideoService.cs`, `LineHfBot/Program.cs`（typed client に `AllowAutoRedirect=false`）, `LineHfBot/Configuration/BotOptions.cs`（`MediaRefetchAllowedHosts`）
実装ゲート: PASS（[`19-impl-image-provider.md`](19-impl-image-provider.md)）。同ゲートの引継ぎ事項 Major#1（allowlist リダイレクトバイパス）の正式クローズを本ゲートで評価。

## 指摘
| # | 重大度 | 箇所 | 脆弱性/リスク | 必要な対応 |
|---|---|---|---|---|
| 1 | Low | MediaRefetch.cs:40 / ImageService.cs:51 / VideoService.cs:51 | 応答ボディ読取（`ReadAsByteArrayAsync`）にサイズ上限なし＋typed client の `Timeout=InfiniteTimeSpan`。悪意/巨大メディアで大量メモリ確保の恐れ（DoS 面）。per-request `CancelAfter` で時間は上限あるがサイズは無制限。既存バイト経路と同パターンで回帰ではない。 | 任意（非ブロック）: `MaxResponseContentBufferSize` 設定 or `Content-Length` 事前確認。Queue 制限（`Queue__Workers`=2）で並行数は緩和済み。 |
| 2 | Low | HfHttp.cs:26 | 非成功時の例外に provider 応答ボディ先頭 500 字を含める。`WorkProcessor` が `LogError` で記録（ユーザーには静的 `messages.Error` のみ返す）。ボディは応答であって要求ヘッダではないため自鍵（HF ApiKey / LINE secret）は含まれない。 | 対応不要（許容）。provider が要求ヘッダをエコーする異常時のみ理論上の残余リスク。 |
| 3 | Info | MediaRefetch.cs | 再取得はホスト名の文字列一致で検証後に接続（IP ピン止めなし）＝DNS rebinding/TOCTOU。ポート制限なし。ただし悪用には許可 CDN（fal.media / replicate.delivery）の DNS 掌握が必要で、prompt しか制御できない攻撃者には到達不能。 | 対応不要（許容）。 |
| 4 | Info | LineHfBot.csproj | `Microsoft.SemanticKernel.Connectors.HuggingFace` は `-preview` ビルド。個人/小規模用途では許容。 | 将来アップグレード時に版数確認。 |

## 委譲分析の結果
- **脆弱パッケージ: 0**。restore 成功。`LineHfBot`（`Line.OpenApi.Bot` 1.0.0 / `Microsoft.SemanticKernel` 1.79.0 / SK HF Connector 1.79.0-preview）・`LineHfBot.Tests`（`Microsoft.NET.Test.Sdk` 17.14.1 / `xunit` 2.9.3 / `xunit.runner.visualstudio` 3.1.4 / `coverlet.collector` 6.0.4）とも既知 CVE なし。本ゲートでも `LineHfBot` 単独の `--vulnerable` を独立実行し「脆弱なパッケージなし」を確認。
- **SSRF: 必須統制すべて実装・テスト済み**。scheme=https 限定（MediaRefetch.cs:26-29）/ allowlist ラベル境界一致（:49-69、`Equals` or `EndsWith("." + allowed)`）/ 空 allowlist=全拒否フェイルクローズ（ParseHosts→`[]`→`foreach` が false）/ 再取得 GET に Authorization 非同送（bare `HttpRequestMessage`、Bearer は POST のみ per-request 付与）。
- **Major#1（AllowAutoRedirect）クローズ確認**: `Program.cs:53-58` が Image/Video **両** typed client の primary handler に `AllowAutoRedirect=false` を設定。両サービスは同一 `http` を `MediaRefetch.FetchAsync` に渡す（ImageService.cs:47 / VideoService.cs:47）ため、初回 POST・再取得 GET とも 3xx を追従せず `HfHttp.EnsureSuccessAsync` が throw。allowlist 越えの他ホストへ黙って追従する経路は消滅。
- **迂回試行はテストで実証**: `evilfal.media`・`fal.media.evil.com`・`notfal.media` 拒否、`cdn.fal.media`/`a.b.fal.media` 許可、大小文字非依存、空 allowlist 全拒否、http 拒否、allowlist 外拒否（POST のみで GET 発生せず）、再取得 GET の no-auth を `MediaRefetchGuardTests` / `MediaServiceTests` が網羅（36件全合格）。userinfo `@` トリック（`https://cdn.fal.media@evil.com/` → `uri.Host=evil.com` で拒否）も .NET Uri 解釈で無効化。
- **JSON 抽出の安全性**: `MediaUrlExtractor` は `JsonDocument.Parse` + 手動 `TryGetProperty`/`GetString` のみ。ポリモーフィック/型束縛デシリアライズなし、`JsonException` は捕捉。安全。抽出不能時は明示 throw。
- **秘密情報**: `.env` 非追跡（`git ls-files` は `.env.example` のみ、`.gitignore` に `.env`/`.env.*`＋`!.env.example`）。`.env.example` の secret は空値。例外/ログに自鍵混入なし。

## 3a 回帰確認
- **Webhook 署名検証（記録 17）に回帰なし**: `Program.cs:83-117` は生ボディをバイト読取（:91-94）→ `parser.ParseAsync(body, signature)` で HMAC 検証、`WebhookSignatureException`→401（:107-111）、`WebhookPayloadException`→400。差分は再取得経路のみで署名経路は不変。
- **動画経路回帰なし**: `video`/`video.url` シェイプの JSON-URL 抽出→再取得を `MediaServiceTests` が確認。

## 判定理由
Critical/High/Medium いずれも 0、脆弱パッケージ 0。本機能最大の攻撃面である SSRF は spec §2.4 の必須統制（scheme=https・allowlist ラベル境界・空=全拒否フェイルクローズ・no-auth・timeout）を実装かつテストで実証し、実装ゲートから引き継いだ Major#1（リダイレクトによる allowlist 迂回）は Image/Video 両 typed client の `AllowAutoRedirect=false` で正式クローズ済みと確認。残る指摘は Low 2・Info 2 の上乗せ強化のみで、いずれも攻撃者誘発経路がない/資格情報漏えいなし/回帰でないため PASS をブロックしない。秘密情報の非露出（`.env` 非追跡・例外本文は provider 応答のみ）と署名検証の無回帰も確認。
