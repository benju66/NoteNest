using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using NoteNest.Core.Plugins;
using NoteNest.Core.Commands;
using NoteNest.Core.Services;
using System.Collections.Generic;
using System.Linq;
using NoteNest.Core.Events;
using System.Linq;

namespace NoteNest.UI.ViewModels
{
	public class ActivityBarViewModel
	{
		public ObservableCollection<IPlugin> Plugins { get; }
		private readonly IPluginManager _pluginManager;
		public event Action<IPlugin, bool> PluginActivated;
		public ICommand ActivatePluginCommand { get; }
        public ICommand ActivatePluginSecondaryCommand { get; }
        public ICommand PinPluginPrimaryCommand { get; }
        public ICommand PinPluginSecondaryCommand { get; }

		public ActivityBarViewModel(IPluginManager pluginManager)
		{
			_pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
			Plugins = new ObservableCollection<IPlugin>(_pluginManager.LoadedPlugins);
			_pluginManager.PluginsChanged += OnPluginsChanged;

			// Refresh when settings change (visibility/order updates)
			try
			{
				var bus = (App.Current as App)?.ServiceProvider?.GetService(typeof(IEventBus)) as IEventBus;
				bus?.Subscribe<AppSettingsChangedEvent>(_ => OnPluginsChanged());
			}
			catch { }
			ActivatePluginCommand = new RelayCommand<IPlugin>(async p =>
			{
				if (p == null) return;
				await _pluginManager.ActivatePluginAsync(p.Id);
				var isSecondary = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
				PluginActivated?.Invoke(p, isSecondary);
			});

            ActivatePluginSecondaryCommand = new RelayCommand<IPlugin>(async p =>
            {
                if (p == null) return;
                await _pluginManager.ActivatePluginAsync(p.Id);
                PluginActivated?.Invoke(p, true);
            });

            PinPluginPrimaryCommand = new RelayCommand<IPlugin>(p => PinPluginToSlot(p, false));
            PinPluginSecondaryCommand = new RelayCommand<IPlugin>(p => PinPluginToSlot(p, true));
			// Apply initial filter/order if configured
			OnPluginsChanged();
		}

		private void OnPluginsChanged()
		{
			App.Current.Dispatcher.Invoke(() =>
			{
				Plugins.Clear();
				var config = (App.Current as App)?.ServiceProvider?.GetService(typeof(ConfigurationService)) as ConfigurationService;
				var settings = config?.Settings;
				IEnumerable<IPlugin> ordered = _pluginManager.LoadedPlugins;
				if (settings != null && settings.PluginOrder != null && settings.PluginOrder.Count > 0)
				{
					ordered = ordered.OrderBy(p =>
					{
						int idx = settings.PluginOrder.IndexOf(p.Id);
						return idx >= 0 ? idx : int.MaxValue;
					});
				}
				foreach (var p in ordered)
				{
					if (settings != null && settings.VisiblePluginIds != null && settings.VisiblePluginIds.Count > 0)
					{
						if (!settings.VisiblePluginIds.Contains(p.Id)) continue;
					}
					Plugins.Add(p);
				}
			});
		}

        private void PinPluginToSlot(IPlugin plugin, bool secondary)
        {
            try
            {
                if (plugin == null) return;
                var config = (System.Windows.Application.Current as NoteNest.UI.App)?.ServiceProvider?.GetService(typeof(ConfigurationService)) as ConfigurationService;
                if (config?.Settings == null) return;
                config.Settings.PluginPanelSlotByPluginId[plugin.Id] = secondary ? "Secondary" : "Primary";
                config.RequestSaveDebounced();
            }
            catch { }
        }
	}
}


