using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Services
{
	/// <summary>
	/// User notification service using standard WPF MessageBox
	/// Migrated from ModernWPF to standard WPF dialogs
	/// </summary>
	public class UserNotificationService : IUserNotificationService
	{
		private readonly Window? _mainWindow;
		private readonly Dispatcher _dispatcher;
		private readonly IAppLogger? _logger;

		public UserNotificationService(Window? mainWindow = null, IAppLogger? logger = null)
		{
			_dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
			_logger = logger;
			_mainWindow = mainWindow;
		}

		public async Task ShowErrorAsync(string message, Exception? exception = null)
		{
			_logger?.Error(exception, message);
			await _dispatcher.InvokeAsync(() =>
			{
				var content = exception != null ? $"{message}\n\nDetails: {exception.Message}" : message;
				var owner = GetSafeOwner();
				MessageBox.Show(owner, content, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			});
		}

		public async Task ShowWarningAsync(string message)
		{
			await _dispatcher.InvokeAsync(() =>
			{
				var owner = GetSafeOwner();
				MessageBox.Show(owner, message, "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
			});
		}

		public async Task ShowInfoAsync(string message)
		{
			await _dispatcher.InvokeAsync(() =>
			{
				var owner = GetSafeOwner();
				MessageBox.Show(owner, message, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
			});
		}

		public async Task<bool> ShowConfirmationAsync(string message, string title = "Confirm")
		{
			var result = false;
			await _dispatcher.InvokeAsync(() =>
			{
				var owner = GetSafeOwner();
				var messageBoxResult = MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
				result = messageBoxResult == MessageBoxResult.Yes;
			});
			return result;
		}

		public async Task<bool> AskYesNoAsync(string title, string message)
		{
			return await ShowConfirmationAsync(message, title);
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

		private Window? GetSafeOwner()
		{
			if (!_dispatcher.CheckAccess()) return null;
            var owner = _mainWindow ?? System.Windows.Application.Current?.MainWindow;
			return (owner != null && owner.IsLoaded && owner.IsVisible) ? owner : null;
		}
	}
}