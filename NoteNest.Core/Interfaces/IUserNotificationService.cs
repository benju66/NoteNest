using System;
using System.Threading.Tasks;

namespace NoteNest.Core.Interfaces
{
	public interface IUserNotificationService
	{
		Task ShowErrorAsync(string message, Exception? exception = null);
		Task ShowWarningAsync(string message);
		Task ShowInfoAsync(string message);
		Task<bool> ShowConfirmationAsync(string message, string title = "Confirm");
		void ShowToast(string message, NotificationType type = NotificationType.Info);
	}

	public enum NotificationType
	{
		Info,
		Success,
		Warning,
		Error
	}
}


