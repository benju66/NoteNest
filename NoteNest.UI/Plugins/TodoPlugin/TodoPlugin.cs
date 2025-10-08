using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.UI.Views;

namespace NoteNest.UI.Plugins.TodoPlugin
{
    /// <summary>
    /// Simple factory for creating the Todo plugin panel.
    /// </summary>
    public class TodoPlugin
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IAppLogger _logger;

        public string Id => "NoteNest.TodoPlugin";
        public string Name => "Todo Manager";
        public string Version => "1.0.0";
        public string Description => "Advanced task management with bidirectional note integration";

        public TodoPlugin(IServiceProvider serviceProvider, IAppLogger logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public FrameworkElement CreatePanel()
        {
            try
            {
                _logger.Info("📦 TodoPlugin.CreatePanel() called");
                
                var panelView = _serviceProvider.GetRequiredService<TodoPanelView>();
                _logger.Info($"📦 TodoPanelView created: {panelView != null}");
                
                return panelView;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ Failed to create Todo panel");
                
                // Return a simple error panel instead of crashing
                var errorPanel = new System.Windows.Controls.TextBlock
                {
                    Text = $"Failed to load Todo plugin:\n{ex.Message}",
                    Margin = new Thickness(12),
                    TextWrapping = TextWrapping.Wrap
                };
                return errorPanel;
            }
        }
    }
}