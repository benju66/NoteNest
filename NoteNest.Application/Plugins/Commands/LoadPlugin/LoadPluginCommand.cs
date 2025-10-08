using System.Collections.Generic;
using MediatR;
using NoteNest.Domain.Common;

namespace NoteNest.Application.Plugins.Commands.LoadPlugin
{
    /// <summary>
    /// Command to load a plugin into the application.
    /// Validates capabilities and initializes the plugin.
    /// </summary>
    public class LoadPluginCommand : IRequest<Result<LoadPluginResult>>
    {
        public string PluginId { get; set; }
        public IReadOnlyList<string> GrantedCapabilities { get; set; }
        public bool AutoStart { get; set; } = true;
    }

    public class LoadPluginResult
    {
        public string PluginId { get; set; }
        public string PluginName { get; set; }
        public IReadOnlyList<string> GrantedCapabilities { get; set; }
        public System.DateTime LoadedAt { get; set; }
    }
}

