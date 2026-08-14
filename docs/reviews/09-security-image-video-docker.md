# セキュリティレビュー — image/video/docker (2026-08-14)
Verdict: **PASS**
委譲分析: `dotnet-claude-kit:security-scan`（6層方針）＋手動確認。脆弱パッケージ 0 件。
対象: 画像/動画サービス・メディア配信・Docker・certs

## 判定サマリ
Critical/High/Medium なし。秘密情報はコード・ログ・レスポンス・**Docker イメージ**に露出なし
（appsettings 空、`.env` は gitignore/dockerignore 双方で除外）。非root・ValidateOnStart 継続・署名検証を確認。
`/dev/*` は Development 限定で Production イメージ非公開。certs は README のみで CA 未コミット。

## 指摘と対応
| # | 重大度 | 箇所 | リスク | 対応 |
|---|--------|------|--------|------|
| 1 | Low | VideoService URL fetch | SSRF（HF router 由来・video 既定オフ・Bearer 非送出・scheme 制約で悪用性低） | 動画プロバイダ統合時に https/allowlist 検証を追加（追跡） |
| 2 | Info | /media/{id} | 認証なし配信（LINE 匿名取得に必須）。GUID+TTL で緩和 | `X-Content-Type-Options: nosniff` 付与を検討（任意・追跡） |
| 3 | Info | /dev/* | Development 限定・Production 非公開を確認 | 対応不要 |

## 判定理由
Critical/High/Medium なし。残存は Low/Info のみで PASS。#1 は動画有効化時の追跡事項。
