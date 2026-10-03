namespace SteamItems.Worker;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker started");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Worker heartbeat at {Time}", DateTimeOffset.Now);
                await Task.Delay(HeartbeatInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown (Ctrl+C / SIGTERM).
        }

        logger.LogInformation("Worker stopped");
    }
}
