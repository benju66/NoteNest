using System;
using System.Threading.Tasks;
using System.Windows;
using ModernWpf.Controls;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Services
{
	public class UserNotificationService : IUserNotificationService
	{
		private readonly Window? _mainWindow;
		private readonly IAppLogger? _logger;

		public UserNotificationService(Window? mainWindow = null, IAppLogger? logger = null)
		{
			_mainWindow = mainWindow ?? Application.Current?.MainWindow;
			_logger = logger ?? AppLogger.Instance;
		}

		public async Task ShowErrorAsync(string message, Exception? exception = null)
		{
			_logger?.Error(exception, message);
			var dialog = new ContentDialog
			{
				Title = "Error",
				Content = exception != null ? $"{message}\n\nDetails: {exception.Message}" : message,
				CloseButtonText = "OK",
				DefaultButton = ContentDialogButton.Close
			};

			AttachOwner(dialog);
			await dialog.ShowAsync();
		}

		public async Task ShowWarningAsync(string message)
		{
			var dialog = new ContentDialog
			{
				Title = "Warning",
				Content = message,
				CloseButtonText = "OK",
				DefaultButton = ContentDialogButton.Close
			};
			AttachOwner(dialog);
			await dialog.ShowAsync();
		}

		public async Task ShowInfoAsync(string message)
		{
			var dialog = new ContentDialog
			{
				Title = "Information",
				Content = message,
				CloseButtonText = "OK",
				DefaultButton = ContentDialogButton.Close
			};
			AttachOwner(dialog);
			await dialog.ShowAsync();
		}

		public async Task<bool> ShowConfirmationAsync(string message, string title = "Confirm")
		{
			var dialog = new ContentDialog
			{
				Title = title,
				Content = message,
				PrimaryButtonText = "Yes",
				CloseButtonText = "No",
				DefaultButton = ContentDialogButton.Primary
			};
			AttachOwner(dialog);
			var result = await dialog.ShowAsync();
			return result == ContentDialogResult.Primary;
		}

		public void ShowToast(string message, NotificationType type = NotificationType.Info)
		{
			// Placeholder: using dialogs until a toast host is introduced
			_ = type switch
			{
				NotificationType.Error => ShowErrorAsync(message),
				NotificationType.Warning => ShowWarningAsync(message),
				_ => ShowInfoAsync(message)
			};
		}

		private void AttachOwner(ContentDialog dialog)
		{
			try
			{
				if (_mainWindow != null)
				{
					dialog.Owner = _mainWindow;
				}
			}
			catch { }
		}
	}
}


