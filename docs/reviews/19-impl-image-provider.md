# 実装レビュー — 画像 Provider 統合（案A / spec02） (2026-08-15)
Verdict: PASS
委譲分析: dotnet-claude-kit:code-review（Roslyn MCP: detect_antipatterns / get_diagnostics 実行・接続あり）

## 対象差分（feat/mode-richmenu-spec 上の未コミット作業ツリー）
新規: `LineHfBot/Ai/MediaUrlExtractor.cs`, `LineHfBot/Ai/MediaRefetch.cs`, `LineHfBot.Tests/`（36件）
変更: `LineHfBot/Ai/ImageService.cs`, `LineHfBot/Ai/VideoService.cs`, `LineHfBot/Configuration/BotOptions.cs`, `LineHfBot/LineHfBot.csproj`, ドキュメント（.env.example / README×2 / CLAUDE.md）

## チェックリスト / 指摘
| # | 重大度 | 箇所 | 問題 | 必要な対応 |
|---|---|---|---|---|
| 1 | Major(defense-in-depth) | MediaRefetch.cs:37 + Program.cs:51-54 | 再取得の `HttpClient`（typed client）が既定ハンドラで `AllowAutoRedirect=true`。allowlist 検証は初回 URL のみに掛かり、許可ホストが 3xx で内部/別ホストへリダイレクトすると再検証なしに追従＝allowlist 境界を越えうる（既知の SSRF allowlist バイパス）。緩和要因: URL 生成元は provider インフラ（fal.media/replicate.delivery）で攻撃者はプロンプトしか制御できず「許可ホスト自身が内部へリダイレクト」を誘発する経路が無い／再取得に Authorization 非同送で資格情報漏えいなし／.NET 既定は https→http ダウングレード追従を禁止。spec §2.4 の必須統制（scheme/allowlist/フェイルクローズ/no-auth/timeout）はすべて実装済みで、リダイレクト無効化は spec 未記載の上乗せ強化。 | 次工程 security-review-gate で正式評価・クローズ。実装は `AllowAutoRedirect=false`（or 追従時に allowlist 再検証）を推奨 |
| 2 | Minor | ImageService.cs:44-45 / VideoService.cs:44-45 | AC#6 は「URL 抽出不能時にログへ本文先頭が残る」を要求するが、抽出失敗パスは本文を含めず汎用メッセージで throw。本文先頭ログは非成功ステータス（HfHttp.EnsureSuccessAsync）のみ。 | 抽出失敗時も応答本文先頭（500字）をログ/例外に含めると AC#6 と厳密一致 |
| 3 | Minor | MediaRefetch.cs:40 | 再取得 `ReadAsByteArrayAsync` にサイズ上限なし（既定 2GB）。悪意/巨大メディアで大量メモリ確保の可能性。既存バイト経路と同パターンで回帰ではない。 | 上限（MaxResponseContentBufferSize 等）検討は任意 |
| 4 | Minor | HfHttp.cs:19 | 本文読取の bare `catch`（AP005）。ベストエフォート読取のフォールバックで意図的。 | 対応不要（許容） |

## 委譲分析の結果
- `get_diagnostics(LineHfBot)`: 0 error / 0 warning / 0 info（本体ビルド 0 警告・0 エラーを裏付け）。
- `detect_antipatterns`: 15件検出だが spec02 新規コードに起因する重大パターンは無し。大半は既存（LineMessenger の catch(Exception)、dev エンドポイント、Worker 群）。新規/隣接は HfHttp の bare catch のみ（意図的・上表#4）。
- `dotnet test`: 36件全合格（ユーザー報告、本ゲートでは再実行せず）。

## 受入基準 §4（1-10）照合
- 1 生バイト回帰 / 2 JSON-URL 抽出→再取得→配信 / 3 allowlist 外拒否＋通知 / 4 ラベル境界（evilfal 拒否・cdn 許可）/ 5 http 拒否 / 7 再取得 no-auth / 8 共通ヘルパー共有 / 9 動画 video・video.url 回帰 / 10 設定キー整合（CLAUDE.md・README×2・.env.example に `MediaRefetchAllowedHosts` 存在）: いずれも実装・テストで満たす。
- 6 抽出不能時のログ本文先頭: 部分達成（上表#2 の Minor）。
- SSRF 統制（§2.4）: scheme=https 限定・allowlist ラベル境界一致・空=全拒否フェイルクローズ・Authorization 非同送・呼び出し側 cts.Token でのタイムアウトを MediaRefetch.cs で正しく実装。

## 判定理由
Blocker 0。全受入基準を実質達成し、ビルド 0/0・テスト 36 緑・Roslyn 診断 0。唯一の非自明指摘（#1 リダイレクト追従の allowlist バイパス）は spec §2.4 の必須統制をすべて満たした上での上乗せ強化で、(a) 攻撃者が誘発できる経路が無く実効的悪用可能性が極めて低い、(b) 資格情報漏えいなし、(c) 次ゲート（security-review-gate → security-scan）が SSRF を重点評価する所掌、という理由から本実装ゲートをブロックしない。ただし Major 相当の可視性を持たせ、security ゲートでの正式クローズ（`AllowAutoRedirect=false` 等の実装 or リスク受容）を条件として引き継ぐ。スレッド安全性（MediaUrlExtractor/MediaRefetch は不変・静的で状態なし）、外部 I/O 失敗のユーザー通知（WorkProcessor が例外を捕捉し `messages.Error` を送出・握りつぶさない）、秘密情報非出力（例外本文は provider 応答のみで自鍵は含めない）を確認。
