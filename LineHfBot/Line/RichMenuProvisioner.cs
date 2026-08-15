namespace LineHfBot.Line;

/// <summary>
/// Runs rich menu provisioning once at startup. Failures are logged but never crash the app
/// (the bot still works without a rich menu; users can use slash commands).
/// </summary>
public sealed class RichMenuProvisioner(RichMenuManager manager, ILogger<RichMenuProvisioner> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await manager.ProvisionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Rich menu provisioning failed; continuing without it.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
