namespace LineHfBot.Configuration;

/// <summary>LINE Messaging API credentials (section: "Line"). Secret; supply via configuration/environment.</summary>
public sealed class LineOptions
{
    public const string Section = "Line";
    public string ChannelSecret { get; set; } = "";
    public string ChannelAccessToken { get; set; } = "";
}

/// <summary>Hugging Face settings (section: "HuggingFace").</summary>
public sealed class HuggingFaceOptions
{
    public const string Section = "HuggingFace";
    public string ApiKey { get; set; } = "";
    public string ChatModel { get; set; } = "";
    public string ImageModel { get; set; } = "";
    public string VideoModel { get; set; } = "";

    /// <summary>
    /// Chat completion base URL. Defaults to the Hugging Face router; the SK connector
    /// appends "/v1/chat/completions", so this must NOT include the "/v1" suffix.
    /// Adjust if your model/provider needs a different base URL.
    /// </summary>
    public string ChatEndpoint { get; set; } = "https://router.huggingface.co";

    /// <summary>
    /// Text-to-image endpoint template. "{model}" is replaced with <see cref="ImageModel"/>.
    /// Image support is provider-dependent; adjust the provider segment/model to one that serves
    /// text-to-image and returns raw image bytes.
    /// </summary>
    public string ImageEndpoint { get; set; } = "https://router.huggingface.co/hf-inference/models/{model}";

    /// <summary>
    /// Text-to-video endpoint template. "{model}" is replaced with <see cref="VideoModel"/>.
    /// Video support is provider-dependent; some providers return raw bytes, others return JSON
    /// containing a video URL (both are handled).
    /// </summary>
    public string VideoEndpoint { get; set; } = "https://router.huggingface.co/hf-inference/models/{model}";
    public int ChatTimeoutSeconds { get; set; } = 60;
    public int ImageTimeoutSeconds { get; set; } = 120;
    public int VideoTimeoutSeconds { get; set; } = 300;
}

/// <summary>Application-wide settings (section: "App").</summary>
public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>Public base URL for generated media. LINE requires HTTPS URLs for images/videos, so this must be https.</summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>In-memory TTL (minutes) for generated media.</summary>
    public int MediaTtlMinutes { get; set; } = 10;
}

/// <summary>Background queue settings (section: "Queue").</summary>
public sealed class QueueOptions
{
    public const string Section = "Queue";

    /// <summary>BoundedChannel capacity. When full, items are dropped and the user is told the bot is busy.</summary>
    public int Capacity { get; set; } = 100;

    /// <summary>Number of parallel workers. Reduces head-of-line blocking while limiting concurrent load on HF.</summary>
    public int Workers { get; set; } = 2;
}

/// <summary>Chat settings (section: "Chat").</summary>
public sealed class ChatOptions
{
    public const string Section = "Chat";

    /// <summary>Maximum number of conversation turns kept per user.</summary>
    public int MaxHistory { get; set; } = 20;
}
