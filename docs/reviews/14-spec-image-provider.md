# 仕様レビュー — 画像 Provider 統合（案A） (2026-08-15)
Verdict: PASS
委譲分析: なし（自前）
対象: `docs/specs/02-image-provider-integration.md`

## 受入基準チェックリスト
- [x] 受入基準が明確・テスト可能（§4）
- [x] 未解決点ゼロ（§5 全確定）
- [x] スコープ整合（スコープ外明示、本文と矛盾なし）
- [x] 既存仕様 01・実コード（ImageService/VideoService/BotOptions/HfHttp）との整合（流用元パターン・命名・{model}置換・SSRF Low#1）

## 指摘（すべて反映済み）
| # | 重大度 | 箇所 | 問題 | 対応 |
|---|---|---|---|---|
| 1 | Major | §2.4 allowlist | 「サフィックス一致」が曖昧で `evilfal.media` 誤許可の恐れ | ラベル境界一致（完全一致 or `"."+host` 終端）に明文化＋§4 に境界バイパス拒否の受入項目を追加（**修正済み**） |
| 2 | Major | §2.1/§2.3/§4 | 共通化で動画 `video`/`video.url` シェイプが統一スキーマから漏れ、動画 JSON-URL 回帰の恐れ | 統一スキーマに `video`/`video.url` を追加（キー和集合）＋§4 に動画 JSON-URL 回帰項目を追加（**修正済み**） |
| 3 | Minor | §4 項目 | Authorization 非付与の検証手段が未記載 | DelegatingHandler でヘッダ検査する旨を明記（**修正済み**） |

## 判定理由
Blocker 0。受入基準（現10項目）はすべて検証可能、未解決点は確定済み、既存仕様・実コードとの整合も取れており実装フェーズへ進める品質。
初回指摘の Major×2・Minor×1 は本レビューで仕様へ反映済み。実装着手可。
