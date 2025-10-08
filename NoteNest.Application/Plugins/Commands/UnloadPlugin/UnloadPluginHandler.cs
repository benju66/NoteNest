using System.Threading;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Application.Plugins.Services;
using NoteNest.Domain.Common;
using NoteNest.Domain.Plugins;

namespace NoteNest.Application.Plugins.Commands.UnloadPlugin
{
    public class UnloadPluginHandler : IRequestHandler<UnloadPluginCommand, Result<UnloadPluginResult>>
    {
        private readonly IPluginManager _pluginManager;

        public UnloadPluginHandler(IPluginManager pluginManager)
        {
            _pluginManager = pluginManager;
        }

        public async Task<Result<UnloadPluginResult>> Handle(UnloadPluginCommand request, CancellationToken cancellationToken)
        {
            var pluginId = PluginId.From(request.PluginId);
            
            var plugin = _pluginManager.GetPlugin(pluginId);
            if (plugin == null)
            {
                return Result.Fail<UnloadPluginResult>($"Plugin not loaded: {request.PluginId}");
            }

            var pluginName = plugin.Metadata.Name;
            
            var unloadResult = await _pluginManager.UnloadPluginAsync(pluginId);
            if (unloadResult.IsFailure)
            {
                return Result.Fail<UnloadPluginResult>(unloadResult.Error);
            }

            return Result.Ok(new UnloadPluginResult
            {
                PluginId = request.PluginId,
                PluginName = pluginName,
                UnloadedAt = System.DateTime.UtcNow
            });
        }
    }
}

