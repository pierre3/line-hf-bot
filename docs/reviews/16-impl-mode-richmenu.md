# 実装レビュー — spec 03 フェーズ3a（モード状態+リッチメニュー+i18n） (2026-08-15)
Verdict: PASS
委譲分析: dotnet-claude-kit:code-review（Roslyn MCP 未接続のため手動フォールバック。ビルド0警告/0エラーで補完）

## 指摘
| # | 重大度 | 箇所 | 問題 | 必要な対応 |
|---|---|---|---|---|
| 1 | Minor | QuickReplyFactory.cs:15 / UserMessages.cs:38 | 画像結果QRが[🔄][💬]のみで✏️編集を含まない（AC#3は[🔄][✏️][💬]） | 編集は仕様上3b。意図的で整合。対応不要（記録のみ） |
| 2 | Minor | RichMenuManager.cs:63 | provisioning部分失敗時に作成済みメニューが孤立し得る | ベストエフォート方針で許容。将来 per-mode try/catch を検討 |
| 3 | Minor | UserStateStore.cs:67 / MessageDispatcher | SetAwaitingEdit/キャンセル遷移は3a未使用（3b用スキャフォールド） | 3aで機能欠落なし。3bで実装。対応不要 |
| 4 | Minor | MessageDispatcher.cs:76 | タブタップ由来postbackでもSyncUserMenu(LinkToUser)が重複発火 | 冪等で無害。対応不要 |

## 判定理由
Blocker 0 / Major 0 / Minor 4。ビルド0警告/0エラー。モード解釈（素テキストvs/上書き・postbackパース）、UserStateStoreのスレッド安全性（ConcurrentDictionary+entry lock、ChatHistoryStore同型）、リッチメニュー冪等provisioning（alias判定・失敗でアプリを落とさない・SyncUserMenuベストエフォート）、Kiota判別子明示、i18n（en/ja・SystemPrompt locale化）、直前画像ID整合、外部I/O失敗通知・秘密情報非出力をすべて確認。AC#3の✏️編集とAC#5は仕様どおり3bへ意図的後回し。差し戻すべきBlocker/Majorなしで PASS。次はセキュリティレビュー。
