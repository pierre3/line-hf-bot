# 仕様レビュー — ユーザー画像の受信→image-to-image 編集 (2026-08-15)
Verdict: PASS
委譲分析: なし（自前）
対象: `docs/specs/04-user-image-edit.md`

## 経緯（初回 FAIL → 修正反映 → 再判定 PASS）
- 初回レビューで FAIL（Blocker 1・Minor 4）。中核機構である受信画像の本体取得（§2.2 `LineContentService`）の SDK 前提が実 SDK・実コードと不一致で、仕様どおり実装すると DI 解決失敗＋コンパイルエラーになるため差し戻し。
- 差し戻し後、修正版を再レビューして PASS。Minor 4 件も併せて反映済み。

## 前回指摘の解消状況（全5件 反映済み）
| # | 重大度 | 箇所 | 問題 | 修正版での対応 |
|---|---|---|---|---|
| 1 | Blocker | §2.2/§6/§7 | 「`MessagingBlobApiClient` を AddLineMessaging が DI 登録済み＝直接注入可」「`blob.Api.V2.Bot.Message[messageId].Content.GetAsync`」が両方誤り。DI 登録は facade の `MessagingClient` のみ（`MessagingBlobApiClient` 未登録＝直接注入で DI 解決失敗）、正しい取得は `client.Blob.V2.Bot.Message[messageId].Content.GetAsync(ct)`（`.Api` は制御プレーンで誤り、blob 直下は `.V2`）。中核機構が literal 実装で DI・コンパイル双方失敗 | `MessagingClient` を注入（既存 `LineMessenger` と同型）し `client.Blob.V2.Bot.Message[messageId].Content.GetAsync(ct)`→Stream で取得。`MessagingBlobApiClient` 直接注入しない旨・`.Api.V2` が誤りである旨も明記（§2.2/§6/§7）。SDK XMLdoc・実コードと一致（解消） |
| 2 | Minor | §2.1 | `ContentProvider.Type` を文字列 "line"/"external" 比較（実際は列挙型） | enum `ContentProvider_type.Line/.External`（null=Line）比較に修正（解消） |
| 3 | Minor | §2.3 | 受信 worker が AwaitingEdit を立てる前に編集テキストが届くレース未言及 | プロンプト到着前テキストは現在モードで解釈＝編集にならない、を期待挙動として文書化（実装変更不要）（解消） |
| 4 | Minor | §3/BotOptions.cs | 新規2キーを credentials 限定 doc の `LineOptions` へ追加する整合の言及なし | 2キーを `LineOptions` に追加し doc-comment を「認証情報＋受信画像の取得制約」へ更新する旨を追記（解消） |
| 5 | Minor | §2.1/WorkItem.cs | `WorkItem.Text` を messageId 転用、既存 doc は ImageEdit のみ言及 | `WorkItem` XML doc に「ReceiveImage=LINE messageId」を追記する旨を明記（解消） |

## チェックリスト
- [x] SDK 前提が正確（blob 取得の注入型・API パスが実 SDK・実コードと一致。`MessagingClient.Blob` データプレーン facade、`AddLineMessaging` は `MessagingClient` のみ登録を裏取り）
- [x] 受入基準が明確・テスト可能（§4、12項目＋テスト観点）
- [x] 決定事項が全確定（§6 [x]、TBD/曖昧表現なし）
- [x] スコープ整合（編集のみ／VQA・image→video 除外／external 非対応が本文・受入・決定で一貫）
- [x] 既存フロー再利用が妥当（HandleImageEditAsync／ImageEditService.GenerateAsync(byte[],string,ct)／MediaStore.Save／SetLastImageId／AwaitingEdit は実コードと一致。§2.4 変更なしは正しい）
- [x] idempotency（ProcessedEventStore.TryMarkNew 実在）・非機能（取得失敗通知／上限／タイムアウト／SSRF回避／PublicBaseUrl不要の切り分け／レース挙動）を明記

## 判定理由
Blocker 0・Major 0。初回 FAIL の Blocker 1・Minor 4 はすべて実 SDK・実コードと整合する形で修正版へ反映済み。中核の受信画像取得は `MessagingClient.Blob` データプレーン経由に是正され、enum 判定・レース挙動・設定/doc 反映も明確化。受入基準は検証可能で、状態遷移・原子的更新・idempotency・エラー処理が具体的。実装フェーズへ進める品質。

## 実装フェーズ着手時の確認観点（次ゲート引き継ぎ）
- LineContentService が MessagingClient 注入＋client.Blob.V2...Content.GetAsync(ct) 経路で実装されているか
- ContentProvider_type の enum 比較（null=Line）でルーティングしているか
- SetReceivedImage の 3項目単一ロック更新（原子性）とテスト
- ReadCappedAsync のヘルパー切り出し・上限/空ストリーム境界テスト
