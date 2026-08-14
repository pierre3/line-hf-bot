using LineHfBot.Configuration;
using Microsoft.Extensions.Options;

namespace LineHfBot.Queue;

/// <summary>
/// Background service that consumes the queue. It runs the configured number of workers in parallel
/// to reduce head-of-line blocking while limiting concurrent load on HF.
/// Each item runs in its own DI scope, and exceptions are isolated per item so a worker never stops.
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
        logger.LogInformation("GenerationWorker started: workers={Workers}", workers);

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
                    // Create a scope per item so scoped services can be used safely.
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
                    // Isolate failures: one failing item must not stop the worker loop.
                    logger.LogError(ex,
                        "Processing failed worker={WorkerId} kind={Kind} user={User}",
                        workerId, item.Kind, item.UserId);
                    // TODO(messaging increment): push a failure notice to the user.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal exit on shutdown.
        }

        logger.LogInformation("Worker stopped: worker={WorkerId}", workerId);
    }
}
