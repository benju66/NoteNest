using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace NoteNest.UI.Services
{
	public class ToastNotificationService
	{
		private readonly Dispatcher _dispatcher;
		private const int MaxToasts = 3;
		private const int ToastDurationMs = 3000;
		public ObservableCollection<ToastMessage> Messages { get; }

		public ToastNotificationService()
		{
                    _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
			Messages = new ObservableCollection<ToastMessage>();
		}

		public void Show(string message, ToastType type = ToastType.Info)
		{
			if (string.IsNullOrWhiteSpace(message)) return;
			_dispatcher.BeginInvoke(new Action(async () =>
			{
				while (Messages.Count >= MaxToasts)
				{
					Messages.RemoveAt(0);
				}
				var toast = new ToastMessage
				{
					Id = Guid.NewGuid(),
					Message = message,
					Type = type,
					Timestamp = DateTime.Now
				};
				Messages.Add(toast);
				await Task.Delay(ToastDurationMs);
				Messages.Remove(toast);
			}));
		}

		public void Remove(Guid id)
		{
			var found = Messages.FirstOrDefault(m => m.Id == id);
			if (found != null)
			{
				_dispatcher.BeginInvoke(new Action(() => Messages.Remove(found)));
			}
		}

		public void Info(string message) => Show(message, ToastType.Info);
		public void Success(string message) => Show(message, ToastType.Success);
		public void Warning(string message) => Show(message, ToastType.Warning);
		public void Error(string message) => Show(message, ToastType.Error);
	}

	public class ToastMessage
	{
		public Guid Id { get; set; }
		public string Message { get; set; }
		public ToastType Type { get; set; }
		public DateTime Timestamp { get; set; }
	}

	public enum ToastType
	{
		Info,
		Success,
		Warning,
		Error
	}
}
