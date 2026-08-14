---
name: line-webhook-test
description: Send a signed test event to the local LINE webhook without the real LINE app. Use when verifying the bot's webhook signature validation and message handling locally (e.g. "test the webhook", "send a fake LINE message", "check signature validation"). Generates a valid X-Line-Signature (HMAC-SHA256 of the raw body with the channel secret, Base64) and POSTs a sample event to /webhook.
---

# line-webhook-test

LINE 実機を使わずに、ローカル起動中のボット (`http://localhost:8080/webhook` など) へ
**正しい署名付き**のダミー Webhook イベントを送って検証するためのスキル。

LINE は `X-Line-Signature` を「リクエスト**生ボディ**を ChannelSecret で HMAC-SHA256 → Base64」で計算する。
署名は生バイトに対して計算するため、送信ボディと署名計算の入力を**完全一致**させることが重要。

## 使い方

1. ボットをローカル起動しておく（`dotnet run`。既定ポートを確認）。
2. ChannelSecret を用意（`.env` / appsettings の `Line__ChannelSecret`）。実際の値でなくても、
   起動中アプリに設定した値と**同じ**であればよい（署名検証を通すため）。
3. 下記 PowerShell 関数でイベントを送信する。テキストを変えて `/image` `/video` コマンドも試せる。

## PowerShell スクリプト（Windows 既定シェル）

```powershell
function Send-LineWebhookTest {
    param(
        [string]$Secret,                                   # Line__ChannelSecret と一致させる
        [string]$Text  = "こんにちは",                      # 送信メッセージ本文。/image ... /video ... も可
        [string]$Url   = "http://localhost:8080/webhook",
        [string]$UserId = "U0000000000000000000000000000000"
    )
    # LINE の webhook ペイロード（最小構成の messageEvent）
    $payload = @{
        destination = "xxxxxxxxxx"
        events = @(@{
            type = "message"
            mode = "active"
            timestamp = 1700000000000
            source = @{ type = "user"; userId = $UserId }
            webhookEventId = "01000000000000000000000000"
            deliveryContext = @{ isRedelivery = $false }
            replyToken = "00000000000000000000000000000000"
            message = @{ id = "1000000000000"; type = "text"; text = $Text; quoteToken = "q0000000000000000000000000000" }
        })
    }
    # ★ 署名は「送信する生ボディ」に対して計算する必要がある。ここで一度だけ JSON 化し使い回す。
    $body = $payload | ConvertTo-Json -Depth 10 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($body)

    $hmac = [System.Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($Secret))
    $sig  = [Convert]::ToBase64String($hmac.ComputeHash($bytes))

    Invoke-WebRequest -Uri $Url -Method Post `
        -ContentType "application/json" `
        -Headers @{ "X-Line-Signature" = $sig } `
        -Body $bytes | Select-Object StatusCode, Content
}

# 例:
# Send-LineWebhookTest -Secret $env:Line__ChannelSecret -Text "こんにちは"
# Send-LineWebhookTest -Secret $env:Line__ChannelSecret -Text "/image 夕日の海辺"
# Send-LineWebhookTest -Secret $env:Line__ChannelSecret -Text "/video 走る猫"
```

## 検証ポイント
- 正しい署名 → **200 OK**（アプリは即応し、生成はバックグラウンドで進む → Push で結果送信）。
- 署名を改ざん / Secret 不一致 → **401 Unauthorized**（署名検証が効いている証拠）。
- ボディを送信後に変えると署名不一致になる。**必ず同一 `$bytes` を署名計算と送信の両方に使う**こと。
- Push 送信は実チャネルの ChannelAccessToken が必要。ローカルのみで送信結果まで見たい場合は、
  `LineReplyService` をログ出力に差し替える等のスタブを検討する。

## 注意
- ここで扱う Secret / Token は秘密情報。ログや履歴に残さない。コマンド例では `$env:` から読む。
