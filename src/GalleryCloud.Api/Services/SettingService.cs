using System.Collections.Concurrent;
using GalleryCloud.Api.Data;
using GalleryCloud.Core.Entities;
using GalleryCloud.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GalleryCloud.Api.Services;

public class SettingService : ISettingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly ConcurrentDictionary<string, string?> _cache = new();

    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["scan.cronExpression"] = "0 3 * * *",
        ["scan.supportedFormats"] = ".jpg,.jpeg,.heic,.avif,.png,.webp",
        ["scan.excludePatterns"] = "**/thumbnails/**,**/@eaDir/**",
        ["filewatcher.enabled"] = "true",
        ["filewatcher.debounceDelayMs"] = "5000",
        ["thumbnail.format"] = "webp",
        ["thumbnail.quality"] = "80",
        ["thumbnail.parallelThreads"] = "2",
        ["thumbnail.cacheDir"] = "data/thumbnails",
        ["thumbnail.maxMemoryCacheMb"] = "512",
        ["preview.format"] = "webp",
        ["preview.quality"] = "85",
        ["preview.maxResolution"] = "2560",
        ["map.tileUrlNormal"] = "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
        ["map.tileUrlSatellite"] = "https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}",
        ["map.defaultBasemap"] = "normal",
        ["auth.tokenExpiryDays"] = "30",
    };

    public SettingService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<string?> GetAsync(string key)
    {
        // Try cache first
        if (_cache.TryGetValue(key, out var cached))
            return cached ?? (Defaults.TryGetValue(key, out var d) ? d : null);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var setting = await db.SystemSettings.FindAsync(key);
        var value = setting?.Value ?? (Defaults.TryGetValue(key, out var def) ? def : null);
        _cache[key] = value;
        return value;
    }

    public async Task<T> GetAsync<T>(string key, T defaultValue) where T : notnull
    {
        var value = await GetAsync(key);
        if (value == null) return defaultValue;
        try { return (T)Convert.ChangeType(value, typeof(T)); }
        catch { return defaultValue; }
    }

    public async Task SetAsync(string key, string value)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var setting = await db.SystemSettings.FindAsync(key);
        if (setting == null)
        {
            setting = new SystemSetting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow };
            db.SystemSettings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        _cache[key] = value; // Update cache
    }

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var dbSettings = await db.SystemSettings.ToDictionaryAsync(s => s.Key, s => s.Value);

        foreach (var (key, defaultValue) in Defaults)
        {
            if (!dbSettings.ContainsKey(key))
                dbSettings[key] = defaultValue;
        }

        return dbSettings;
    }

    public async Task LoadDefaultsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var (key, value) in Defaults)
        {
            if (!await db.SystemSettings.AnyAsync(s => s.Key == key))
            {
                db.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
            }
        }

        await db.SaveChangesAsync();
    }
}
