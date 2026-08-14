namespace LineHfBot.Queue;

/// <summary>
/// Processes one dequeued item. Resolved as scoped, so each item runs in its own DI scope.
/// Currently a stub; later increments replace it with chat / image / video handling and push delivery.
/// </summary>
public interface IWorkProcessor
{
    Task ProcessAsync(WorkItem item, CancellationToken cancellationToken);
}
