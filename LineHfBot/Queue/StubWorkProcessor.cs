namespace LineHfBot.Queue;

/// <summary>
/// 暫定のスタブ処理。キューイングの流れを通すためにログ出力のみ行う。
/// TODO(chat/image/video 増分): 実処理と Push 送信に差し替える。
/// </summary>
public sealed class StubWorkProcessor(ILogger<StubWorkProcessor> logger) : IWorkProcessor
{
    public Task ProcessAsync(WorkItem item, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "処理(stub): kind={Kind} user={User} text=\"{Text}\" eventId={EventId}",
            item.Kind, item.UserId, item.Text, item.WebhookEventId);
        return Task.CompletedTask;
    }
}
