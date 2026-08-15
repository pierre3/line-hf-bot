# セキュリティレビュー — 全体統合点検 (2026-08-15)
Verdict: **PASS**
委譲分析: `dotnet-claude-kit:security-scan`（6層パイプライン）＋手動確認。`dotnet list package --vulnerable --include-transitive` = 脆弱パッケージ 0 件。
対象: `LineHfBot` プロジェクト全体、`Dockerfile` / `compose.yaml` / `scripts/run.ps1` / `.env.example` / `certs` / `.gitignore` / `.dockerignore`

> これは静的解析です。既知パターンは検出しますが、ペネトレーションテスト・動的解析・ビジネスロジック検証の代替ではありません。

## サマリ（重大度別）
| 重大度 | 件数 |
|--------|------|
| Critical | 0 |
| High | 0 |
| Medium | 0 |
| Low | 2 |
| Informational | 3 |

## 6層スキャン結果
| 層 | OWASP 2025 | 判定 | 内容 |
|----|-----------|------|------|
| 1. パッケージ脆弱性 | A03 Supply Chain | PASS | `dotnet list package --vulnerable --include-transitive` = 0 件（Line.OpenApi.Bot 1.0.0 / SemanticKernel 1.79.0 / Connectors.HuggingFace 1.79.0-preview） |
| 2. シークレット検出 | — | PASS | ソース・json・yaml・ps1・md に実シークレットなし。`appsettings*.json` の Line/HuggingFace は空文字。`.env` は未追跡（`git ls-files` に不在）で `.gitignore`／`.dockerignore` 双方が除外。`.env.example` は空値のみ |
| 3. OWASP コードパターン | A05/A08/A04/A01 | PASS | SQL/XSS/`Html.Raw`/`BinaryFormatter`/`TypeNameHandling`/MD5・SHA1/ECB/`Process.Start` いずれも不検出。DB 層なし。署名検証は正しく HMAC-SHA256（下記） |
| 4. 認証設定 | A07/A01 | PASS | `/webhook` は LINE 署名検証で保護。`/media/{id}`・`/health`・`/assets/*` は設計上の匿名公開（LINE 匿名取得に必須）。`/dev/*` は Development 限定 |
| 5. CORS | A02 | PASS | `AddCors`/`UseCors`/`AllowAnyOrigin` なし＝CORS ヘッダ無し（同一オリジンのみ）。誤設定なし |
| 6. データ保護 | A04/A09 | PASS | ログは識別子（userId/eventId/kind）中心で PII・トークン・プロンプト本文を出さない。レスポンスは生成メディアのみ |

## 重点確認（CLAUDE.md 準拠）

### Webhook 署名検証 — PASS
- `Program.cs` は生ボディをバイト列で読み（`request.Body.CopyToAsync(ms)` → `ms.ToArray()`）、`x-line-signature` と共に `WebhookRequestParser.ParseAsync(body, signature)` に渡す。署名は生ボディに対する ChannelSecret の HMAC-SHA256（検証は `Line.OpenApi.Bot` に委譲、タイミング安全比較もライブラリ側の標準実装に依存）。
- 検証失敗 `WebhookSignatureException` → **401**、ペイロード不正 `WebhookPayloadException` → **400**。握りつぶしなし。
- ChannelSecret/AccessToken は `ValidateOnStart` で必須化（空のまま起動不可）→ 空シークレットで偽造 webhook を受理する事故を防止。

### トークン漏洩 — PASS
- HuggingFace ApiKey は各 `HttpRequestMessage` の `Authorization` ヘッダにのみ設定。ログ出力なし。
- `HfHttp.EnsureSuccessAsync` は HF エラー応答本文を 500 文字に切り詰めて例外へ含めるが、これは HF 側の理由文であり、リクエストの Authorization は含まない。
- `LineMessenger.DescribeLineError` はリフレクションで LINE の検証詳細のみ抽出、トークン非出力。
- `.env` 未追跡・二重除外。git 履歴にも `.env` の混入なし。

