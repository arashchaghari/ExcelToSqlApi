using ExcelToSqlApi.Contracts;

namespace ExcelToSqlApi.Services;

public class ExcelProcessingService(IBackgroundTaskQueue taskQueue,
                                    ILogger<ExcelProcessingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var workItem = await taskQueue.DequeueAsync(stoppingToken);

            try
            {
                await workItem!(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred executing background work item.");
            }
        }
    }
}