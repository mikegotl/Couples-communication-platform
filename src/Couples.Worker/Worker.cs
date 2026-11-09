namespace Couples.Worker;

/// <summary>
/// Background worker host. Scheduled jobs (retention enforcement, deletion requests,
/// invitation expiry) are added in later milestones. Logs never include user content.
/// </summary>
public sealed partial class Worker(ILogger<Worker> logger) : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        LogStarted(logger);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                LogHeartbeat(logger);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Couples worker started")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Couples worker heartbeat")]
    private static partial void LogHeartbeat(ILogger logger);
}
