# ドキュメントレビュー — 画像 Provider 統合（案A / spec02） (2026-08-15)
Verdict: PASS
委譲分析: なし（自前）

## チェックリスト / 指摘
| # | 重大度 | 箇所 | 問題 | 必要な対応 |
| --- | --- | --- | --- | --- |
| 1 | Minor | docs/specs/02-image-provider-integration.md:54（§3 表） | 許可規則を「サフィックス一致」と表現。実装・§2.4・ユーザー向けドキュメントは「ラベル境界一致」（evilfal.media は拒否）で不整合な緩い用語 | §3 表を「ラベル境界一致」に統一（**本レビューで反映済み**） |

## 判定理由
Blocker/Major 0。新設定 `HuggingFace__MediaRefetchAllowedHosts` は、キー名・既定値(`fal.media;replicate.delivery`)・
意味論（画像/動画共通・JSON-URL 再取得・ラベル境界一致・空=全拒否フェイルクローズ・`;`/`,`/whitespace 区切り）が、
正である `BotOptions.cs` および `MediaRefetch.cs` の実装と、`.env.example` / `README.md` / `README.ja.md` /
`CLAUDE.md` / spec §3 / テスト `MediaServiceTests.cs`(AC#10) で過不足なく一致。`CLAUDE.md` の「HF Inference」
本文改訂（生バイト or JSON(URL) 両対応・SSRF ガード付き再取得＝MediaRefetchAllowedHosts）は `ImageService.cs` の
二分岐実装と整合し、image が JSON-URL 応答対応した事実を正しく反映。エンドポイント（/webhook・/media/{id}・/health）・
コマンド（`dotnet test`=36件緑）・秘密情報（プレースホルダのみ）・セットアップ再現性にも差異なし。唯一の Minor
（内部仕様書 §3 表の用語の緩さ）は本レビューで「ラベル境界一致」に統一済み。差し戻すべき項目なしで PASS。
spec02 の4ゲート（仕様=14 / 実装=19 / セキュリティ=20 / ドキュメント=21）すべて PASS。
