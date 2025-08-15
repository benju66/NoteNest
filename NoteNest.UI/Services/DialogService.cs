using System;
using System.Threading.Tasks;
using System.Windows;
using NoteNest.UI.Dialogs;

namespace NoteNest.UI.Services
{
	public class DialogService : IDialogService
	{
		public async Task<string?> ShowInputDialogAsync(string title, string prompt, string defaultValue = "",
			Func<string, string?>? validationFunction = null)
		{
			return await Application.Current.Dispatcher.InvokeAsync(() =>
			{
				var dialog = new InputDialog(title, prompt, defaultValue);
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
			return await Application.Current.Dispatcher.InvokeAsync(() =>
			{
				var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
				return result == MessageBoxResult.Yes;
			});
		}

		public async Task<bool?> ShowYesNoCancelAsync(string message, string title)
		{
			return await Application.Current.Dispatcher.InvokeAsync(() =>
			{
				var result = MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
				
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
			Application.Current.Dispatcher.Invoke(() =>
				MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error));
		}
		
		public void ShowInfo(string message, string title = "Information")
		{
			Application.Current.Dispatcher.Invoke(() =>
				MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information));
		}
	}
}


