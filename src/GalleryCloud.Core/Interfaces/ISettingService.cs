namespace GalleryCloud.Core.Interfaces;

public interface ISettingService
{
    Task<string?> GetAsync(string key);
    Task<T> GetAsync<T>(string key, T defaultValue) where T : notnull;
    Task SetAsync(string key, string value);
    Task<Dictionary<string, string>> GetAllAsync();
    Task LoadDefaultsAsync();
}
