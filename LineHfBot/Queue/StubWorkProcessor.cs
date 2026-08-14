namespace LineHfBot.Queue;

/// <summary>
/// Temporary stub processor. Logs only, so we can wire up the queue flow first.
/// TODO(chat/image/video increments): replace with real handling and push delivery.
/// </summary>
public sealed class StubWorkProcessor(ILogger<StubWorkProcessor> logger) : IWorkProcessor
{
    public Task ProcessAsync(WorkItem item, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "process(stub): kind={Kind} user={User} text=\"{Text}\" eventId={EventId}",
            item.Kind, item.UserId, item.Text, item.WebhookEventId);
        return Task.CompletedTask;
    }
}
