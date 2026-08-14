# line-hf-bot

[English](README.md) | 日本語

Hugging Face のモデルを使って、LINE で **AI とチャットしたり、画像や動画を作れる**ボットです。
ASP.NET（.NET 10）で作っています。手元の PC で Docker イメージを動かし、トンネルで公開して
LINE につなぐだけ、という手軽さを目指しています。もちろんクラウドに置いても動きます。

> ⚠️ **開発中**です。いまは仕様を固めて、実装を始めたところです。

## できること（予定）
- 💬 会話の流れを覚えるテキストチャット（Semantic Kernel の Hugging Face コネクタ）
- 🎨 画像の生成（`/image プロンプト`）
- 🎬 動画の生成（`/video プロンプト`）
- 🐳 Docker イメージとして配布。ローカル＋トンネルで手軽に、クラウドでの運用も可能。

## 使っている技術
- .NET 10 / ASP.NET Minimal API
- [pierre3/line-openapi-dotnet](https://github.com/pierre3/line-openapi-dotnet)（`Line.OpenApi.Bot`）
- [Semantic Kernel](https://github.com/microsoft/semantic-kernel)（Hugging Face コネクタ）
- Hugging Face Inference Providers（画像・動画）

## ドキュメント
- 仕様: [`docs/specs/01-line-hf-bot.md`](docs/specs/01-line-hf-bot.md)
- レビュー記録: [`docs/reviews/`](docs/reviews/)
- 開発ガイド: [`CLAUDE.md`](CLAUDE.md)

## ライセンス
まだ決めていません。
