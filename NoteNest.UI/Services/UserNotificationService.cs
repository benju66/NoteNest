using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ModernWpf.Controls;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Services
{
	public class UserNotificationService : IUserNotificationService
	{
		private readonly IAppLogger? _logger;
		private readonly Dispatcher _dispatcher;
		private Window? _mainWindow;

		public UserNotificationService(Window? mainWindow = null, IAppLogger? logger = null)
		{
			_logger = logger ?? AppLogger.Instance;
			_dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
			_mainWindow = mainWindow;
		}

		public async Task ShowErrorAsync(string message, Exception? exception = null)
		{
			_logger?.Error(exception, message);
			if (_dispatcher.CheckAccess())
			{
				await ShowDialogCoreAsync(() => new ContentDialog
				{
					Title = "Error",
					Content = exception != null ? $"{message}\n\nDetails: {exception.Message}" : message,
					CloseButtonText = "OK",
					DefaultButton = ContentDialogButton.Close
				});
				return;
			}
			await _dispatcher.InvokeAsync(async () =>
			{
				await ShowDialogCoreAsync(() => new ContentDialog
				{
					Title = "Error",
					Content = exception != null ? $"{message}\n\nDetails: {exception.Message}" : message,
					CloseButtonText = "OK",
					DefaultButton = ContentDialogButton.Close
				});
			});
		}

		public async Task ShowWarningAsync(string message)
		{
			if (_dispatcher.CheckAccess())
			{
				await ShowDialogCoreAsync(() => new ContentDialog
				{
					Title = "Warning",
					Content = message,
					CloseButtonText = "OK",
					DefaultButton = ContentDialogButton.Close
				});
				return;
			}
			await _dispatcher.InvokeAsync(async () =>
			{
				await ShowDialogCoreAsync(() => new ContentDialog
				{
					Title = "Warning",
					Content = message,
					CloseButtonText = "OK",
					DefaultButton = ContentDialogButton.Close
				});
			});
		}

		public async Task ShowInfoAsync(string message)
		{
			if (_dispatcher.CheckAccess())
			{
				await ShowDialogCoreAsync(() => new ContentDialog
				{
					Title = "Information",
					Content = message,
					CloseButtonText = "OK",
					DefaultButton = ContentDialogButton.Close
				});
				return;
			}
			await _dispatcher.InvokeAsync(async () =>
			{
				await ShowDialogCoreAsync(() => new ContentDialog
				{
					Title = "Information",
					Content = message,
					CloseButtonText = "OK",
					DefaultButton = ContentDialogButton.Close
				});
			});
		}

		public async Task<bool> ShowConfirmationAsync(string message, string title = "Confirm")
		{
			if (_dispatcher.CheckAccess())
			{
				var r = await ShowDialogCoreAsync(() => new ContentDialog
				{
					Title = title,
					Content = message,
					PrimaryButtonText = "Yes",
					CloseButtonText = "No",
					DefaultButton = ContentDialogButton.Primary
				});
				return r == ContentDialogResult.Primary;
			}
			var result = await (await _dispatcher.InvokeAsync(() => ShowDialogCoreAsync(() => new ContentDialog
			{
				Title = title,
				Content = message,
				PrimaryButtonText = "Yes",
				CloseButtonText = "No",
				DefaultButton = ContentDialogButton.Primary
			}))); 
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

		private async Task<ContentDialogResult> ShowDialogCoreAsync(Func<ContentDialog> createDialog)
		{
			var owner = GetSafeOwner();
			if (owner == null) return ContentDialogResult.None;
			var dialog = createDialog();
			dialog.Owner = owner;
			try
			{
				return await dialog.ShowAsync();
			}
			catch
			{
				return ContentDialogResult.None;
			}
		}

		private Window? GetSafeOwner()
		{
			if (!_dispatcher.CheckAccess()) return null;
			var owner = _mainWindow ?? Application.Current?.MainWindow;
			return (owner != null && owner.IsLoaded && owner.IsVisible) ? owner : null;
		}
	}
}


