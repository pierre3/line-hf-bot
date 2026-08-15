# セキュリティレビュー — spec03 フェーズ3a（モード状態＋リッチメニュー＋i18n / commit 301bca2） (2026-08-15)
Verdict: PASS
委譲分析: dotnet-claude-kit:security-scan（6レイヤ静的スキャン。42crunch/claude-security は本フェーズ無新規API面のため不要と判断）

## レイヤ結果
| レイヤ | 状態 | 結果 |
|---|---|---|
| 1. 脆弱パッケージ | PASS | 0件（新規NuGet追加なし。csproj差分はPNGアセットの`<Content Include>`のみ） |
| 2. シークレット検出 | PASS | 露出0。ChannelAccessToken は `RichMenuClient.CreateWithStaticToken` へ渡すのみでログ非出力。`.env.example`は空値、`.gitignore`は`.env`/`.env.*`除外 |
| 3. OWASPパターン | PASS | SQL/XSS/危険逆シリアライズ/弱ハッシュ 無し。postbackパース・パストラバーサル安全 |
| 4. 認証/エンドポイント | PASS | 新規エンドポイントなし。Webhook署名検証 無変更・回帰なし |
| 5. CORS | N/A | 設定なし・無変更 |
| 6. データ保護 | PASS | ログはLINE userId（擬似ID）＋modeのみ。既存enqueueログと同水準、新規PIIクラスなし |

## 指摘
| # | 重大度 | 箇所 | 脆弱性/リスク | 必要な対応 |
|---|---|---|---|---|
| 1 | Low | State/UserStateStore.cs:31,89 | per-user状態を`ConcurrentDictionary`で保持、`/reset`以外に退避/TTLなし。多数の異なるuserIdでエントリ単調増加 | 既存 `ChatHistoryStore`/`ProcessedEventStore` と同一パターンで悪化なし。1ユーザ占有は極小。個人・小規模用途で受容可。将来 idle TTL 退避を検討（記録のみ、PASS阻害せず） |
| 2 | Info | Messaging/MessageDispatcher.cs:77 | mode変更時に userId をログ出力 | LINE userId は擬似識別子。既存 enqueue ログと同水準。是正不要 |

## 重点確認の結論
- **Webhook署名検証（回帰）**: 問題なし。生ボディbytesを `parser.ParseAsync(body, signature)` で検証→失敗は 401。新規 `PostbackEvent` 処理は `DispatchAsync` 内で**検証成功後にのみ**実行（署名前処理なし）。
- **トークン漏洩**: なし。ChannelAccessToken は `CreateWithStaticToken` に渡すのみ。ログ出力箇所ゼロ。
- **入力検証/インジェクション**: 安全。`ParsePostback` は `&`/`=` 分割の O(n) パースで小固定長データのみ。未知 action→即return、未知 value→`TryParseMode` false で `when` ガード不発（既定Chatへ遷移せず無視）。postback は署名済みWebhook経由のみ＝偽造不可。ユーザ由来テキストは RichMenu API に流れない（Data/Label は enum・固定文字列由来）。
- **パストラバーサル（画像）**: なし。`_locale` は `"ja"` 完全一致以外すべて `"en"` 正規化。ファイル名は enum 由来。外部入力がパスに混入しない。
- **メモリ/DoS**: 上記 #1。既存ストアと同水準。生成要求は Bounded Queue(100)＋重複排除で保護。regen も独自 eventId で重複排除機能。
- **脆弱NuGet**: 0件。**SSRF**: 本フェーズ新経路なし（画像編集=3b）。`/media/{id}` 未変更。

## 判定理由
Critical/High/Medium 0件。署名検証は不変で回帰なし、トークン非露出、postback パースは想定外入力を安全に無視、locale 由来のパストラバーサル余地なし。残存は Low 1・Info 1 のみで既存実装と同一パターンかつ緩和済み。よって PASS。次ゲート（ドキュメント）へ進行可。
