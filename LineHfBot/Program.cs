using System.Text;
using Line.OpenApi.Messaging.DependencyInjection;
using Line.OpenApi.Messaging.Webhook;
using Line.OpenApi.Messaging.Webhook.DependencyInjection;
using LineHfBot.Ai;
using LineHfBot.Chat;
using LineHfBot.Configuration;
using LineHfBot.Line;
using LineHfBot.Media;
using LineHfBot.Messaging;
using LineHfBot.Queue;
using LineHfBot.State;
using LineHfBot.Text;
using Microsoft.SemanticKernel;

// Force UTF-8 console output so Japanese log text is not garbled on Windows.
// Setting this can fail when there is no console or output is redirected, so guard it.
try { Console.OutputEncoding = Encoding.UTF8; } catch { /* ignore */ }

var builder = WebApplication.CreateBuilder(args);

// --- Options (validated at startup) ---
builder.Services.AddBotOptions(builder.Configuration);

// --- LINE webhook parser (signature verification) ---
builder.Services.AddLineWebhook(o =>
    o.ChannelSecret = builder.Configuration[$"{LineOptions.Section}:{nameof(LineOptions.ChannelSecret)}"] ?? "");

// --- LINE messaging client (reply / push) ---
builder.Services.AddLineMessaging(o =>
    o.ChannelAccessToken = builder.Configuration[$"{LineOptions.Section}:{nameof(LineOptions.ChannelAccessToken)}"] ?? "");

// --- Semantic Kernel chat completion via the Hugging Face connector ---
var hf = builder.Configuration.GetSection(HuggingFaceOptions.Section).Get<HuggingFaceOptions>() ?? new HuggingFaceOptions();
builder.Services.AddHuggingFaceChatCompletion(
    model: hf.ChatModel,
    endpoint: string.IsNullOrWhiteSpace(hf.ChatEndpoint) ? null : new Uri(hf.ChatEndpoint),
    apiKey: hf.ApiKey);

