using Microsoft.Extensions.Options;

namespace SteamItems.Web.Status;

/// <summary>
/// Polls the Worker every <see cref="WorkerStatusOptions.PollInterval"/> for exports that are not finished
/// (<see cref="ExportStatusUpdater"/>). While the Worker is down, those exports stay as they are and the next round tries again.
/// </summary>
public sealed class ExportStatusPoller(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerStatusOptions> options,
    ILogger<ExportStatusPoller> logger) : BackgroundService
{
    // Logs the Worker going away and coming back once, not every round.
    private bool workerReachable = true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        logger.LogInformation(
            "Polling export status from {BaseUrl} every {Interval}", settings.BaseUrl, settings.PollInterval);

        using var timer = new PeriodicTimer(settings.PollInterval);
        try
        {
            do
            {
                await PollAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
    }

    private async Task PollAsync(CancellationToken stoppingToken)
    {
        try
        {
            // One scope (and DbContext) per round.
            await using var scope = scopeFactory.CreateAsyncScope();
            var updater = scope.ServiceProvider.GetRequiredService<ExportStatusUpdater>();
            var changed = await updater.UpdateAsync(stoppingToken);

            if (!workerReachable)
            {
                logger.LogInformation("The Worker answers again; export status updates resumed");
                workerReachable = true;
            }

            if (changed > 0)
            {
                logger.LogInformation("Updated the status of {Count} export(s)", changed);
            }
        }
        catch (WorkerUnavailableException ex)
        {
            if (workerReachable)
            {
                logger.LogWarning(ex, "The Worker is unavailable; exports keep their status until it answers");
                workerReachable = false;
            }
            else
            {
                logger.LogDebug(ex, "The Worker is still unavailable");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // web.db busy or similar. The web app must keep serving pages, so the poller logs and tries next round.
            logger.LogError(ex, "Polling export status failed; trying again next round");
        }
    }
}
