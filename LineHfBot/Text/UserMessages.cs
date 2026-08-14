namespace LineHfBot.Text;

/// <summary>
/// User-facing message text shown to LINE users. Kept in Japanese (product UI language)
/// and centralized here so wording is easy to find and adjust.
/// </summary>
public static class UserMessages
{
    public const string Help =
        "使い方\n" +
        "・そのままメッセージを送ると AI とチャットできます\n" +
        "・/image 説明 … 画像を作ります\n" +
        "・/video 説明 … 動画を作ります\n" +
        "・/reset … 会話の履歴を消します\n" +
        "・/help … この案内を表示します";

    public const string ResetDone = "会話の履歴を消しました。";

    public const string NotYetImplemented = "この機能はいま準備中です。もう少しお待ちください。";

    public const string Busy = "いま混み合っています。少し待ってから、もう一度送ってください。";

    public const string EmptyAnswer = "うまく返事を作れませんでした。もう一度試してみてください。";

    public const string Error = "エラーが起きました。しばらくたってから、もう一度試してください。";
}
