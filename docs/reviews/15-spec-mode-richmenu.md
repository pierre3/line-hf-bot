# 仕様レビュー — モードコンテキスト+リッチメニュー+画像セッション+i18n (2026-08-15)
Verdict: PASS
委譲分析: なし（自前）
対象: `docs/specs/03-mode-context-richmenu.md`

## 経緯（初回 FAIL → 修正反映）
- 初回レビューで FAIL（Major 3・Minor 3。壊れ参照・spec02 依存範囲の誤り・言語ルール未改訂）。
- 差し戻し後、修正版を再レビューして PASS。残 Minor（CLAUDE.md アーキ記述の更新漏れ）も修正版へ反映済み。

## 前回指摘の解消状況（全 6 件 反映済み）
| # | 重大度 | 箇所 | 問題 | 修正版での対応 |
|---|---|---|---|---|
| M1 | Major | §2.3 | img2img が「spec02 の payload 依存」 | 流用は応答分岐(生バイト/JSON-URL)＋SSRF allowlist のみ、送信 payload は 3b 新規定義、`{inputs}` 流用不可を明記（§2.3/§5/§6）。spec02 §5 と整合（解消） |
| M2 | Major | §2.5/§3 | en 既定化に対し CLAUDE.md 言語ルール本文改訂が更新対象外 | §3 に「LINE 返信は日本語→既定は App__Locale 依存(en/ja)」への本文改訂を追加（§6 確定）（解消） |
| M3 | Major | §2.1 | 壊れた参照（§5 Low#2）／UserStateStore 方針未確定 | 壊れ参照除去。ChatHistoryStore 同型（per-user/上限なし/reset クリア/再起動消失）確定。実コードと一致（解消） |
| m4 | Minor | §2.2 | out→Assets コピー＋csproj 同梱手順欠落 | out/<locale>→LineHfBot/Assets/richmenu/<locale>、Content Include(PreserveNewest)、publish/Docker 同梱を明記（解消） |
| m5 | Minor | §4-2 | 検証手段未記載 | postback=サーバ状態検証可／alias ハイライト=実機確認(webhook-test 不可)を峻別（解消） |
| m6 | Minor | §2.3 | 既存 QuickReplies.Default の去就不明 | message-action QR 廃止、画像=[🔄][✏️][💬]/chat=QR なし/video=[💬] のみに置換を明記（解消） |

## 今回の残指摘（反映済み）
| # | 重大度 | 箇所 | 問題 | 対応 |
|---|---|---|---|---|
| A | Minor | §3 | CLAUDE.md 更新対象に「アーキテクチャ要点」のモード切替/QuickReply 説明が漏れ | §3 に CLAUDE.md アーキテクチャ要点（モード解釈・QuickReply）の改訂を追記（**修正済み**） |

## チェックリスト
- [x] 受入基準が明確・テスト可能（§4、12 項目。各項目に検証手段／フェーズ(3a/3b)明示）
- [x] 未解決点ゼロ（§6 全確定。将来項目はスコープ外として明示分離）
- [x] スコープ整合（spec01/02・実コードと整合。3a は spec02 非依存で単独着手可）
- [x] 実コード整合（ChatHistoryStore/QuickReplies/MessageDispatcher/UserMessages の現状と一致）
- [x] SDK 前提（RichMenu 一式 / PostbackEvent）を NuGet アセンブリで確認済み（新パッケージ不要）

## 判定理由
Blocker 0・Major 0。初回 FAIL の Major 3・Minor 3 と再レビューの残 Minor 1 はすべて修正版へ反映済み。
実コードおよび spec01/02 と整合し、受入基準は検証可能、3a/3b フェーズ分けが明確で 3a は spec02 非依存で単独着手可能。実装フェーズへ進める品質。
