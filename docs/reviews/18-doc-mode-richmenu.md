# ドキュメントレビュー — モードコンテキスト+リッチメニュー+i18n（spec03 3a） (2026-08-15)
Verdict: PASS
委譲分析: なし（自前）

## チェックリスト / 指摘
| # | 重大度 | 箇所 | 問題 | 必要な対応 |
| --- | --- | --- | --- | --- |
| 1 | Minor | RichMenuManager.cs XMLドキュメント | 「slash command でモード変化」と読める記述。実際は slash はモード不変で SyncUserMenu 未呼出 | 記述を quick reply(back-to-chat) に限定（**修正済み**） |
| 2 | Minor | README.md/README.ja.md Features | 画像 QuickReply を 🔄 のみ記載、実際は 💬 も付与 | 💬 併記（**修正済み**） |
| 3 | Minor | appsettings.json App | Locale/RichMenuEnabled を明示列挙せず既定値依存 | 他キー同様で実害なし。未対応（任意） |

## 判定理由
Blocker/Major 0。App__Locale(en)/App__RichMenuEnabled(true) が BotOptions.cs・.env.example・CLAUDE.md・README(EN/JA)
でキー名・既定値・区切りとも完全一致。前回 doc ゲート(13) Major の「QuickReply 記述 vs 未実装」は
QuickReplyFactory 実装＋WorkProcessor での添付（画像=🔄/💬、動画=💬）で解消。✏️編集/image-to-image は 3b として
正しく未実装表記。言語ルール改訂（en/ja 切替を UserMessages.cs に集約）・リッチメニュー起動時 provisioning／
RichMenuEnabled=false 無効化・エンドポイント・秘密情報も整合。残指摘は Minor のみ（1・2 は反映済み）で差し戻し不要。PASS。
