# 仕様: 画像 Provider 統合（案A: 設定駆動の単一パス）

- 状態: ドラフト（仕様ゲート未実施）
- 対象: 拡張フェーズ / 画像生成の複数プロバイダ対応
- 関連: `docs/specs/01-line-hf-bot.md`、`docs/reviews/12-security-full-audit.md`（Low#1 SSRF）、`LineHfBot/Ai/ImageService.cs`、`LineHfBot/Ai/VideoService.cs`

## 1. 目的 / スコープ
`ImageService` を、HF Inference Providers 上の**複数プロバイダ（fal-ai / replicate / nebius / hf-inference 等）が配信する画像モデル**に対応させる。
現状は hf-inference 決め打ち・生バイト応答前提のため、FLUX 系など他プロバイダ配信モデル（JSON に URL を返す形式）で動作しない。これを解消する。

設計方針は **案A（設定駆動の単一パス）**: プロバイダ抽象化クラスは導入せず、`VideoService` と同じ「レスポンス形式の実行時判定」で吸収する。プロバイダ切替は環境変数（`ImageEndpoint` / `ImageModel`）で運用者が行う。

### スコープ外
- Provider ストラテジ抽象化（案B）。将来多プロバイダ前提になった時点で再検討。
- 画像加工（img2img / アップスケール）・image→video。別仕様で扱う。
- Provider ごとの個別 API キー管理（HF Router 経由のため `Bearer hf_***` 一本で足りる）。
- プロバイダ固有の高度パラメータ網羅（初版は最小限。§2.4 参照）。

## 2. 機能要件

### 2.1 レスポンス形式の自動判定（中核）
`ImageService.GenerateAsync` を、`VideoService` と同じ二分岐にする:
- `Content-Type` が画像バイト（`image/*` 等 JSON 以外）→ そのままバイト取得。
- `Content-Type` が JSON → ボディから画像 URL を抽出 → **自前で再取得**してバイト化。

抽出は共通のベストエフォート方式で、初版は次のスキーマに対応する: `url` / `output` / `image` / `images[0]`（配列先頭、要素が文字列 or `{url}`）/ `data[0].url`、および既存 `VideoService` が対応する `video`（文字列）/ `video.url`。共通ヘルパーは画像・動画両系のキーの**和集合**を扱い、既存の動画 JSON-URL 応答に回帰を出さないこと。抽出不能なら明示的に失敗（ユーザーへエラー通知、§2.5）。

### 2.2 JSON-URL 応答時の配信方針（確定）
プロバイダが URL を返す場合、**その URL を LINE に直接渡さず、自前で再取得して `GET /media/{id}` で配信する**。
- 理由: プロバイダ URL は失効・署名付きのことがあり永続保証がない／プロバイダ URL の外部露出を避ける／`MediaTtlMinutes` による TTL 管理を一元化。

### 2.3 共通化（リファクタ）
`ImageService` と `VideoService` に重複する「JSON からメディア URL 抽出 → 再取得」を共通ヘルパー（例: `HfHttp` もしくは `MediaFetch`）へ集約する。動画・画像で同じ再取得経路を通すことで、§2.4 の SSRF 対策も一箇所で効かせる。

### 2.4 SSRF 対策（セキュリティ Low#1 の同梱）
JSON-URL 再取得経路に多層防御を追加する（画像・動画共通）:
- **scheme は `https` のみ許可**（`http`・その他スキームは拒否）。
- **ホスト allowlist**: 許可ホストを設定で持ち、一致しない URL は拒否。既定は `fal.media` / `replicate.delivery` の2つ＋運用者が追記可能。**allowlist が空の場合は「全拒否」**（安全側フェイルクローズ。JSON-URL 再取得を使わない運用では実害なし）。
  - **一致規則（ラベル境界）**: URL のホストが「許可ホストと**完全一致**」または「`"." + 許可ホスト` で終わる」場合のみ許可。素朴な `EndsWith("fal.media")` は `evilfal.media` を誤許可するため**採用しない**。例: 許可 `fal.media` → `cdn.fal.media` は許可 / `evilfal.media` は拒否。
