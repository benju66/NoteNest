using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Domain.Common;
using NoteNest.Domain.Plugins;

namespace NoteNest.Application.Plugins.Interfaces
{
    /// <summary>
    /// Repository interface for plugin persistence and retrieval.
    /// </summary>
    public interface IPluginRepository
    {
        Task<Result<Plugin>> GetByIdAsync(PluginId pluginId);
        Task<Result<IReadOnlyList<Plugin>>> GetAllAsync();
        Task<Result<IReadOnlyList<Plugin>>> GetByStatusAsync(PluginStatus status);
        Task<Result> AddAsync(Plugin plugin);
        Task<Result> UpdateAsync(Plugin plugin);
        Task<Result> DeleteAsync(PluginId pluginId);
    }
}

