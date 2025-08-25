using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NoteNest.Core.Plugins
{
	/// <summary>
	/// Base interface for all NoteNest plugins
	/// </summary>
	public interface IPlugin : IDisposable
	{
		string Id { get; }
		string Name { get; }
		string Icon { get; }
		Version Version { get; }
		string Description { get; }
		bool IsEnabled { get; set; }
		Task<bool> InitializeAsync();
		Task ShutdownAsync();
		IPluginPanel GetPanel();
		IPluginSettings GetSettings();
		ICommand ActivateCommand { get; }
	}
}