- 再取得の `HttpClient` に **Authorization を付与しない**（現状踏襲。第三者ホストへの資格情報同送を防ぐ）。
- 再取得にもタイムアウト（画像は `ImageTimeoutSeconds` 内、動画は `VideoTimeoutSeconds` 内）を適用。

### 2.5 エラー処理
- 非成功ステータスは既存 `HfHttp.EnsureSuccessAsync`（本文先頭500字をログ・エラーに含める）を踏襲。
- URL 抽出失敗・allowlist 拒否・scheme 拒否・再取得失敗は、ユーザーへ日本語で失敗通知（握りつぶさない）。開発者向けログは英語。

### 2.6 プロバイダ選択（運用）
- モデルは `HuggingFace__ImageModel` に設定。必要に応じ `:fastest` / `:cheapest` / `:preferred` サフィックスで自動ルーティング可能。
- エンドポイントは `HuggingFace__ImageEndpoint`（`{model}` 置換）で切替。プロバイダ固有パスが必要な場合も運用者が設定で吸収できる。

## 3. 設定（追加・変更）
| キー | 既定 | 説明 |
|---|---|---|
| `HuggingFace__MediaRefetchAllowedHosts` | `fal.media;replicate.delivery` | JSON-URL 再取得で許可するホスト（サフィックス一致、`;` 区切り or 配列）。画像・動画共通。空なら全拒否 |
| （既存）`HuggingFace__ImageEndpoint` | `.../hf-inference/models/{model}` | 変更なし。プロバイダ切替に使用 |
| （既存）`HuggingFace__ImageModel` | 現行既定 | `:cheapest` 等のサフィックス指定を許容 |

`.env.example` / `README.md` / `README.ja.md` / `CLAUDE.md` の設定表に追記する。

## 4. 受入基準（テスト可能）
1. hf-inference（生バイト）応答: 従来どおり画像を取得し `/media/{id}` 配信・LINE Push できる（回帰なし）。
2. JSON-URL 応答（fal-ai/replicate 形式のモック）: URL を抽出→再取得→`/media/{id}` 配信できる。
3. allowlist 外ホストの URL を含む JSON 応答: 再取得せず拒否し、ユーザーへ失敗通知する。
4. **ラベル境界バイパス**: 許可 `fal.media` に対し `evilfal.media` の URL を拒否し、`cdn.fal.media` は許可する。
5. `http://`（非 https）URL を含む JSON 応答: 拒否し失敗通知する。
6. URL 抽出不能な JSON 応答: 明示的に失敗し、ログに本文先頭が残る。
7. 再取得の HttpClient に Authorization ヘッダが付かないことを確認できる（DelegatingHandler 等で再取得 GET のヘッダを検査）。
8. `ImageService` / `VideoService` が共通の抽出・再取得ヘルパーを使用している（重複解消）。
9. **動画 JSON-URL 回帰**: 既存の `video` / `video.url` シェイプの動画応答が従来どおり抽出→再取得できる。
10. 追加設定キーが実コード（Options）とドキュメントで一致する。

## 5. 決定事項（旧・未解決点／2026-08-15 確定）
- [x] `MediaRefetchAllowedHosts` 既定は `fal.media;replicate.delivery`。**空なら全拒否**（フェイルクローズ）。運用者が追記可能。
- [x] リクエストボディは初版 **`{ inputs }` のみ**（現状踏襲）。`parameters`（size/steps 等）対応は後続仕様に切り出す。
- [x] JSON 抽出は初版で `url` / `output` / `image` / `images[0]` / `data[0].url` に対応（§2.1）。

## 6. 参考（実装の流用元）
- `VideoService.GenerateAsync`（`Content-Type` 判定 + `ExtractVideoUrl` + 再取得）をパターンごと画像へ横展開し、共通化する。
