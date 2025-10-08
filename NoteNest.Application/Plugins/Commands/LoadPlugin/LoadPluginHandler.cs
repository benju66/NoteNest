using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Application.Plugins.Services;
using NoteNest.Domain.Common;
using NoteNest.Domain.Plugins;

namespace NoteNest.Application.Plugins.Commands.LoadPlugin
{
    public class LoadPluginHandler : IRequestHandler<LoadPluginCommand, Result<LoadPluginResult>>
    {
        private readonly IPluginManager _pluginManager;

        public LoadPluginHandler(IPluginManager pluginManager)
        {
            _pluginManager = pluginManager;
        }

        public async Task<Result<LoadPluginResult>> Handle(LoadPluginCommand request, CancellationToken cancellationToken)
        {
            var pluginId = PluginId.From(request.PluginId);
            
            var loadResult = await _pluginManager.LoadPluginAsync(pluginId);
            if (loadResult.IsFailure)
            {
                return Result.Fail<LoadPluginResult>(loadResult.Error);
            }

            var plugin = loadResult.Value;
            
            return Result.Ok(new LoadPluginResult
            {
                PluginId = plugin.Id.Value,
                PluginName = plugin.Metadata.Name,
                GrantedCapabilities = request.GrantedCapabilities,
                LoadedAt = System.DateTime.UtcNow
            });
        }
    }
}

