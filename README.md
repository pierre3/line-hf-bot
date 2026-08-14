# line-hf-bot

LINE から Hugging Face のモデルを使って **AI チャット・画像生成・動画生成** ができるボット。
ASP.NET (.NET 10) 製で、ローカル PC で Docker 実行 → トンネル公開 → LINE に接続するだけで手軽に使えることを目指します。
（クラウドホストも可能）

> ⚠️ **開発中（Work in Progress）** — 現在は仕様確定フェーズです。実装はこれから進みます。

## 特長（予定）
- 💬 テキストチャット（会話履歴を保持、Semantic Kernel の HuggingFace コネクタ）
- 🎨 画像生成（`/image <prompt>`）
- 🎬 動画生成（`/video <prompt>`）
- 🐳 Docker イメージとして配布。ローカル + Dev トンネルで手軽に、あるいはクラウドでホスト

## 技術スタック
- .NET 10 / ASP.NET Minimal API
- [pierre3/line-openapi-dotnet](https://github.com/pierre3/line-openapi-dotnet)（`Line.OpenApi.Bot`）
- [Semantic Kernel](https://github.com/microsoft/semantic-kernel)（HuggingFace コネクタ）
- Hugging Face Inference Providers（画像 / 動画）

## ドキュメント
- 仕様: [`docs/specs/01-line-hf-bot.md`](docs/specs/01-line-hf-bot.md)
- レビュー記録: [`docs/reviews/`](docs/reviews/)
- 開発ガイド: [`CLAUDE.md`](CLAUDE.md)

## ライセンス
未定（追って設定）。
