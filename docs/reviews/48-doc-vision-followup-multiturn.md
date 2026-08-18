# ドキュメントレビューゲート — spec09 vision フォローアップ / 会話型 vision

- 日付: 2026-08-18
- 対象: `docs/specs/09-vision-followup-multiturn.md`（Part 1＋Part 2b）の実装に伴うドキュメント変更
- ゲート: 4段階レビューの④ドキュメント（`doc-review-gate`）
- **判定: PASS**（Blocker 0 / Major 2 / Minor 1）。Major 2 は本レビュー後に修正済み。

## 整合チェックリスト
- [x] 六者一致: `App__VisionMaxTurns=8` が `BotOptions.cs`（コード既定）＝ `appsettings.json` ＝ `.env.example` ＝ `README.md` ＝ `README.ja.md` ＝ `CLAUDE.md` で一致（下限1丸めも各所で言及）
- [x] README(EN)/README.ja(JA) 情報整合（Features・Commands 表・Config 表の会話型 vision と `VisionMaxTurns` が両言語に対応して存在）
- [x] 日本語が自然（翻訳調・AI 生成臭なし）
- [x] クレジット消費説明の正確性（毎ターン画像＋履歴を再送→ターン数に比例、上限 `VisionMaxTurns`）が実装（`WorkProcessor.HandleVisionAsync` の毎回 history 再送 / `AppendVisionTurn(..., VisionMaxTurns)`）と一致
- [x] 離脱導線（💬 チャットへ / モード切替 / スラッシュ）が Commands 表・Features と実装（`VisionAnswer` QR、dispatcher の Clear 地点）で一致
- [x] Part 1 の gate 条件（生成/編集画像結果の 💬 Ask は `VisionEnabled` 時のみ）が実装 `QuickReplyFactory.ImageResult` と一致
- [x] `VisionAnswer` QR 項目（Edit / Animate(video時) / Chat）が実装・spec §4.5・CLAUDE.md 一致
- [x] エンドポイント（/webhook /media/{id} /health）変更なし、記載と一致
- [x] 秘密情報なし（`.env.example` はプレースホルダのみ）
- [x] コメント/ログは英語、ユーザー向け文言は locale 依存（`VisionFollowupHint` en/ja、Help 両言語）

## 指摘と対応
| # | 重大度 | 箇所 | 問題 | 対応 |
|---|---|---|---|---|
| 1 | Major | `README.md:119` / `README.ja.md:119` | Commands 表直後の要約文が画像結果ボタンの列挙に **💬 Ask（質問）を含んでいない**（既定 `VisionEnabled=true` では表示され、同 README の Features・Config 表と自己矛盾） | **修正済**。要約列挙に `💬 Ask about this (when vision enabled)` / `💬 質問（vision 有効時）` を追加 |
| 2 | Major | `docs/deploy/docker-hub.md` / `docker-hub.ja.md` の App パラメータ表 | README が「full list」と案内するパラメータ表に **`App__VisionMaxTurns` が無い**（他 `App__*` は網羅） | **修正済**。両 docker-hub 表に `App__VisionMaxTurns`（既定 `8`・最小1・追い質問ごとに再送でクレジット比例消費）を追記。あわせて `App__VisionEnabled` 説明も生成画像への💬質問ボタンに言及 |
| 3 | Minor | `README.md`（EN）Features | 説明ラベル「💬 Ask about this **image**」と実ボタンラベル `LabelAsk="💬 Ask about this"` の文言差 | 据え置き（機能説明として許容） |

## 判定理由
spec09 §5 の中核要件（`App__VisionMaxTurns` の反映先一致）は値・下限丸めまで完全一致。会話型 vision のクレジット消費・離脱導線・Part1 の Ask ボタン gate・`VisionAnswer` QR・失敗ターン非蓄積/初回ヒントの各記述は実装と齟齬なし。Blocker 0 のため PASS。Major 2 件（README 内自己矛盾・docker-hub の新キー欠落）は公開前修正推奨だったため本レビュー後に修正済み。
