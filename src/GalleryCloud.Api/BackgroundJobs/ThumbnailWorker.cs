using GalleryCloud.Core.Interfaces;

namespace GalleryCloud.Api.BackgroundJobs;

public class ThumbnailWorker : BackgroundService
{
    private readonly IThumbnailService _service;

    public ThumbnailWorker(IThumbnailService service) => _service = service;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => _service.ConsumeChannelAsync(stoppingToken);
}
