using System.Collections.Generic;
using MediatR;
using NoteNest.Domain.Common;
using NoteNest.Domain.Plugins;

namespace NoteNest.Application.Plugins.Queries.GetLoadedPlugins
{
    /// <summary>
    /// Query to retrieve all currently loaded plugins.
    /// </summary>
    public class GetLoadedPluginsQuery : IRequest<Result<GetLoadedPluginsResult>>
    {
        public PluginCategory? FilterByCategory { get; set; }
        public PluginStatus? FilterByStatus { get; set; }
    }

    public class GetLoadedPluginsResult
    {
        public IReadOnlyList<PluginSummary> Plugins { get; set; }
    }

    public class PluginSummary
    {
        public string PluginId { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public PluginStatus Status { get; set; }
        public PluginCategory Category { get; set; }
    }
}

