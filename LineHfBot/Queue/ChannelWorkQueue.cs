using System.Threading.Channels;
using LineHfBot.Configuration;
using Microsoft.Extensions.Options;

namespace LineHfBot.Queue;

/// <summary>
/// In-memory queue backed by <see cref="System.Threading.Channels.Channel{T}"/>.
/// Uses a bounded channel with FullMode.Wait; <see cref="TryEnqueue"/> returns false when full
/// so the caller can drop the item and tell the user the bot is busy (no blocking backpressure).
/// </summary>
public sealed class ChannelWorkQueue : IWorkQueue
{
    private readonly Channel<WorkItem> _channel;

    public ChannelWorkQueue(IOptions<QueueOptions> options)
    {
        var capacity = Math.Max(1, options.Value.Capacity);
        _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    public bool TryEnqueue(WorkItem item) => _channel.Writer.TryWrite(item);

    public IAsyncEnumerable<WorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
