using System.Threading.Tasks;
using NoteNest.Domain.Common;
using NoteNest.Domain.Plugins;

namespace NoteNest.Application.Plugins.Interfaces
{
    /// <summary>
    /// Plugin-specific data persistence interface.
    /// Provides isolated storage for plugin data.
    /// </summary>
    public interface IPluginDataStore
    {
        Task<Result<T>> LoadDataAsync<T>(PluginId pluginId, string key) where T : class;
        Task<Result> SaveDataAsync<T>(PluginId pluginId, string key, T data) where T : class;
        Task<Result> DeleteDataAsync(PluginId pluginId, string key);
        Task<Result<long>> GetStorageSizeAsync(PluginId pluginId);
        Task<Result> BackupPluginDataAsync(PluginId pluginId);
    }
}

