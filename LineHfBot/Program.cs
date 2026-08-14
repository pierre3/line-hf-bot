using System.Text;
using Line.OpenApi.Messaging.Webhook;
using Line.OpenApi.Messaging.Webhook.DependencyInjection;
using LineHfBot.Configuration;

// Windows コンソールでの日本語ログ文字化けを防ぐため UTF-8 に固定。
// コンソールが無い/リダイレクト時に失敗し得るため防御的に握る。
try { Console.OutputEncoding = Encoding.UTF8; } catch { /* ignore */ }

var builder = WebApplication.CreateBuilder(args);

// --- 設定 (Options) ---
builder.Services.Configure<LineOptions>(builder.Configuration.GetSection(LineOptions.Section));
builder.Services.Configure<HuggingFaceOptions>(builder.Configuration.GetSection(HuggingFaceOptions.Section));
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.Section));
builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection(QueueOptions.Section));
builder.Services.Configure<ChatOptions>(builder.Configuration.GetSection(ChatOptions.Section));

// --- LINE Webhook パーサ (署名検証) ---
builder.Services.AddLineWebhook(o =>
    o.ChannelSecret = builder.Configuration[$"{LineOptions.Section}:{nameof(LineOptions.ChannelSecret)}"] ?? "");

var app = builder.Build();

// ヘルスチェック
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// LINE Webhook: 署名検証 → 即 2xx 応答（重い処理は後続増分でキューへ）
app.MapPost("/webhook", async (HttpRequest request, WebhookRequestParser parser, ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("Webhook");

    // 署名は生ボディに対して検証する必要があるため、生バイトを読む。
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    var body = ms.ToArray();
    var signature = request.Headers["x-line-signature"].ToString();

    try
    {
        var callback = await parser.ParseAsync(body, signature);

        // TODO(キュー増分): イベントをバックグラウンドキューへ enqueue する。現時点はログのみ。
        foreach (var ev in callback.Events ?? [])
        {
            logger.LogInformation("受信イベント: {EventType}", ev.GetType().Name);
        }

        // 生成完了を待たず即座に 2xx を返す（LINE 推奨の非同期処理）。
        return Results.Ok();
    }
    catch (WebhookSignatureException)
    {
        logger.LogWarning("Webhook 署名検証に失敗しました。");
        return Results.Unauthorized();
    }
    catch (WebhookPayloadException ex)
    {
        logger.LogWarning(ex, "Webhook ペイロードの解析に失敗しました。");
        return Results.BadRequest();
    }
});

app.Run();
