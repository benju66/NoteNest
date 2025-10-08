using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.Application.Plugins.Commands.UnloadPlugin
{
    /// <summary>
    /// Command to unload a plugin from the application.
    /// Saves state and releases resources.
    /// </summary>
    public class UnloadPluginCommand : IRequest<Result<UnloadPluginResult>>
    {
        public string PluginId { get; set; }
        public bool Force { get; set; } = false;
    }

    public class UnloadPluginResult
    {
        public string PluginId { get; set; }
        public string PluginName { get; set; }
        public System.DateTime UnloadedAt { get; set; }
    }
}

