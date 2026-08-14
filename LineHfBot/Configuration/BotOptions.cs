namespace LineHfBot.Configuration;

/// <summary>LINE Messaging API の資格情報（section: "Line"）。秘密情報のため設定/環境変数で与える。</summary>
public sealed class LineOptions
{
    public const string Section = "Line";
    public string ChannelSecret { get; set; } = "";
    public string ChannelAccessToken { get; set; } = "";
}

/// <summary>Hugging Face の設定（section: "HuggingFace"）。</summary>
public sealed class HuggingFaceOptions
{
    public const string Section = "HuggingFace";
    public string ApiKey { get; set; } = "";
    public string ChatModel { get; set; } = "";
    public string ImageModel { get; set; } = "";
    public string VideoModel { get; set; } = "";
    public int ChatTimeoutSeconds { get; set; } = 60;
    public int ImageTimeoutSeconds { get; set; } = 120;
    public int VideoTimeoutSeconds { get; set; } = 300;
}

/// <summary>アプリ全体の設定（section: "App"）。</summary>
public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>生成メディアの公開ベース URL。LINE は画像/動画に HTTPS URL を要求するため https 必須。</summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>生成メディアのメモリ内 TTL（分）。</summary>
    public int MediaTtlMinutes { get; set; } = 10;
}

/// <summary>バックグラウンドキューの設定（section: "Queue"）。</summary>
public sealed class QueueOptions
{
    public const string Section = "Queue";

    /// <summary>BoundedChannel の容量。満杯時は drop してユーザーへ混雑通知。</summary>
    public int Capacity { get; set; } = 100;

    /// <summary>並列 worker 数。head-of-line blocking を緩和しつつ HF 同時負荷を抑制。</summary>
    public int Workers { get; set; } = 2;
}

/// <summary>チャットの設定（section: "Chat"）。</summary>
public sealed class ChatOptions
{
    public const string Section = "Chat";

    /// <summary>ユーザー毎に保持する会話の最大往復数。</summary>
    public int MaxHistory { get; set; } = 20;
}
