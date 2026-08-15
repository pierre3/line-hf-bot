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
- 2026-08-15 **PASS** ドキュメントレビュー（画像Provider統合・案A / spec02）— [`21-doc-image-provider.md`](21-doc-image-provider.md)（Blocker/Major 0、Minor 1。`HuggingFace__MediaRefetchAllowedHosts`（既定 `fal.media;replicate.delivery`・画像/動画共通・ラベル境界一致・空=全拒否）が BotOptions.cs と .env.example/README×2/CLAUDE.md/spec/テストAC#10 で完全一致。CLAUDE.md「HF Inference」本文の JSON(URL) 両対応＋SSRF ガード改訂が実装と整合。エンドポイント・`dotnet test`(36件)・秘密情報も整合。残 Minor＝内部仕様§3表の「サフィックス一致」表現は本レビューで「ラベル境界一致」へ統一済み。spec02の4ゲート全PASS）
- 2026-08-15 **PASS** セキュリティレビュー（画像Provider統合・案A / spec02）— [`20-security-image-provider.md`](20-security-image-provider.md)（Critical/High/Medium 0、Low 2・Info 2、脆弱パッケージ0。SSRF必須統制(scheme=https/allowlistラベル境界/空=全拒否フェイルクローズ/no-auth/timeout)を実装・テスト実証。実装ゲート引継ぎのMajor#1=リダイレクトによるallowlist迂回は Image/Video両typed clientの `AllowAutoRedirect=false` で正式クローズ確認。userinfo@トリック無効・秘密情報非露出・署名検証(記録17)回帰なし。委譲: dotnet-claude-kit:security-scan / security-auditor）
- 2026-08-15 **PASS** 実装レビュー（画像Provider統合・案A / spec02）— [`19-impl-image-provider.md`](19-impl-image-provider.md)（Blocker 0、Major 1・Minor 3。ビルド0警告/0エラー・Roslyn診断0・テスト36緑。SSRF必須統制(scheme/allowlistラベル境界/フェイルクローズ/no-auth/timeout)は実装・テスト済み。唯一のMajorはHttpClientのAllowAutoRedirect未無効化=allowlistリダイレクトバイパスの上乗せ強化で、攻撃者誘発経路なし・資格情報漏えいなしのためブロックせず、securityゲートへクローズ引継ぎ。委譲: dotnet-claude-kit:code-review / Roslyn MCP接続あり）
- 2026-08-15 **PASS** ドキュメントレビュー（モードコンテキスト+リッチメニュー+i18n / spec03 3a）— [`18-doc-mode-richmenu.md`](18-doc-mode-richmenu.md)（Blocker/Major 0、Minor 3。App__Locale/App__RichMenuEnabled が実コードと全ドキュメントで一致。前回(13)Major の QuickReply 齟齬は 🔄/💬 実装＋添付で解消。✏️編集=3b は未実装として正しく表記）
- 2026-08-15 **PASS** セキュリティレビュー（モードコンテキスト+リッチメニュー+i18n / spec03 3a）— [`17-security-mode-richmenu.md`](17-security-mode-richmenu.md)（Critical/High/Medium 0、Low 1・Info 1、脆弱パッケージ0。署名検証は回帰なし・トークン非露出・postback入力は安全に無視・locale由来のパストラバーサルなし。SSRFは3a新経路なし）
- 2026-08-15 **PASS** 実装レビュー（モードコンテキスト+リッチメニュー+i18n / spec03 3a）— [`16-impl-mode-richmenu.md`](16-impl-mode-richmenu.md)（Blocker/Major 0、Minor 4。ビルド0警告/0エラー。UserStateStore並行性・冪等provisioning・Kiota判別子・i18nを確認。✏️編集/AC#5は仕様どおり3bへ後回し。Roslyn MCP未接続で手動フォールバック）
- 2026-08-15 **PASS** 仕様レビュー（モードコンテキスト+リッチメニュー+画像セッション+i18n）— [`15-spec-mode-richmenu.md`](15-spec-mode-richmenu.md)（初回 FAIL→修正反映で再レビュー PASS。Blocker 0、前回 Major 3・Minor 3＋残 Minor 1 を全反映。3a は spec02 非依存で着手可）
- 2026-08-15 **PASS** 仕様レビュー（画像Provider統合・案A）— [`14-spec-image-provider.md`](14-spec-image-provider.md)（Blocker 0、Major 2・Minor 1 は仕様へ反映済み。allowlist のラベル境界明文化と動画 JSON-URL 回帰防止を対応）
- 2026-08-15 **PASS** ドキュメントレビュー（全体統合点検）— [`13-doc-full-audit.md`](13-doc-full-audit.md)（Blocker 0、Major 1・Minor 2。CLAUDE.md の QuickReply 記述が未実装＝要修正だが再現性・設定整合は全一致で PASS）
- 2026-08-15 **PASS** セキュリティレビュー（全体統合点検）— [`12-security-full-audit.md`](12-security-full-audit.md)（Critical/High/Medium 0、Low 2・Info 3、脆弱パッケージ0。SSRFはトークン非同送・video既定オフでLow止まり）
- 2026-08-15 **PASS** 実装レビュー（全体統合点検）— [`11-impl-full-audit.md`](11-impl-full-audit.md)（Blocker/Major 0、Minor 5。ビルド警告0/エラー0。Roslyn MCP 未接続のため手動フォールバック）
- 2026-08-14 **PASS** ドキュメントレビュー（image/video/docker）— [`10-doc-image-video-docker.md`](10-doc-image-video-docker.md)（Minor 2件を修正）
- 2026-08-14 **PASS** セキュリティレビュー（image/video/docker）— [`09-security-image-video-docker.md`](09-security-image-video-docker.md)（Low 1・Info 2、脆弱パッケージ0）
- 2026-08-14 **PASS** 実装レビュー（image/video/docker）— [`08-impl-image-video-docker.md`](08-impl-image-video-docker.md)（Minor 4、OCE 除外を修正）
- 2026-08-14 **PASS** ドキュメントレビュー（chat増分）— [`07-doc-chat.md`](07-doc-chat.md)（初回 FAIL→`ChatEndpoint` 記載を追記して PASS）
- 2026-08-14 **PASS** セキュリティレビュー（chat増分）— [`06-security-chat.md`](06-security-chat.md)（Critical/High/Medium なし。worker タイムアウトを追加対応）
- 2026-08-14 **PASS** 実装レビュー（chat増分）— [`05-impl-chat.md`](05-impl-chat.md)（Blocker/Major なし、Minor 3。worker タイムアウトを追加対応）
- 2026-08-14 **PASS** ドキュメントレビュー（phase2-scaffold）— [`04-doc-phase2-scaffold.md`](04-doc-phase2-scaffold.md)（初回 FAIL→設定キー整合を修正して PASS）
- 2026-08-14 **PASS** セキュリティレビュー（phase2-scaffold）— [`03-security-phase2-scaffold.md`](03-security-phase2-scaffold.md)（Critical/High/Medium なし、脆弱パッケージ0）
- 2026-08-14 **PASS** 実装レビュー（phase2-scaffold）— [`02-impl-phase2-scaffold.md`](02-impl-phase2-scaffold.md)（Blocker/Major なし、Minor 4）
- 2026-08-14 **PASS** 仕様レビュー — [`01-spec-line-hf-bot.md`](01-spec-line-hf-bot.md)（Major #1 タイムアウトを仕様反映）
