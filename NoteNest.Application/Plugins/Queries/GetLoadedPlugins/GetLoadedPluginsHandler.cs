using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Application.Plugins.Services;
using NoteNest.Domain.Common;

namespace NoteNest.Application.Plugins.Queries.GetLoadedPlugins
{
    public class GetLoadedPluginsHandler : IRequestHandler<GetLoadedPluginsQuery, Result<GetLoadedPluginsResult>>
    {
        private readonly IPluginManager _pluginManager;

        public GetLoadedPluginsHandler(IPluginManager pluginManager)
        {
            _pluginManager = pluginManager;
        }

        public async Task<Result<GetLoadedPluginsResult>> Handle(GetLoadedPluginsQuery request, CancellationToken cancellationToken)
        {
            await Task.CompletedTask; // Make async for consistency

            var plugins = _pluginManager.LoadedPlugins;

            var summaries = plugins.Select(p => new PluginSummary
            {
                PluginId = p.Id.Value,
                Name = p.Metadata.Name,
                Version = p.Metadata.Version.ToString(),
                Description = p.Metadata.Description,
                Status = Domain.Plugins.PluginStatus.Active, // All loaded plugins are active
                Category = p.Metadata.Category
            }).ToList();

            return Result.Ok(new GetLoadedPluginsResult
            {
                Plugins = summaries
            });
        }
    }
}

