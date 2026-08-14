namespace LineHfBot.Queue;

/// <summary>
/// バックグラウンド処理キューの抽象。既定実装は in-memory の BoundedChannel。
/// 将来 Redis / Storage Queue などの永続キューへ差し替え可能にするための境界。
/// </summary>
public interface IWorkQueue
{
    /// <summary>キューに投入する。満杯なら false（呼び出し側が混雑を通知する）。</summary>
    bool TryEnqueue(WorkItem item);

    /// <summary>キューから順次読み出す（worker が消費）。</summary>
    IAsyncEnumerable<WorkItem> ReadAllAsync(CancellationToken cancellationToken);
}
