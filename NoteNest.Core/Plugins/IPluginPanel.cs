namespace NoteNest.Core.Plugins
{
	/// <summary>
	/// Interface for plugin UI panels
	/// </summary>
	public interface IPluginPanel
	{
		object Content { get; }
		double PreferredWidth { get; }
		double MinWidth { get; }
		double MaxWidth { get; }
		bool IsVisible { get; set; }
		void OnPanelOpened();
		void OnPanelClosed();
		void Refresh();
	}
}


