using System;
using System.Windows;
using System.Windows.Controls;
using NoteNest.UI.Services;

namespace NoteNest.UI.Controls
{
	public partial class NotificationHost : UserControl
	{
		public NotificationHost()
		{
			InitializeComponent();
		}

		private void CloseToast_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				if (DataContext is ToastNotificationService svc && sender is Button b && b.Tag is Guid id)
				{
					svc.Remove(id);
				}
			}
			catch { }
		}
	}
}
