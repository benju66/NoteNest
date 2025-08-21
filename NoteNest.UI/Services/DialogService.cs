using System;
using System.Threading.Tasks;
using System.Windows;
using NoteNest.UI.Dialogs;

namespace NoteNest.UI.Services
{
	public class DialogService : IDialogService
	{
		public Window OwnerWindow { get; set; }

		public async Task<string?> ShowInputDialogAsync(string title, string prompt, string defaultValue = "",
			Func<string, string?>? validationFunction = null)
		{
			var owner = OwnerWindow ?? Application.Current?.MainWindow;
			var dispatcher = owner?.Dispatcher ?? Application.Current?.Dispatcher;
			return await dispatcher.InvokeAsync(() =>
			{
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
			});
		}
		
		public async Task<bool> ShowConfirmationDialogAsync(string message, string title)
		{
			var owner = OwnerWindow ?? Application.Current?.MainWindow;
			var dispatcher = owner?.Dispatcher ?? Application.Current?.Dispatcher;
			return await dispatcher.InvokeAsync(() =>
			{
				var result = MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
				return result == MessageBoxResult.Yes;
			});
		}

		public async Task<bool?> ShowYesNoCancelAsync(string message, string title)
		{
			var owner = OwnerWindow ?? Application.Current?.MainWindow;
			var dispatcher = owner?.Dispatcher ?? Application.Current?.Dispatcher;
			return await dispatcher.InvokeAsync(() =>
			{
				var result = MessageBox.Show(owner, message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
				
				return result switch
				{
					MessageBoxResult.Yes => true,
					MessageBoxResult.No => false,
					_ => (bool?)null
				};
			});
		}
		
		public void ShowError(string message, string title = "Error")
		{
			var owner = OwnerWindow ?? Application.Current?.MainWindow;
			(owner?.Dispatcher ?? Application.Current?.Dispatcher)?.Invoke(() =>
				MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error));
		}
		
		public void ShowInfo(string message, string title = "Information")
		{
			var owner = OwnerWindow ?? Application.Current?.MainWindow;
			(owner?.Dispatcher ?? Application.Current?.Dispatcher)?.Invoke(() =>
				MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information));
		}
	}
}


