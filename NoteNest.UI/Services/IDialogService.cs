using System;
using System.Threading.Tasks;
using System.Windows;

namespace NoteNest.UI.Services
{
	public interface IDialogService
	{
		Window OwnerWindow { get; set; }
		Task<string?> ShowInputDialogAsync(string title, string prompt, string defaultValue = "",
			Func<string, string?>? validationFunction = null);
		Task<string?> ShowFolderDialogAsync(string title, string? initialDirectory = null);
		Task<bool> ShowConfirmationDialogAsync(string message, string title);
		Task<bool?> ShowYesNoCancelAsync(string message, string title);
		void ShowError(string message, string title = "Error");
		void ShowInfo(string message, string title = "Information");
	}
}


