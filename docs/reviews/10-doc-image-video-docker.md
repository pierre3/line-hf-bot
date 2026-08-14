# ドキュメントレビュー — image/video/docker (2026-08-14)
Verdict: **PASS**
委譲分析: なし（自前）
対象: README.md / README.ja.md / CLAUDE.md / Dockerfile・compose.yaml・.env.example（設定の実体は BotOptions.cs / appsettings.json）

## 判定サマリ
Blocker/Major なし。設定4箇所（BotOptions/appsettings/CLAUDE.md/.env.example）の全キー（新規 ImageEndpoint/VideoEndpoint/VideoEnabled 含む）が一致。
README 英/日はコマンド表・設定表・機能記述（chat+image 稼働、video 既定オフ）が実装と整合。Docker 手順（:8080・env_file・非root）も矛盾なし。相互リンク・日本語品質・秘密非露出も良好。

## 指摘（Minor）と対応
| # | 箇所 | 問題 | 対応 |
|---|------|------|------|
| 1 | README §2/§3 | ローカル `dotnet run` は :5119 で、トンネル `-p 8080` と食い違い | **対応済**: ローカル起動を `dotnet run --urls http://localhost:8080` に統一 |
| 2 | README §4 | 「同梱の line CLI」だが実際は `dotnet tool install -g` の別ツール | **対応済**: 「グローバルツールとして導入」に表現修正 |

## 判定理由
全基準を満たし Blocker なしで PASS。Minor 2件は本ゲート後に修正済み。
