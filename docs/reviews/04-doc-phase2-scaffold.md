# ドキュメントレビュー — phase2-scaffold (2026-08-14)
Verdict: **PASS**（初回 FAIL → 修正後 PASS）
委譲分析: なし（自前）
対象: README.md / README.ja.md / CLAUDE.md / docs/specs/01-line-hf-bot.md（設定キーの実体は BotOptions.cs / appsettings.json）

## 判定サマリ
初回は設定キーの不整合で FAIL。修正後、実バインドコード（`GetSection(*.Section)`）と
CLAUDE.md・spec のキー一覧が完全一致し PASS。言語ルール（README 英語＋日本語版）、リンク健全性、
日本語品質、秘密情報の非露出はいずれも問題なし。

## 指摘と対応（初回 FAIL → 解消）
| # | 重大度 | 箇所 | 問題 | 対応 |
|---|--------|------|------|------|
| 1 | Blocker | CLAUDE.md 設定節 | `PublicBaseUrl`/`MediaTtlMinutes` を接頭辞なしで記載（実装は section `App`＝`App__` が正） | **解消**: `App__PublicBaseUrl` / `App__MediaTtlMinutes` に修正、`__` 区切りの説明を追加 |
| 2 | Major | CLAUDE.md 設定節 | 「すべて」と謳うが `Queue__*`・`Chat__MaxHistory`・`HuggingFace__*TimeoutSeconds` が欠落 | **解消**: 実装済みの全キーを追記 |
| 3 | Minor | spec §5 | 同様に `App__` 接頭辞欠落 | **解消**: `App__PublicBaseUrl` / `App__MediaTtlMinutes` に修正 |

## 判定理由
設定整合を含む5基準すべてを満たすため PASS。新規指摘なし。
