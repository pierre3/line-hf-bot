# 実装レビュー — image/video/docker (2026-08-14)
Verdict: **PASS**
委譲分析: `dotnet-claude-kit:code-review`（Roslyn MCP 未接続のため手動フォールバック）
対象: 画像生成・動画スキャフォールド（既定オフ）・LINE 送信/識別子・HF エラー処理・Docker

## 判定サマリ
Blocker/Major なし。typed HttpClient + per-request タイムアウト（linked CTS）、HfHttp のエラー本文表示（秘密非露出）、
VideoService の JSON-URL 安全抽出（fetch に Bearer 非送出）、MediaStore（GUID+TTL）と /media の 404 処理、
WorkProcessor の共通化・video フラグ・DI スコープ、Dockerfile（multi-stage/非root/dev非公開）はいずれも妥当。

## 指摘（Minor）と対応
| # | 箇所 | 問題 | 対応 |
|---|------|------|------|
| 1 | WorkProcessor 集約 catch | シャットダウン時の OCE も飲む | **対応済**: `when (ex is not OperationCanceledException)` で除外 |
| 2 | VideoService URL fetch | SSRF/サイズ上限（video 既定オフ・HF 信頼） | 動画プロバイダ統合時に host allowlist/サイズ上限を追加（追跡） |
| 3 | MediaStore | IMemoryCache SizeLimit 未設定 | 低トラフィックなら実害なし。高負荷時に検討（追跡） |
| 4 | Dockerfile | HEALTHCHECK 未定義 | 見送り（aspnet イメージに curl 非同梱＝肥大化回避）。/health は存在（追跡） |

## 判定理由
Minor のみで PASS。#1 は本ゲート後に修正済み。#2〜#4 は追跡事項。
