using System;
using System.Threading.Tasks;
using System.Windows;
using NoteNest.UI.Dialogs;
using System.Windows.Threading;
#nullable enable

namespace NoteNest.UI.Services
{
	public class DialogService : IDialogService
	{
		private readonly Dispatcher _dispatcher;
		public Window? OwnerWindow { get; set; }

		public DialogService()
		{
			_dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
		}

		public async Task<string?> ShowInputDialogAsync(string title, string prompt, string defaultValue = "",
			Func<string, string?>? validationFunction = null)
		{
			if (_dispatcher.CheckAccess())
			{
				return ShowInputDialogCore(title, prompt, defaultValue, validationFunction);
			}

			return await _dispatcher.InvokeAsync(() =>
				ShowInputDialogCore(title, prompt, defaultValue, validationFunction));
		}

		private string? ShowInputDialogCore(string title, string prompt, string defaultValue,
			Func<string, string?>? validationFunction)
		{
			var owner = GetSafeOwner();
			if (owner == null) return null;

			var dialog = new InputDialog(title, prompt, defaultValue)
			{
				Owner = owner
			};
			if (validationFunction != null)
			{
				// InputDialog expects Func<string, string> where empty string means valid
				dialog.ValidationFunction = (value) => validationFunction(value) ?? string.Empty;
			}
			
			if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResponseText))
			{
				return dialog.ResponseText;
			}
			return null;
		}
		
		public async Task<bool> ShowConfirmationDialogAsync(string message, string title)
		{
			if (_dispatcher.CheckAccess())
			{
				return ShowConfirmationDialogCore(message, title);
			}

			return await _dispatcher.InvokeAsync(() =>
				ShowConfirmationDialogCore(message, title));
		}

		private bool ShowConfirmationDialogCore(string message, string title)
		{
			var owner = GetSafeOwner();
			if (owner == null) return false;
			var result = MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
			return result == MessageBoxResult.Yes;
		}

		public async Task<bool?> ShowYesNoCancelAsync(string message, string title)
		{
			if (_dispatcher.CheckAccess())
			{
				return ShowYesNoCancelCore(message, title);
			}

			return await _dispatcher.InvokeAsync(() => ShowYesNoCancelCore(message, title));
		}

		private bool? ShowYesNoCancelCore(string message, string title)
		{
			var owner = GetSafeOwner();
			if (owner == null) return null;
			var result = MessageBox.Show(owner, message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
			return result switch
			{
				MessageBoxResult.Yes => true,
				MessageBoxResult.No => false,
				_ => (bool?)null
			};
		}
		
		public void ShowError(string message, string title = "Error")
		{
			Action showAction = () =>
			{
				var owner = GetSafeOwner();
				if (owner != null)
				{
					MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
				}
			};
			if (_dispatcher.CheckAccess()) showAction(); else _dispatcher.BeginInvoke(showAction);
		}
		
		public void ShowInfo(string message, string title = "Information")
		{
			Action showAction = () =>
			{
				var owner = GetSafeOwner();
				if (owner != null)
				{
					MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
				}
			};
			if (_dispatcher.CheckAccess()) showAction(); else _dispatcher.BeginInvoke(showAction);
		}

		private Window? GetSafeOwner()
		{
			if (!_dispatcher.CheckAccess()) return null;
			var owner = OwnerWindow ?? Application.Current?.MainWindow;
			if (owner == null || !owner.IsLoaded || !owner.IsVisible) return null;
			return owner;
		}
	}
}


