# レビューゲート記録 (docs/reviews/)

line-hf-bot の**4段階レビューゲート**の判定記録を残す場所。各ゲートは薄いラッパのサブエージェント
（`.claude/agents/`）が担い、実分析は既存プラグインに委譲する。

## ゲートと担当エージェント
| 順 | ゲート | エージェント | 委譲先（分析エンジン） |
|---|---|---|---|
| 1 | 仕様レビュー | `spec-review-gate` | なし（自前） |
| 2 | 実装レビュー | `impl-review-gate` | `dotnet-claude-kit:code-review`（Roslyn MCP） |
| 3 | セキュリティレビュー | `security-review-gate` | `dotnet-claude-kit:security-scan`（＋`claude-security`/`42crunch`） |
| 4 | ドキュメントレビュー | `doc-review-gate` | なし（自前） |

## 運用ルール
- **起動**: オンデマンド（「仕様ゲート回して」等の指示、または各フェーズ完了時）。将来は機能/PR 単位でも同じエージェントを流用。
- **強制度**: 記録付きソフトゲート。**FAIL は既定でブロック**（次ゲートに進まず差し戻し）。ユーザーが明示すれば上書き可。
- **順序**: 仕様 → 実装 → セキュリティ → ドキュメント。各ゲート PASS で次へ。

## ファイル命名
`<連番2桁>-<gate>-<対象スラッグ>.md`
例: `01-spec-initial-scaffold.md` / `02-impl-image-generation.md` / `03-security-webhook.md`

## 記録テンプレート
```
# <ゲート名> — <対象> (<YYYY-MM-DD>)
Verdict: PASS | FAIL
委譲分析: <実行したプラグイン/スキル、無ければ「なし（自前）」>

## チェックリスト / 指摘
| # | 重大度 | 箇所 | 問題 | 必要な対応 |

## 判定理由
<根拠。FAIL なら差し戻すべき項目を明示>
```

## 記録インデックス
<!-- 新しい判定を上に追記する -->
- 2026-08-14 **PASS** 仕様レビュー — [`01-spec-line-hf-bot.md`](01-spec-line-hf-bot.md)（Major #1 タイムアウトを仕様反映）
