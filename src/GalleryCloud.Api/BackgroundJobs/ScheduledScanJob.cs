using Cronos;
using GalleryCloud.Core.Interfaces;

namespace GalleryCloud.Api.BackgroundJobs;

public class ScheduledScanJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledScanJob> _logger;

    public ScheduledScanJob(IServiceScopeFactory scopeFactory, ILogger<ScheduledScanJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit for the app to fully start
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cronExpression = await GetCronExpressionAsync();
                var now = DateTime.UtcNow;

                CronExpression cron;
                try
                {
                    cron = CronExpression.Parse(cronExpression, CronFormat.Standard);
                }
                catch
                {
                    cron = CronExpression.Parse("0 3 * * *");
                }

                var next = cron.GetNextOccurrence(now, TimeZoneInfo.Utc);
                if (next == null)
                {
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                    continue;
                }

                var delay = next.Value - now;
                if (delay > TimeSpan.Zero)
                {
                    _logger.LogInformation("Next scheduled scan at {NextScan} (in {Delay})", next.Value, delay);
                    await Task.Delay(delay, stoppingToken);
                }

                _logger.LogInformation("Starting scheduled scan...");
                using var scope = _scopeFactory.CreateScope();
                var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
                await scanService.TriggerFullScanForAllUsersAsync(stoppingToken);
                _logger.LogInformation("Scheduled scan completed.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scheduled scan");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    private async Task<string> GetCronExpressionAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingService>();
            return await settings.GetAsync("scan.cronExpression", "0 3 * * *");
        }
        catch
        {
            return "0 3 * * *";
        }
    }
}
