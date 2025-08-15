using System;
using System.Threading.Tasks;

namespace NoteNest.UI.Services
{
	public interface IDialogService
	{
		Task<string?> ShowInputDialogAsync(string title, string prompt, string defaultValue = "",
			Func<string, string?>? validationFunction = null);
		Task<bool> ShowConfirmationDialogAsync(string message, string title);
		Task<bool?> ShowYesNoCancelAsync(string message, string title);
		void ShowError(string message, string title = "Error");
		void ShowInfo(string message, string title = "Information");
	}
}


