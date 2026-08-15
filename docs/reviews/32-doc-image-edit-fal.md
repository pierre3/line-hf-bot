# ドキュメントレビュー — 画像編集 image-to-image を fal-ai 経由に対応 (spec 05) (2026-08-16)
Verdict: PASS
委譲分析: なし（自前）
対象範囲: git diff 96da177..HEAD（ブランチ fix/image-edit-fal-provider）
先行ゲート: 実装=docs/reviews/30 PASS / セキュリティ=docs/reviews/31 PASS

## 整合チェックリスト
- [x] 設定整合: ImageEditModel(fal-ai/qwen-image-edit)/ImageEditEndpoint(fal router?_subdomain=queue) が
      BotOptions.cs と .env.example/README(EN/JA)/CLAUDE.md/spec05§4 で過不足なく一致
- [x] ImageEditTimeoutSeconds=120 据え置き: BotOptions / .env / spec05§4 / CLAUDE 一致
- [x] 機能記述: submit→poll(router書換)→result→fal.media 取得 が ImageEditService.cs と一致
- [x] hf-inference 非対応 / fal 有料 の注記が README(EN/JA)・.env.example・CLAUDE・XMLドキュメントに明記
- [x] トークン境界: queue.fal.run 始まりのみ書換受理(ToRouterUrl) をテスト(allowlist外/http/サブドメイン偽装拒否)が裏付け
- [x] spec05 §2 ワイヤ形式が実装・ImageEditServiceTests.cs と一致
- [x] エンドポイント /webhook /media/{id} /health が Program.cs と一致
- [x] /dev/imageedit は Development 限定(Program.cs ガード)。dev 専用の位置づけと矛盾なし
- [x] コマンド(docker compose up --build / devtunnel host -p 8080) が README と一致
- [x] 言語ルール: README 二言語 / spec05 内部日本語 / コメント・XMLドキュメント英語
- [x] 秘密情報なし（.env.example は空プレースホルダのみ）
- [x] spec03(3b) の旧既定記述に spec05 への追従ポインタを追記（Minor #1 反映済み）

## 指摘
| # | 重大度 | 箇所 | 問題 | 対応 |
| --- | --- | --- | --- | --- |
| 1 | Minor | docs/specs/03-mode-context-richmenu.md §3 | 3b の編集既定が Qwen/Qwen-Image-Edit + hf-inference のまま。値は当時の記録として妥当だが spec05 への追従ポインタが無く単体で誤読の恐れ | 解消。spec03 §3 冒頭に「※既定は spec05 で fal-ai に変更。現行は spec05§4 参照」を追記（値は当時の記録として保持） |

## 判定理由
設定キー・機能記述・ワイヤ形式・エンドポイント・コマンド・二言語 README・秘密情報のいずれも
実コード(BotOptions.cs / ImageEditService.cs / Program.cs)およびテスト(ImageEditServiceTests.cs)と一致。
CLAUDE.md/README/.env.example/spec05 に旧 hf-inference 既定の残存なし。fal 有料の注意もユーザー向けに明記。
Blocker ゼロ。唯一の Minor（spec03 の追従ポインタ欠如）は本レビューで反映済み。よって PASS。
spec05 は 3ゲート（実装=30 / セキュリティ=31 / ドキュメント=32）すべて PASS。