### SSRF（重点・実装ゲート引き継ぎ）— 残存 Low、PASS
- `VideoService.cs:26` の POST 先 URL は `VideoEndpoint`（env 由来・運用者設定）に `{model}` を `VideoModel`（同）で置換して組み立て。**エンドユーザー入力はプロンプト（JSON body の `inputs`）のみで URL に混入しない** → ユーザー由来の URL 注入余地なし。`ImageService.cs:24` も同様。
- `VideoService.cs:46` の `http.GetByteArrayAsync(videoUrl)` は、プロバイダ JSON 応答内の URL を再フェッチする。評価:
  - **認証トークンは同送されない**。Bearer は 28 行目で個別 `HttpRequestMessage` に付与しており、`http.DefaultRequestHeaders` には無い。`GetByteArrayAsync` は新規 GET を生成するため Authorization ヘッダ無し → 第三者 URL への資格情報漏洩は発生しない。
  - URL の出所は HF router（router.huggingface.co）応答であり、LINE エンドユーザーではない。悪用には HF プロバイダ応答の掌握が必要でスレットモデル外。
  - さらに `App__VideoEnabled` は既定 **false** で本経路は既定で不到達。
  - 残存: 再フェッチ URL に scheme/host の allowlist が無い（悪性/侵害プロバイダが内部 URL を返す仮想シナリオ）。`GetByteArrayAsync` は非 http(s) scheme を拒否するため影響限定 → **Low（多層防御ギャップ）**。動画プロバイダ統合時に `https` 限定＋host allowlist を追加（追跡）。

### メディア配信 `/media/{id}` — PASS
- `id` は `Guid.NewGuid().ToString("N")`（128bit 乱数）で予測・列挙困難。
- 参照は `IMemoryCache` のキー `media:{id}` ルックアップで、ファイルシステム経路ではない → **パストラバーサル不可**（`../` を含んでもキー文字列として扱われるだけ）。
- 未知/期限切れは 404。TTL 既定 10 分。認証なし配信は LINE 匿名取得のための設計で、乱数 ID＋短命 TTL で緩和。

### DoS 面 — PASS（Low 追跡）
- `BoundedChannel`（既定 100、満杯時 `TryWrite` false → ドロップ＋「混雑」通知）。ワーカー数上限（既定 2）。HF 呼び出しに個別タイムアウト（chat 60s/image 120s/video 300s、`CancelAfter`）。生成要求の起動には有効署名が必須で乱発を抑制。
- 残存: `ChatHistoryStore._byUser`・`MediaStore`／`ProcessedEventStore` の `IMemoryCache` に**サイズ上限（`SizeLimit`）や distinct ユーザー上限が無い**。多数の異なる userId が来た場合のメモリ増加余地 → 個人/小規模用途では受容可能だが **Low**。将来 `IMemoryCache` に `SizeLimit`、履歴 store に LRU/上限を検討（追跡）。

## 指摘一覧
| # | 重大度 | 箇所 | 脆弱性/リスク | 必要な対応 |
|---|--------|------|---------------|------------|
| 1 | Low | `LineHfBot/Ai/VideoService.cs:46` | プロバイダ応答 URL の再フェッチ（SSRF 多層防御ギャップ）。トークン非同送・video 既定オフ・非 http(s) 拒否で悪用性は低い | 動画プロバイダ統合時に `https` 限定＋host allowlist を追加（追跡） |
| 2 | Low | `MediaStore` / `ProcessedEventStore` / `ChatHistoryStore` | `IMemoryCache`／`ConcurrentDictionary` に size/ユーザー上限が無く、多数 userId でメモリ増加余地 | `IMemoryCache` に `SizeLimit`、履歴 store に上限/LRU を検討（追跡） |
| 3 | Info | `Program.cs` `/media/{id}` | 認証なし配信（LINE 匿名取得に必須）。乱数 GUID＋TTL で緩和済み | `X-Content-Type-Options: nosniff` 付与を検討（任意・追跡） |
| 4 | Info | `Program.cs` `/dev/*` | 例外メッセージ返却・認証なしだが Development 限定。コンテナは `ASPNETCORE_ENVIRONMENT` 未設定＝Production のため非公開 | 対応不要 |
| 5 | Info | `scripts/run.ps1:177` | `line config set --token $token` でトークンを CLI 引数に渡す（ローカル開発のみ、プロセス一覧に残り得る） | ローカル運用限定のため受容。必要なら環境変数経由に | 

## 判定理由
Critical/High/Medium いずれも 0 件。脆弱パッケージ 0 件、実シークレットのコミット・ログ・イメージ露出なし、Webhook 署名検証は生ボディ HMAC-SHA256＋失敗時 401＋ChannelSecret 必須で健全。重点の SSRF（VideoService.cs:46）は「トークン非同送・URL 出所が HF router・video 既定オフ・非 http(s) 拒否」により実効リスクは Low の多層防御ギャップに留まり、差し戻し対象の Critical/High には該当しない。残存は Low×2／Info×3 のみで、しきい値上 **PASS**。Low 2 件は動画プロバイダ統合およびメモリ上限として追跡。
