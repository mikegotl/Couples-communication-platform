using Couples.Application.Privacy;

namespace Couples.Worker;

/// <summary>
/// Runs the deletion workflow for any pending requests (for example one interrupted in the web
/// app) and applies retention rules. Logs counts and ids only.
/// </summary>
public sealed partial class PrivacyMaintenanceWorker(IServiceScopeFactory scopes, ILogger<PrivacyMaintenanceWorker> logger) : BackgroundService
{
    private static readonly TimeSpan DeletionInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetentionInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(DeletionInterval);
        var nextRetention = DateTimeOffset.MinValue;

        try
        {
            do
            {
                await RunSafelyAsync("deletions", async services =>
                    await services.GetRequiredService<IPersonalDataService>().ProcessPendingDeletionsAsync(stoppingToken), stoppingToken);

                if (DateTimeOffset.UtcNow >= nextRetention)
                {
                    await RunSafelyAsync("retention", async services =>
                        await services.GetRequiredService<IRetentionService>().ApplyAsync(stoppingToken), stoppingToken);
                    nextRetention = DateTimeOffset.UtcNow + RetentionInterval;
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task RunSafelyAsync(string job, Func<IServiceProvider, Task> work, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await work(scope.ServiceProvider);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            // Exception messages from the data layer can include SQL but never row content.
            LogJobFailed(logger, job, ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Privacy job {Job} failed with {ExceptionType}; it will retry")]
    private static partial void LogJobFailed(ILogger logger, string job, string exceptionType);
}
