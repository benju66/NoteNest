using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NoteNest.UI.Plugins.Todo.Models
{
	public class TodoItem : INotifyPropertyChanged
	{
		private string _id;
		private string _text;
		private bool _isCompleted;
		private DateTime? _dueDate;
		private RecurrencePattern _recurrence;
		private TodoPriority _priority;
		private string _category;
		private int _order;
		private DateTime _createdDate;
		private DateTime? _completedDate;
		private string _notes;
		private string _linkedNoteId;
		private string _linkedNoteFilePath;
		private string _sourceText;
		private int? _sourceLine;
		private string _noteTitle;

		public string Id
		{
			get => _id ??= Guid.NewGuid().ToString();
			set { _id = value; OnPropertyChanged(); }
		}

		public string Text
		{
			get => _text;
			set { _text = value; OnPropertyChanged(); }
		}

		public bool IsCompleted
		{
			get => _isCompleted;
			set
			{
				_isCompleted = value;
				CompletedDate = value ? DateTime.Now : null;
				OnPropertyChanged();
				OnPropertyChanged(nameof(CompletedDate));
			}
		}

		public DateTime? DueDate
		{
			get => _dueDate;
			set
			{
				_dueDate = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(IsOverdue));
				OnPropertyChanged(nameof(IsDueToday));
				OnPropertyChanged(nameof(IsDueTomorrow));
			}
		}

		public RecurrencePattern Recurrence
		{
			get => _recurrence;
			set { _recurrence = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsRecurring)); }
		}

		public TodoPriority Priority
		{
			get => _priority;
			set { _priority = value; OnPropertyChanged(); }
		}

		public string Category
		{
			get => _category ?? "General";
			set { _category = value; OnPropertyChanged(); }
		}

		public int Order
		{
			get => _order;
			set { _order = value; OnPropertyChanged(); }
		}

		public DateTime CreatedDate
		{
			get => _createdDate;
			set { _createdDate = value; OnPropertyChanged(); }
		}

		public DateTime? CompletedDate
		{
			get => _completedDate;
			set { _completedDate = value; OnPropertyChanged(); }
		}

		public string Notes
		{
			get => _notes;
			set { _notes = value; OnPropertyChanged(); }
		}

		// Link metadata (nullable for backward compatibility)
		public string LinkedNoteId
		{
			get => _linkedNoteId;
			set { _linkedNoteId = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsLinkedToNote)); }
		}

		public string LinkedNoteFilePath
		{
			get => _linkedNoteFilePath;
			set { _linkedNoteFilePath = value; OnPropertyChanged(); }
		}

		public string SourceText
		{
			get => _sourceText;
			set { _sourceText = value; OnPropertyChanged(); }
		}

		public int? SourceLine
		{
			get => _sourceLine;
			set { _sourceLine = value; OnPropertyChanged(); }
		}

		public string NoteTitle
		{
			get => _noteTitle;
			set { _noteTitle = value; OnPropertyChanged(); }
		}

		public bool IsOverdue => !IsCompleted && DueDate.HasValue && DueDate.Value.Date < DateTime.Today;
		public bool IsDueToday => !IsCompleted && DueDate?.Date == DateTime.Today;
		public bool IsDueTomorrow => !IsCompleted && DueDate?.Date == DateTime.Today.AddDays(1);
		public bool IsRecurring => Recurrence != null && Recurrence.IsEnabled;
		public bool IsLinkedToNote => !string.IsNullOrEmpty(LinkedNoteId);

		public TodoItem()
		{
			CreatedDate = DateTime.Now;
			Priority = TodoPriority.Normal;
		}

		public TodoItem Clone()
		{
			return new TodoItem
			{
				Text = Text,
				IsCompleted = false,
				DueDate = DueDate,
				Recurrence = Recurrence?.Clone(),
				Priority = Priority,
				Category = Category,
				Notes = Notes,
				LinkedNoteId = LinkedNoteId,
				LinkedNoteFilePath = LinkedNoteFilePath,
				SourceText = SourceText,
				SourceLine = SourceLine,
				NoteTitle = NoteTitle,
				CreatedDate = DateTime.Now
			};
		}

		public void UpdateFromRecurrence()
		{
			if (!IsRecurring || !DueDate.HasValue) return;
			DueDate = Recurrence.GetNextDate(DueDate.Value);
			IsCompleted = false;
			CompletedDate = null;
		}

		public event PropertyChangedEventHandler PropertyChanged;
		protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}

	public enum TodoPriority
	{
		Low,
		Normal,
		High,
		Urgent
	}
}


