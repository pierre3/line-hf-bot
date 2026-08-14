namespace LineHfBot.Queue;

/// <summary>
/// Abstraction over the background work queue. The default implementation is an in-memory BoundedChannel.
/// This boundary lets us swap in a durable queue (Redis, Storage Queue, ...) later.
/// </summary>
public interface IWorkQueue
{
    /// <summary>Enqueue an item. Returns false when the queue is full (the caller then notifies the user).</summary>
    bool TryEnqueue(WorkItem item);

    /// <summary>Read items in order (consumed by workers).</summary>
    IAsyncEnumerable<WorkItem> ReadAllAsync(CancellationToken cancellationToken);
}
