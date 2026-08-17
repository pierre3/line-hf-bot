# レビュー記録 39 — spec07 画像→チャット(vision/VQA) セキュリティゲート

- 日付: 2026-08-17
- ゲート: セキュリティ（4段階中3）
- 対象: spec07（docs/specs/07-image-vision-vqa.md §7）
- 前提: build 0/0、test 79 緑、実装ゲート PASS（記録38）
- 委譲: dotnet-claude-kit:security-scan（6層）。42crunch/claude-security は不適用（新規 HTTP API 面・OpenAPI なし）
- 判定: **PASS**

## 6層サマリ
| 層 | 結果 |
|---|---|
| 1 パッケージ脆弱性 | PASS（--vulnerable 0 件、新規依存なし）|
| 2 シークレット | PASS（.env.example 空値・.gitignore で .env 除外）|
| 3 OWASP パターン | PASS（injection/危険 API/SSRF なし）|
| 4 認証/アクセス制御 | PASS（webhook 署名検証不変・新規 endpoint なし）|
| 5 CORS | PASS（変更なし）|
| 6 データ保護/ログ | PASS（token/画像本文/質問文の非露出）|

## 重点確認（CLAUDE.md）
1. 署名検証回帰なし: /webhook は生ボディ→ParseAsync→401/400 のまま。vision は検証後 DispatchAsync 分岐追加のみ。
2. トークン漏洩なし: Bearer は VisionEndpoint(router)のみ。AllowAutoRedirect=false。ログは kind/user/eventId のみ。
3. SSRF 新規面なし: 結果 URL 再取得なし・送信先 router 固定・data URI はユーザー自身の写真(spec04 上限済み)。
4. DoS 制御済み: VisionTimeoutSeconds linked CTS(下限5s)、Queue 有界(100)、TryMarkNew 冪等。
5. 脆弱パッケージなし。

## 指摘（いずれも Informational・ブロックせず）
- base64 化で送信ボディ +33%（最大~13.3MB）。MaxIncomingImageBytes/Queue 有界で上限あり。受容。
- WorkProcessor 最上位 catch が例外ログ。vision 非2xx の HF 応答本文(500字トランケート)を含むが HF 由来・トークン非含。

## 結論
Critical/High/Medium なし。差し戻し事項なし。ドキュメントレビュー（ゲート4）へ進行可。
