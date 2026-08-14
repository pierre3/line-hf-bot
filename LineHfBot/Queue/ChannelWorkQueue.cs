using System.Threading.Channels;
using LineHfBot.Configuration;
using Microsoft.Extensions.Options;

namespace LineHfBot.Queue;

/// <summary>
/// <see cref="System.Threading.Channels.Channel{T}"/> ベースの in-memory キュー。
/// BoundedChannel + FullMode.Wait とし、<see cref="TryEnqueue"/> は満杯時に false を返す
/// （バックプレッシャで待たず、呼び出し側が「混雑」通知して drop する仕様）。
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
