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
    /// Image-to-video model, as a provider model id. Image-to-video is not served by hf-inference, so this
    /// defaults to the fal-ai provider id for Wan2.2-I2V-A14B. Change together with
    /// <see cref="ImageToVideoEndpoint"/> to target a different provider (a lighter alternative is
    /// "fal-ai/wan-i2v"). Runs on the same credit-heavy fal provider as text-to-video and is gated by the
    /// same <see cref="AppOptions.VideoEnabled"/> flag.
    /// </summary>
    public string ImageToVideoModel { get; set; } = "fal-ai/wan/v2.2-a14b/image-to-video";

    /// <summary>
    /// Chat completion base URL. Defaults to the Hugging Face router; the SK connector
    /// appends "/v1/chat/completions", so this must NOT include the "/v1" suffix.
    /// Adjust if your model/provider needs a different base URL.
    /// </summary>
    public string ChatEndpoint { get; set; } = "https://router.huggingface.co";

    /// <summary>
    /// Vision (image question answering) model, as a provider-pinned model id ("model:provider"). A
    /// vision-capable chat model served on the OpenAI-compatible chat completions endpoint. Pin the provider
    /// explicitly ("...:ovhcloud") because auto-routing may not select a provider that serves the model and
    /// returns model_not_supported. Availability/capacity is provider-dependent; enable the pinned provider
    /// in your HF settings, or change to a model:provider your token can serve.
    /// </summary>
    public string VisionModel { get; set; } = "Qwen/Qwen2.5-VL-72B-Instruct:ovhcloud";

    /// <summary>
    /// Full URL of the OpenAI-compatible chat completions endpoint used for vision. Unlike
    /// <see cref="ChatEndpoint"/> (a base the SK connector extends), this is called directly, so it must
    /// include the "/v1/chat/completions" path.
    /// </summary>
    public string VisionEndpoint { get; set; } = "https://router.huggingface.co/v1/chat/completions";

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
    /// Image-to-video submit endpoint template. "{model}" is replaced with <see cref="ImageToVideoModel"/>.
    /// Defaults to the fal-ai async queue on the HF router (same template as text-to-video/image-to-image):
    /// the service submits the job here, polls the returned status URL, then reads the result video URL.
    /// hf-inference does not serve image-to-video, so a GPU provider is required; changing provider means
    /// changing this template, the model id, and possibly the request/response handling.
    /// </summary>
    public string ImageToVideoEndpoint { get; set; } = "https://router.huggingface.co/fal-ai/{model}?_subdomain=queue";

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
    public int VisionTimeoutSeconds { get; set; } = 120;
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
    /// Enable video generation: the /video command (text-to-video) and the "🎬 Make a video" button on
    /// images (image-to-video). Off by default: both run via the credit-heavy, slow fal-ai provider
    /// (see <see cref="HuggingFaceOptions.VideoModel"/>/<see cref="HuggingFaceOptions.ImageToVideoModel"/>),
    /// so they ship as opt-in to avoid draining HF Inference credits unexpectedly. When off, no animate
    /// button is shown and both paths reply "not available". Set to true once your HF token has credits to spare.
    /// </summary>
    public bool VideoEnabled { get; set; }

    /// <summary>
    /// Enable image question answering (vision/VQA) on user-sent photos. On by default: vision runs via the
    /// same HF Inference chat endpoint as chat (not the credit-heavy fal provider). When on, receiving a photo
    /// offers "Edit"/"Ask about this image" instead of going straight to editing; when off, the photo goes
    /// straight to the edit flow (spec04 behavior) and no vision UI is shown. A [Ask] tap fails with the generic
    /// error if <see cref="HuggingFaceOptions.VisionModel"/> is not servable by your token's provider.
    /// </summary>
    public bool VisionEnabled { get; set; } = true;

    /// <summary>
    /// Maximum number of Q&amp;A turns kept in a conversational vision session (spec09). Each follow-up resends
    /// the image plus prior turns to the stateless endpoint, so credit cost grows with turn count; this caps it.
    /// Values below 1 are treated as 1. Separate axis from <see cref="ChatOptions.MaxHistory"/> (vision turns
    /// carry an image and cost more per turn).
    /// </summary>
    public int VisionMaxTurns { get; set; } = 8;

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
