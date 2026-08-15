# ドキュメントレビュー — ユーザー画像の受信→image-to-image 編集 (spec04) (2026-08-15)
Verdict: PASS
委譲分析: なし（自前）

## 整合チェックリスト
- [x] 設定整合: Line__MaxIncomingImageBytes(10485760=10MB) / Line__ContentFetchTimeoutSeconds(30) が BotOptions.cs(LineOptions) 既定値と .env.example/README(EN/JA)/CLAUDE.md/spec04§3 で一致
- [x] 機能記述: 受信→MessagingClient.Blob 取得→MediaStore 保存→SetReceivedImage(AwaitingEdit)→img2img が MessageDispatcher/LineContentService/WorkProcessor/UserStateStore と一致
- [x] external 非対応: ContentProvider_type.External を declined→ImageSourceUnsupported（MessageDispatcher.cs:98）。SSRF 回避（外部URL自前取得なし）と一致
- [x] 上限/タイムアウト: ReadCappedAsync + CancelAfter（LineContentService）と README「default 10 MB / 30 s」一致。cap 超過は ImageTooLargeException→ImageTooLarge
- [x] 原子性: UserStateStore.SetReceivedImage が LastImageId/LastPrompt=null/AwaitingEdit=true を単一 lock で更新
- [x] i18n: ImageReceived/ImageReceiveFailed/ImageTooLarge/ImageSourceUnsupported が en/ja 揃い、help に「写真を送ると編集」1行を en/ja 追記
- [x] README EN/JA: Features に「Edit your own photo」/「自分の写真を編集」、設定表に Line 2キー行を対応追記
- [x] 言語ルール: コメント/ログ英語・README 二言語・spec 内部日本語を順守
- [x] エンドポイント /webhook /media/{id} /health 実装一致（Program.cs、本 spec で変更なし）
- [x] コマンド（dotnet/docker/devtunnel）齟齬なし
- [x] 秘密情報: .env.example はプレースホルダのみ

## 指摘
| # | 重大度 | 箇所 | 問題 | 必要な対応 |
| --- | --- | --- | --- | --- |
| 1 | Minor | spec04:39,50 | 公開メソッド名 FetchAsync 記載だが実装は FetchImageAsync | 内部 spec を FetchImageAsync に修正（**本レビューで反映済み**） |
| 2 | Minor | spec04:3 | テスト件数(旧53=45+8)が最新実装と未突合 | 実装ゲート26 の実測に合わせ 57件（既存45＋新規12）へ更新（**本レビューで反映済み**） |

## 判定理由
新2キーは LineOptions 既定値と全ドキュメントで過不足なく一致。受信→編集フロー・external 非対応・SSRF 回避・上限/タイムアウト・モード非依存・原子的 SetReceivedImage の記述は実装と一致。en/ja 文言 4件＋help は揃い翻訳も自然。README EN/JA 対応・言語ルール・エンドポイント・コマンド・秘密情報も整合。指摘の Minor 2件（内部 spec の名称表記ずれ・テスト件数未突合）は本レビューで反映済み。Blocker なしで PASS。spec04 は 4ゲート（仕様25 / 実装26 / セキュリティ27 / ドキュメント28）すべて PASS。
