using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using NoteNest.Core.Plugins;
using NoteNest.Core.Commands;

namespace NoteNest.UI.ViewModels
{
	public class ActivityBarViewModel
	{
		public ObservableCollection<IPlugin> Plugins { get; }
		private readonly IPluginManager _pluginManager;
		public event Action<IPlugin> PluginActivated;
		public ICommand ActivatePluginCommand { get; }

		public ActivityBarViewModel(IPluginManager pluginManager)
		{
			_pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
			Plugins = new ObservableCollection<IPlugin>(_pluginManager.LoadedPlugins);
			ActivatePluginCommand = new RelayCommand<IPlugin>(async p =>
			{
				if (p == null) return;
				await _pluginManager.ActivatePluginAsync(p.Id);
				PluginActivated?.Invoke(p);
			});
		}
	}
}


