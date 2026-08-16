namespace LineHfBot.Configuration;

/// <summary>
/// LINE Messaging API settings (section: "Line"): credentials (secret; supply via
/// configuration/environment) plus constraints for fetching user-sent image content.
/// </summary>
public sealed class LineOptions
{
    public const string Section = "Line";
    public string ChannelSecret { get; set; } = "";
    public string ChannelAccessToken { get; set; } = "";

    /// <summary>Maximum bytes to download for a user-sent image (via the Content API). Larger images are rejected.</summary>
    public long MaxIncomingImageBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Timeout (seconds) for fetching user-sent image content from the LINE Content API.</summary>
    public int ContentFetchTimeoutSeconds { get; set; } = 30;
}

/// <summary>Hugging Face settings (section: "HuggingFace").</summary>
public sealed class HuggingFaceOptions
{
    public const string Section = "HuggingFace";
    public string ApiKey { get; set; } = "";
    public string ChatModel { get; set; } = "";
    public string ImageModel { get; set; } = "";

    /// <summary>
    /// Text-to-video model, as a provider model id. Text-to-video is not served by hf-inference, so this
    /// defaults to the fal-ai provider id for Wan2.2-5B. Change together with <see cref="VideoEndpoint"/>
    /// to target a different provider.
    /// </summary>
    public string VideoModel { get; set; } = "fal-ai/wan/v2.2-5b/text-to-video";

    /// <summary>
    /// Image-to-image (edit) model, as a provider model id. Image-to-image is not served by hf-inference,
    /// so this defaults to the fal-ai provider id for Qwen-Image-Edit. Change together with
    /// <see cref="ImageEditEndpoint"/> to target a different provider.
    /// </summary>
    public string ImageEditModel { get; set; } = "fal-ai/qwen-image-edit";

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
    /// Text-to-video submit endpoint template. "{model}" is replaced with <see cref="VideoModel"/>.
    /// Defaults to the fal-ai async queue on the HF router: the service submits the job here, polls the
    /// returned status URL, then reads the result video URL. hf-inference does not serve text-to-video,
    /// so a GPU provider is required; changing provider means changing this template, the model id, and
    /// possibly the request/response handling.
    /// </summary>
    public string VideoEndpoint { get; set; } = "https://router.huggingface.co/fal-ai/{model}?_subdomain=queue";

    /// <summary>
    /// Image-to-image (edit) submit endpoint template. "{model}" is replaced with <see cref="ImageEditModel"/>.
    /// Defaults to the fal-ai async queue on the HF router: the service submits the job here, polls the
    /// returned status URL, then reads the result image URL. Provider-dependent; changing provider means
    /// changing this template, the model id, and possibly the request/response handling.
    /// </summary>
    public string ImageEditEndpoint { get; set; } = "https://router.huggingface.co/fal-ai/{model}?_subdomain=queue";

    /// <summary>
    /// Hosts allowed when re-fetching media from a provider-supplied URL (JSON-URL responses).
    /// Shared by the image and video paths. Separated by ";" / "," / whitespace; label-boundary match
    /// (e.g. "fal.media" allows "cdn.fal.media" but not "evilfal.media"). Empty = deny all (fail-closed).
    /// </summary>
    public string MediaRefetchAllowedHosts { get; set; } = "fal.media;replicate.delivery";
    public int ChatTimeoutSeconds { get; set; } = 60;
    public int ImageTimeoutSeconds { get; set; } = 120;
    public int ImageEditTimeoutSeconds { get; set; } = 120;
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

    /// <summary>
    /// Enable the /video command. Off by default: text-to-video runs via the paid, slow fal-ai provider
    /// (see <see cref="HuggingFaceOptions.VideoModel"/>/<see cref="HuggingFaceOptions.VideoEndpoint"/>),
    /// so it ships as opt-in to avoid unexpected charges. Set to true once your HF token has Inference Providers credits.
    /// </summary>
    public bool VideoEnabled { get; set; }

    /// <summary>
    /// UI language for user-facing text and the rich menu images ("en" or "ja").
    /// English is the default for the published image; set to "ja" for Japanese.
    /// </summary>
    public string Locale { get; set; } = "en";

    /// <summary>
    /// Provision the mode-switcher rich menu on startup (idempotent). Disable to run without a rich menu.
    /// </summary>
    public bool RichMenuEnabled { get; set; } = true;
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
