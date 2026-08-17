using LineHfBot.Configuration;
using Microsoft.Extensions.Options;

namespace LineHfBot.Text;

/// <summary>
/// User-facing message text shown to LINE users, resolved to the configured locale
/// (<see cref="AppOptions.Locale"/>; "en" default, "ja" available). Developer-facing logs
/// stay in English; only these end-user strings are localized.
/// </summary>
public sealed class UserMessages
{
    private readonly Strings _s;

    public UserMessages(IOptions<AppOptions> options) =>
        _s = string.Equals(options.Value.Locale, "ja", StringComparison.OrdinalIgnoreCase) ? Ja : En;

    public string Help => _s.Help;
    public string ResetDone => _s.ResetDone;
    public string NotYetImplemented => _s.NotYetImplemented;
    public string GeneratingImage => _s.GeneratingImage;
    public string ImageUsage => _s.ImageUsage;
    public string GeneratingVideo => _s.GeneratingVideo;
    public string VideoUsage => _s.VideoUsage;
    public string Busy => _s.Busy;
    public string EmptyAnswer => _s.EmptyAnswer;
    public string Timeout => _s.Timeout;
    public string Error => _s.Error;

    // Mode / session (3a)
    public string ModeChatSet => _s.ModeChatSet;
    public string ModeImageSet => _s.ModeImageSet;
    public string ModeVideoSet => _s.ModeVideoSet;
    public string RegenNoImage => _s.RegenNoImage;

    // Image edit (3b)
    public string EditPrompt => _s.EditPrompt;
    public string EditingImage => _s.EditingImage;
    public string EditNoImage => _s.EditNoImage;
    public string EditImageExpired => _s.EditImageExpired;

    // User-sent image received for editing (spec 04)
    public string ImageReceived => _s.ImageReceived;
    public string ImageReceiveFailed => _s.ImageReceiveFailed;
    public string ImageTooLarge => _s.ImageTooLarge;
    public string ImageSourceUnsupported => _s.ImageSourceUnsupported;

    // Vision / VQA on a user-sent photo (spec 07)
    public string ImageReceivedChoose => _s.ImageReceivedChoose;
    public string VisionPrompt => _s.VisionPrompt;
    public string VisionThinking => _s.VisionThinking;
    public string VisionImageExpired => _s.VisionImageExpired;

    // Quick reply button labels
    public string LabelRegenerate => _s.LabelRegenerate;
    public string LabelEdit => _s.LabelEdit;
    public string LabelAsk => _s.LabelAsk;
    public string LabelBackToChat => _s.LabelBackToChat;

    /// <summary>Chat assistant persona; steers the reply language to the configured locale.</summary>
    public string SystemPrompt => _s.SystemPrompt;

    private sealed record Strings(
        string Help,
        string ResetDone,
        string NotYetImplemented,
        string GeneratingImage,
        string ImageUsage,
        string GeneratingVideo,
        string VideoUsage,
        string Busy,
        string EmptyAnswer,
        string Timeout,
        string Error,
        string ModeChatSet,
        string ModeImageSet,
        string ModeVideoSet,
        string RegenNoImage,
        string EditPrompt,
        string EditingImage,
        string EditNoImage,
        string EditImageExpired,
        string ImageReceived,
        string ImageReceiveFailed,
        string ImageTooLarge,
        string ImageSourceUnsupported,
        string ImageReceivedChoose,
        string VisionPrompt,
        string VisionThinking,
        string VisionImageExpired,
        string LabelRegenerate,
        string LabelEdit,
        string LabelAsk,
        string LabelBackToChat,
        string SystemPrompt);

