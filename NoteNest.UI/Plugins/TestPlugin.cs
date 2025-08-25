using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NoteNest.Core.Plugins;

namespace NoteNest.UI.Plugins
{
	public class TestPlugin : PluginBase
	{
		private TestPanel _panel;

		public override string Id => "test-plugin";
		public override string Name => "Test Plugin";
		public override string Icon => "🧪";
		public override Version Version => new Version(1, 0, 0);
		public override string Description => "A simple test plugin.";

		protected override Task OnInitializeAsync()
		{
			_panel = new TestPanel();
			return Task.CompletedTask;
		}

		protected override Task OnShutdownAsync()
		{
			_panel = null;
			return Task.CompletedTask;
		}

		protected override void OnActivate()
		{
			// No-op for test
		}

		public override IPluginPanel GetPanel() => _panel;
		public override IPluginSettings GetSettings() => new TestSettings();
	}

	public class TestPanel : IPluginPanel
	{
		private readonly Border _root;
		public TestPanel()
		{
			_root = new Border
			{
				Child = new TextBlock
				{
					Text = "Test Plugin Panel",
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center
				},
				Padding = new Thickness(16)
			};
		}

		public object Content => _root;
		public double PreferredWidth => 300;
		public double MinWidth => 200;
		public double MaxWidth => 500;
		public bool IsVisible { get; set; }
		public void OnPanelOpened() { }
		public void OnPanelClosed() { }
		public void Refresh() { }
	}

	public class TestSettings : IPluginSettings
	{
		public System.Collections.Generic.Dictionary<string, object> ToDictionary() => new();
		public void FromDictionary(System.Collections.Generic.Dictionary<string, object> settings) { }
		public void ResetToDefaults() { }
		public bool Validate(out string errorMessage) { errorMessage = null; return true; }
	}
}


