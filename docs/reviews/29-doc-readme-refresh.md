# ドキュメントレビュー — README 整備（リポジトリ現状整合） (2026-08-15)
Verdict: PASS
委譲分析: なし（自前）
対象ブランチ: docs/readme-refresh（直前 main ecaba4c）

## 整合チェックリスト
- [x] 設定整合: README(EN/JA) Config 表 = BotOptions.cs = .env.example = appsettings.json で過不足なく一致
  - App__Locale=en / App__RichMenuEnabled=true / App__VideoEnabled=false / App__MediaTtlMinutes=10（AppOptions）
  - Line__MaxIncomingImageBytes=10485760(10MB) / Line__ContentFetchTimeoutSeconds=30（LineOptions）
  - HuggingFace__ImageEditModel=Qwen/Qwen-Image-Edit / MediaRefetchAllowedHosts=fal.media;replicate.delivery（空=全拒否）
  - HuggingFace__ChatModel=Qwen/Qwen2.5-7B-Instruct（appsettings.json、"non-gated" 記述妥当）
- [x] Features/Commands: チャット履歴／画像生成／🔄再生成・✏️編集(img2img Qwen/Qwen-Image-Edit)・💬チャット／写真受信→編集／モード＋リッチメニュー／i18n(en/ja)／動画既定オフ が実装と一致
- [x] 手順の再現性: LINE チャネル→トークン→.env→docker compose up→devtunnel→line webhook set-endpoint が順序どおり
- [x] コマンド正確性: docker compose up --build / dotnet run --project LineHfBot --urls / devtunnel host -p 8080 / dotnet tool install -g Line.OpenApi.Tools + line webhook
- [x] エンドポイント: /webhook /media/{id} /health = Program.cs と一致
- [x] Documentation 節: spec 01-04 が docs/specs/ 実在ファイルと一致、docs/reviews/ リンク実在
- [x] 言語ルール: README 二言語・EN/JA 本文対応・内部 doc 日本語・コメント/ログ英語
- [x] 秘密情報: .env.example は空プレースホルダのみ、実トークン混入なし
- [x] EN/JA 対応: Documentation 節の重複リンク（指摘1）を修正し EN/JA 対応化

## 指摘
| # | 重大度 | 箇所 | 問題 | 対応 |
| --- | --- | --- | --- | --- |
| 1 | Minor | README.md Documentation 節 | CLAUDE.md リンクが2行重複（"Contributor and architecture notes" と旧 "Developer guide"）。JA は1行のみで EN/JA 非対応 | 解消。EN を「Developer guide (architecture notes): CLAUDE.md」1行に統合し JA「開発ガイド」と対応化 |
| 2 | — | CLAUDE.md | ゲート指摘（アーキ節に写真受信→編集なし・Line 2キー未記載）は**誤検出**。CLAUDE.md は spec04 実装コミット(4a10d5a)で既に反映済み（写真受信→編集の記述 L22、`Line__MaxIncomingImageBytes`/`Line__ContentFetchTimeoutSeconds` L29） | 対応不要（現状で整合） |

## 判定理由
README.md / README.ja.md の全セクション（Features・Commands・Configuration・Documentation・セットアップ手順・エンドポイント）が実コード（BotOptions.cs / Program.cs）・.env.example・appsettings.json・docs/specs/01-04・docs/reviews/ と過不足なく一致。存在しないキー記載・必要キー漏れ・秘密情報混入なし。指摘 #1（EN 内の重複リンク）は本レビュー後に修正済み。#2 は誤検出で CLAUDE.md は既に現状反映済み。Blocker なしで PASS。