    private static readonly Strings En = new(
        Help:
            "How to use\n" +
            "・Send a message to chat with the AI\n" +
            "・Use the menu at the bottom to switch mode (Chat / Image / Video)\n" +
            "・In Image mode, send a description to generate an image\n" +
            "・After an image, tap 🔄 to regenerate or ✏️ to edit it\n" +
            "・Send a photo to edit it or ask about it\n" +
            "・/image <text> … make an image\n" +
            "・/video <text> … make a video\n" +
            "・/reset … clear the conversation\n" +
            "・/help … show this help",
        ResetDone: "Cleared the conversation.",
        NotYetImplemented: "This feature isn't available yet. Please hold on a little longer.",
        GeneratingImage: "Creating your image… please wait 🎨",
        ImageUsage: "Send a description of the image. e.g. /image a seaside sunset",
        GeneratingVideo: "Creating your video… this can take a while 🎬",
        VideoUsage: "Send a description of the video. e.g. /video a running cat",
        Busy: "We're busy right now. Please wait a moment and try again.",
        EmptyAnswer: "I couldn't come up with a reply. Please try again.",
        Timeout: "That took too long. Please try again.",
        Error: "Something went wrong. Please try again in a little while.",
        ModeChatSet: "Switched to Chat mode. Send a message to talk.",
        ModeImageSet: "Switched to Image mode. Send a description of what to create.",
        ModeVideoSet: "Switched to Video mode. Send a description of what to create.",
        RegenNoImage: "Make an image first, then you can regenerate it.",
        EditPrompt: "How should I edit it? Send an instruction. e.g. add a hat",
        EditingImage: "Editing your image… please wait ✏️",
        EditNoImage: "Make an image first, then you can edit it.",
        EditImageExpired: "That image is no longer available. Please make a new one and try again.",
        ImageReceived: "Got your image. How should I edit it? e.g. make the background a night sky",
        ImageReceiveFailed: "I couldn't get that image. Please try sending it again.",
        ImageTooLarge: "That image is too large. Please send a smaller one.",
        ImageSourceUnsupported: "I can't use that image. Please send a photo from your device.",
        ImageReceivedChoose: "Got your photo. What would you like to do?",
        VisionPrompt: "What would you like to ask about this image? e.g. What is written here?",
        VisionThinking: "Looking at your image… 🔍",
        VisionImageExpired: "That image is no longer available. Please send the photo again.",
        LabelRegenerate: "🔄 Regenerate",
        LabelEdit: "✏️ Edit",
        LabelAsk: "💬 Ask about this",
        LabelBackToChat: "💬 Chat",
        SystemPrompt: "You are a kind, helpful assistant. Answer clearly and concisely.");

    private static readonly Strings Ja = new(
        Help:
            "使い方\n" +
            "・そのままメッセージを送ると AI とチャットできます\n" +
            "・下のメニューでモード（チャット / 画像 / 動画）を切り替えられます\n" +
            "・画像モードでは、説明を送ると画像を作ります\n" +
            "・画像のあとは 🔄 で作り直し、✏️ で編集できます\n" +
            "・写真を送ると、その画像を編集するか、内容を質問できます\n" +
            "・/image 説明 … 画像を作ります\n" +
            "・/video 説明 … 動画を作ります\n" +
            "・/reset … 会話の履歴を消します\n" +
            "・/help … この案内を表示します",
        ResetDone: "会話の履歴を消しました。",
        NotYetImplemented: "この機能はいま準備中です。もう少しお待ちください。",
        GeneratingImage: "画像を作っています…少しお待ちください 🎨",
        ImageUsage: "画像の説明を入れて送ってください。例: /image 夕日の海辺",
        GeneratingVideo: "動画を作っています…少し時間がかかります 🎬",
        VideoUsage: "動画の説明を入れて送ってください。例: /video 走る猫",
        Busy: "いま混み合っています。少し待ってから、もう一度送ってください。",
        EmptyAnswer: "うまく返事を作れませんでした。もう一度試してみてください。",
        Timeout: "時間がかかりすぎました。もう一度試してみてください。",
        Error: "エラーが起きました。しばらくたってから、もう一度試してください。",
        ModeChatSet: "チャットモードにしました。メッセージを送ってください。",
        ModeImageSet: "画像モードにしました。作りたいものを説明して送ってください。",
        ModeVideoSet: "動画モードにしました。作りたいものを説明して送ってください。",
        RegenNoImage: "先に画像を作ってください。作ったあとに再生成できます。",
        EditPrompt: "どう編集しますか？指示を送ってください。例: 帽子を足して",
        EditingImage: "画像を編集しています…少しお待ちください ✏️",
        EditNoImage: "先に画像を作ってください。作ったあとに編集できます。",
        EditImageExpired: "その画像はもう使えません。もう一度作ってから試してください。",
        ImageReceived: "画像を受け取りました。どう編集しますか？例: 背景を夜空に",
        ImageReceiveFailed: "画像を取得できませんでした。もう一度送ってください。",
        ImageTooLarge: "画像が大きすぎます。小さいものを送ってください。",
        ImageSourceUnsupported: "この画像は使えません。端末内の写真を送ってください。",
        ImageReceivedChoose: "写真を受け取りました。どうしますか？",
        VisionPrompt: "この画像について何を聞きますか？例: ここに何が書いてある？",
        VisionThinking: "画像を確認しています… 🔍",
        VisionImageExpired: "その画像はもう使えません。もう一度写真を送ってください。",
        LabelRegenerate: "🔄 再生成",
        LabelEdit: "✏️ 編集",
        LabelAsk: "💬 この画像について質問",
        LabelBackToChat: "💬 チャットへ",
        SystemPrompt: "あなたは親切で丁寧なアシスタントです。分かりやすく簡潔に答えてください。");
}
