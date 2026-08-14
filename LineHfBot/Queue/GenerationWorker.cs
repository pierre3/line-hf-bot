using LineHfBot.Configuration;
using Microsoft.Extensions.Options;

namespace LineHfBot.Queue;

/// <summary>
/// キューを消費するバックグラウンドサービス。設定数の worker を並列に走らせ、
/// head-of-line blocking を緩和しつつ HF への同時負荷を抑える。
/// 各 work item ごとに DI スコープを生成し、例外は1件単位で隔離して worker を止めない。
/// </summary>
public sealed class GenerationWorker(
    IWorkQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<QueueOptions> options,
    ILogger<GenerationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = Math.Max(1, options.Value.Workers);
        logger.LogInformation("GenerationWorker 開始: worker数={Workers}", workers);

        var tasks = Enumerable.Range(0, workers)
            .Select(id => RunWorkerAsync(id, stoppingToken))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    private async Task RunWorkerAsync(int workerId, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    // work item ごとにスコープを生成（scoped サービスを安全に利用するため）。
                    using var scope = scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<IWorkProcessor>();
                    await processor.ProcessAsync(item, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 例外隔離: 1件の失敗で worker ループを止めない。
                    logger.LogError(ex,
                        "処理に失敗しました worker={WorkerId} kind={Kind} user={User}",
                        workerId, item.Kind, item.UserId);
                    // TODO(messaging 増分): ユーザーへ失敗を Push 通知する。
                }
            }
        }
        catch (OperationCanceledException)
        {
            // シャットダウン時の正常終了。
        }

        logger.LogInformation("worker 終了: worker={WorkerId}", workerId);
    }
}
