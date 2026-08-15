# セキュリティレビューゲート記録 27 — spec 04 ユーザー画像受信→image-to-image 編集

- 対象: `git diff a012f10..HEAD`（`4a10d5a` 実装 + `8b15e79` タイムアウト修正）
- ブランチ: `feat/user-image-edit`
- 前提ゲート: 仕様=25(PASS) / 実装=26(PASS)
- 委譲分析: dotnet-claude-kit:security-scan（6層）+ `dotnet list package --vulnerable --include-transitive`
- 日付: 2026-08-15

## Verdict: PASS（Critical/High/Medium 0）

## 指摘
| # | 重大度 | 箇所 | リスク | 対応 |
|---|---|---|---|---|
| 1 | Info | LineContentService.cs:45（Blob/Kiota client） | 受信取得は固定ホスト api-data.line.me（SDK管理）経由だが redirect 無効化は未明示。第一者・Bearer 認証ホストで allowlist 迂回シナリオ外＝実害無視可。 | 対応不要（将来 defense-in-depth で検討可） |
| 2 | Low | LineContentService.cs:37 | 取得タイムアウト上限キャップ無し。運用者設定由来で ReadCapped＋停止トークンにより実効抑制あり。 | 記録のみ（受容可） |
| 3 | Info | WorkProcessor.cs:129 | TryMarkNew を取得前に実施＝取得失敗で再配信非再取得。実装ゲート26 Minor#2 受容済み。セキュリティ影響なし。 | 対応不要 |

## 層別結果
| 層 | 判定 | 所見 |
|---|---|---|
| 1. 脆弱パッケージ | PASS | CVE 0（両プロジェクト）。新規 NuGet 追加なし |
| 2. シークレット検出 | PASS | ハードコード無し。`.env`/`.env.*` gitignore、追跡は `.env.example` プレースホルダのみ。新規キー2件は非秘匿値。test-token/hf_test_token はテストフィクスチャ |
| 3. OWASP コードパターン | PASS | SSRF: messageId は Kiota が固定ホスト＋エンコード、external は enqueue 前に拒否、自前 URL fetch 無し。SQL/デシリアライズ/弱暗号 該当なし。IDOR: GUID media id・per-user LastImageId |
| 4. 認証設定 | PASS | webhook 署名検証（ParseAsync/生ボディ/401）不変。画像経路は検証後 DispatchAsync に追加で迂回不可。新規エンドポイント無し |
| 5. CORS | N/A(PASS) | CORS 未設定（server-to-server webhook） |
| 6. データ保護 | PASS | 新規ログは userId/messageId（LINE識別子・仕様許可）のみ。token/ApiKey 非露出。画像はメモリTTL有界 |

## 重点確認結果（CLAUDE.md 指定 5 項目）
1. SSRF — PASS。固定ホスト・external 拒否・任意 URL 非取得。redirect は Info#1 に記録。
2. トークン漏洩 — PASS。ChannelAccessToken/HF ApiKey は新規コードで非ログ・非例外。DescribeLineError は API バリデーション詳細のみ。
3. Webhook 署名検証 — PASS。回帰なし、迂回経路なし。
4. DoS/リソース枯渇 — PASS。ReadCapped 書込前判定でメモリ有界、linked-CTS タイムアウト、キュー容量＋満杯拒否。
5. IDOR/情報漏洩 — PASS。GUID id・per-user 状態・/media 既存 GUID 保護。

## 判定理由
Critical/High/Medium 0。新規攻撃面（ユーザー由来 messageId のコンテンツ取得）は SDK 管理の
固定ホスト・データプレーン経由で任意 URL 呼び出しに至らず、external 提供画像は取得前に拒否。
署名検証は不変で画像経路も迂回しない。メモリ・タイムアウト・キューで DoS 実効抑制。
残存は受容可の Info/Low 3 件のみ。次工程（ドキュメントレビュー 28）へ進行可。
