using GalleryCloud.Core.Interfaces;

namespace GalleryCloud.Api.BackgroundJobs;

public class ThumbnailWorker : BackgroundService
{
    private readonly IThumbnailService _service;
    private readonly ILogger<ThumbnailWorker> _logger;

    public ThumbnailWorker(IThumbnailService service, ILogger<ThumbnailWorker> logger)
    {
        _service = service;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _service.ConsumeChannelAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ThumbnailWorker crashed, restarting in 5s");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}
