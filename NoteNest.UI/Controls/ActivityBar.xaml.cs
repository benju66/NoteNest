using System.Windows;
using System.Windows.Controls;

namespace NoteNest.UI.Controls
{
	public partial class ActivityBar : UserControl
	{
		public ActivityBar()
		{
			InitializeComponent();
		}

		private void SettingsButton_Click(object sender, RoutedEventArgs e)
		{
			var win = Window.GetWindow(this) as NewMainWindow;
			win?.OpenSettings();
		}
	}
}


