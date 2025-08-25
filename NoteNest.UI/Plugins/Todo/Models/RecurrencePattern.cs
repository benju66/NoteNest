using System;

namespace NoteNest.UI.Plugins.Todo.Models
{
	public class RecurrencePattern
	{
		public bool IsEnabled { get; set; }
		public RecurrenceType Type { get; set; }
		public int Interval { get; set; } = 1;
		public DayOfWeek[] DaysOfWeek { get; set; }
		public int? DayOfMonth { get; set; }
		public DateTime? EndDate { get; set; }
		public int? MaxOccurrences { get; set; }
		public int CurrentOccurrence { get; set; }

		public DateTime GetNextDate(DateTime currentDate)
		{
			if (!IsEnabled) return currentDate;
			switch (Type)
			{
				case RecurrenceType.Daily:
					return currentDate.AddDays(Interval);
				case RecurrenceType.Weekly:
					return currentDate.AddDays(7 * Interval);
				case RecurrenceType.Monthly:
					return currentDate.AddMonths(Interval);
				case RecurrenceType.Yearly:
					return currentDate.AddYears(Interval);
				case RecurrenceType.Weekdays:
					var next = currentDate.AddDays(1);
					while (next.DayOfWeek == DayOfWeek.Saturday || next.DayOfWeek == DayOfWeek.Sunday)
					{
						next = next.AddDays(1);
					}
					return next;
				default:
					return currentDate;
			}
		}

		public bool ShouldRecur()
		{
			if (!IsEnabled) return false;
			if (EndDate.HasValue && DateTime.Now > EndDate.Value) return false;
			if (MaxOccurrences.HasValue && CurrentOccurrence >= MaxOccurrences.Value) return false;
			return true;
		}

		public RecurrencePattern Clone()
		{
			return new RecurrencePattern
			{
				IsEnabled = IsEnabled,
				Type = Type,
				Interval = Interval,
				DaysOfWeek = DaysOfWeek?.Clone() as DayOfWeek[],
				DayOfMonth = DayOfMonth,
				EndDate = EndDate,
				MaxOccurrences = MaxOccurrences,
				CurrentOccurrence = CurrentOccurrence
			};
		}
	}

	public enum RecurrenceType
	{
		None,
		Daily,
		Weekly,
		Monthly,
		Yearly,
		Weekdays
	}
}