// --- App services ---
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<UserMessages>();
builder.Services.AddSingleton<ChatHistoryStore>();
builder.Services.AddSingleton<UserStateStore>();
builder.Services.AddSingleton<IChatService, HuggingFaceChatService>();
builder.Services.AddSingleton<ILineMessenger, LineMessenger>();
builder.Services.AddSingleton<ILineContentService, LineContentService>();
builder.Services.AddSingleton<QuickReplyFactory>();
builder.Services.AddSingleton<RichMenuManager>();
builder.Services.AddSingleton<MediaStore>();
builder.Services.AddSingleton<ProcessedEventStore>();
// Disable auto-redirect: the JSON-URL re-fetch validates the host against the allowlist, so a 3xx
// must not silently follow into a non-allowlisted host (SSRF allowlist-bypass hardening).
builder.Services.AddHttpClient<IImageService, HuggingFaceImageService>(
        c => c.Timeout = System.Threading.Timeout.InfiniteTimeSpan) // per-request timeout is applied in the service
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<IVideoService, HuggingFaceVideoService>(
        c => c.Timeout = System.Threading.Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<IImageEditService, HuggingFaceImageEditService>(
        c => c.Timeout = System.Threading.Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

// --- Background queue ---
builder.Services.AddSingleton<IWorkQueue, ChannelWorkQueue>();
builder.Services.AddScoped<IWorkProcessor, WorkProcessor>();
builder.Services.AddSingleton<MessageDispatcher>();
builder.Services.AddHostedService<GenerationWorker>();
builder.Services.AddHostedService<RichMenuProvisioner>();

var app = builder.Build();

// Health check.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Serve generated media (LINE fetches image/video from here). 404 when expired/unknown.
app.MapGet("/media/{id}", (string id, MediaStore store) =>
    store.TryGet(id, out var media) && media is not null
        ? Results.File(media.Bytes, media.ContentType)
        : Results.NotFound());

// Placeholder preview image required for LINE video messages.
app.MapGet(VideoPreview.Path, () => Results.File(VideoPreview.Bytes, VideoPreview.ContentType));

// LINE webhook: verify the signature, then return 2xx immediately.
// Heavy work is handed off to the background queue (LINE recommends async processing).
app.MapPost("/webhook", async (
    HttpRequest request,
    WebhookRequestParser parser,
    MessageDispatcher dispatcher,
    ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("Webhook");

    // The signature is computed over the raw body, so read the bytes as-is.
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    var body = ms.ToArray();
    var signature = request.Headers["x-line-signature"].ToString();

    try
    {
        var callback = await parser.ParseAsync(body, signature);

        // Parse events and enqueue them; the worker does the heavy lifting.
        await dispatcher.DispatchAsync(callback, request.HttpContext.RequestAborted);

        // Return 2xx right away without waiting for generation to finish.
        return Results.Ok();
    }
    catch (WebhookSignatureException)
    {
        logger.LogWarning("Webhook signature verification failed.");
        return Results.Unauthorized();
    }
    catch (WebhookPayloadException ex)
    {
        logger.LogWarning(ex, "Failed to parse webhook payload.");
        return Results.BadRequest();
    }
});

// Development-only diagnostic: exercise the Hugging Face chat path in isolation (no LINE).
// Reports the error/timeout in the body so problems are visible in the curl output.
// Never mapped in Production. Example: GET /dev/chat?message=hello
if (app.Environment.IsDevelopment())
{
    app.MapGet("/dev/chat", async (
        string message,
        IChatService chat,
        Microsoft.Extensions.Options.IOptions<HuggingFaceOptions> hf,
        CancellationToken ct) =>
    {
        var timeout = Math.Max(5, hf.Value.ChatTimeoutSeconds);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeout));
        try
        {
            var answer = await chat.CompleteAsync("dev-user", message ?? "", cts.Token);
            return Results.Text(string.IsNullOrWhiteSpace(answer) ? "(empty answer)" : answer);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return Results.Text($"(timeout after {timeout}s)");
        }
        catch (Exception ex)
        {
            return Results.Text($"ERROR {ex.GetType().Name}: {ex.Message}");
        }
    });

    // Dev-only: exercise the HF image path in isolation. Returns the image bytes, or an error string.
    // Example: GET /dev/image?prompt=a%20cat  (curl -o out.png)
    app.MapGet("/dev/image", async (string prompt, IImageService images, CancellationToken ct) =>
    {
        try
        {
            var media = await images.GenerateAsync(prompt ?? "", ct);
            return Results.File(media.Bytes, media.ContentType);
        }
        catch (Exception ex)
        {
            return Results.Text($"ERROR {ex.GetType().Name}: {ex.Message}");
        }
    });

    // Dev-only: exercise the image-to-image edit path end-to-end. Generates a reference image via
    // text-to-image, then edits it with the given instruction. Returns the edited bytes, or an error.
    // Example: GET /dev/imageedit?prompt=make%20it%20night  (curl -o out.png)
    app.MapGet("/dev/imageedit", async (
        string prompt, IImageService images, IImageEditService edit, CancellationToken ct) =>
    {
        try
        {
            var reference = await images.GenerateAsync("a simple daytime landscape photo", ct);
            var media = await edit.GenerateAsync(reference.Bytes, prompt ?? "make it night", ct);
            return Results.File(media.Bytes, media.ContentType);
        }
        catch (Exception ex)
        {
            return Results.Text($"ERROR {ex.GetType().Name}: {ex.Message}");
        }
    });

    // Dev-only: exercise the HF video path in isolation. Returns the video bytes, or an error string.
    // Example: GET /dev/video?prompt=a%20running%20cat  (curl -o out.mp4)
    app.MapGet("/dev/video", async (string prompt, IVideoService videos, CancellationToken ct) =>
    {
        try
        {
            var media = await videos.GenerateAsync(prompt ?? "", ct);
            return Results.File(media.Bytes, media.ContentType);
        }
        catch (Exception ex)
        {
            return Results.Text($"ERROR {ex.GetType().Name}: {ex.Message}");
        }
    });
}

app.Run();
