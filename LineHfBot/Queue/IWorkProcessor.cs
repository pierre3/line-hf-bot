namespace LineHfBot.Queue;

/// <summary>
/// キューから取り出した1件を実処理する。scoped で解決され、work item ごとに新しいスコープで動く。
/// 現段階はスタブ実装。以降の増分で chat / image / video の処理と Push 送信に差し替える。
/// </summary>
public interface IWorkProcessor
{
    Task ProcessAsync(WorkItem item, CancellationToken cancellationToken);
}
