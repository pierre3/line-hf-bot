# 仕様レビュー — docs/specs/09-vision-followup-multiturn.md

- 日付: 2026-08-18
- ゲート: ① 仕様（`spec-review-gate`）
- 対象仕様: `docs/specs/09-vision-followup-multiturn.md`（vision フォローアップ / 会話型 vision＝Part 1 生成/編集画像への質問＋Part 2b マルチターン）
- 初回判定: **FAIL**（Blocker 1 / Minor 2）→ 指摘反映後 **PASS 相当**

## 判定基準と結果
- 受入基準の明確さ: OK（§6 の各 AC は quickreply 内容・enqueue の kind/RefImageId・messages 組み立て・store 破棄・期限切れを観測可能な粒度で規定）。
- 未解決点ゼロ: OK（§7 は初回時点で①②③すべて `[x]`）。
- テスト可能性: 概ね OK。ただし状態機械の交差点（失敗初回ターン × 初回ヒント/セッション開始）に論理矛盾（Blocker）。
- スコープ整合: OK（動画/音声・複数画像・永続化・provider fallback を明示除外。§1/§4/§6 一貫）。
- 実コード整合: OK。`PendingAction{Edit,VisionQuestion,Animate}`／`SetPending`/`SetReceivedImage`／`action=ask`→`LastImageId`→`Pending=VisionQuestion`→`WorkKind.Vision`／`QuickReplyFactory` は既に `IOptions<AppOptions>` 注入済（`VideoEnabled`）→`VisionEnabled` gate 追加と `VisionAnswer` は整合／`PushTextAsync` は任意 `QuickReply` を受理／`messages.Timeout`/`EmptyAnswer` 実在。`/dev/vision` は未存在＝§4.8 の dev 更新は真に任意（5引数化で compile break なし）。

## 指摘一覧
| # | 重大度 | 箇所 | 問題 | 対応 |
|---|---|---|---|---|
| 1 | **Blocker** | §4.4 手順6⇔7 / AC7・AC8 | 初回ターンで vision 失敗（`Timeout`/`EmptyAnswer`）時、手順6は `AppendVisionTurn` を呼ばず**セッション未開始**（`VisionImageId=null`）。一方 手順7は「push 前 history が空」だけを条件にヒント＋継続導線を出す→「続けて質問できます」と表示するのに次の素メッセージは priority2 に乗らず現在モードで解釈され、追い質問が機能しない矛盾。 | **反映済**。ヒント/継続導線を「実際に `AppendVisionTurn` された初回成功ターン」に gate。失敗初回はセッション未開始＝ヒントなし（`VisionAnswer` QR の Edit/[Animate]/Chat は付与）。AC8 を「初回**成功**ターンのみ」に修正。§7 決定④で明文化。 |
| 2 | Minor | §4.1 Clear 地点 | 編集チェーンの `SetLastImageId`（`SetLastImage` とは別）が Clear 地点に不在。実害はない（編集は `edit` postback→`ClearVisionSession` 経由でセッション解除済み）が根拠が未記載で実装者が迷う。 | **反映済**。§4.1 に「`SetLastImageId` は `edit` arm で解除済みのため Clear 対象外（リークしない）」を明記。§7 決定⑤。 |
| 3 | Minor | §4.4.6 / AC7 | 失敗ターン判定が `answer == Timeout/EmptyAnswer` の**文字列一致**（ロケール依存・偶発一致リスク）。spec07「表示可能文字列を返す」契約の踏襲で新規欠陥ではない。 | **反映済**。AC7 に「en/ja 両文言でテスト固定」を追記。§7 決定⑤で構造化シグナル化を将来改善余地として注記。 |

## 修正反映（2026-08-18）
- §4.4 手順6/7・§6 AC7/AC8・§4.1 Clear 地点・§7 決定④⑤ を更新。
- Blocker（指摘1）＋Minor（2/3）すべて反映済み。reviewer 所見「Blocker を Minor と併せ反映すれば PASS 相当」に合致。

## 次ゲート
→ 実装後に ② 実装ゲート（`impl-review-gate` → `dotnet-claude-kit:code-review`）。
