using System;
using System.Collections.Generic;
using System.Linq;

namespace NoteNest.UI.Plugins.Todo.Models
{
	public class TodoStorage
	{
		public Dictionary<string, List<TodoItem>> Categories { get; set; }
		public TodoSettings Settings { get; set; }
		public DateTime LastModified { get; set; }
		public int Version { get; set; } = 1;

		public TodoStorage()
		{
			Categories = new Dictionary<string, List<TodoItem>>();
			Settings = new TodoSettings();
			LastModified = DateTime.Now;
		}

		public void AddCategory(string category)
		{
			if (!Categories.ContainsKey(category))
			{
				Categories[category] = new List<TodoItem>();
			}
		}

		public void RemoveCategory(string category)
		{
			if (category != "General" && Categories.ContainsKey(category))
			{
				if (!Categories.ContainsKey("General"))
					Categories["General"] = new List<TodoItem>();
				Categories["General"].AddRange(Categories[category]);
				Categories.Remove(category);
			}
		}

		public void AddTask(TodoItem task)
		{
			var category = task.Category ?? "General";
			AddCategory(category);
			Categories[category].Add(task);
			LastModified = DateTime.Now;
		}

		public void RemoveTask(TodoItem task)
		{
			var category = task.Category ?? "General";
			if (Categories.ContainsKey(category))
			{
				Categories[category].Remove(task);
				LastModified = DateTime.Now;
			}
		}

		public void MoveTask(TodoItem task, string newCategory)
		{
			RemoveTask(task);
			task.Category = newCategory;
			AddTask(task);
		}

		public List<TodoItem> GetAllTasks()
		{
			return Categories.Values.SelectMany(list => list).ToList();
		}

		public List<TodoItem> GetTasksByCategory(string category)
		{
			return Categories.TryGetValue(category, out var tasks) ? tasks : new List<TodoItem>();
		}

		public List<TodoItem> GetTodayTasks()
		{
			return GetAllTasks().Where(t => t.IsDueToday || (t.DueDate == null && !t.IsCompleted))
				.OrderBy(t => t.Priority)
				.ThenBy(t => t.Order)
				.ToList();
		}

		public List<TodoItem> GetOverdueTasks()
		{
			return GetAllTasks().Where(t => t.IsOverdue)
				.OrderBy(t => t.DueDate)
				.ToList();
		}

		public int GetActiveTaskCount()
		{
			return GetAllTasks().Count(t => !t.IsCompleted);
		}

		public int GetCompletedTaskCount()
		{
			return GetAllTasks().Count(t => t.IsCompleted);
		}
	}

	public class TodoSettings
	{
		public bool ShowCompletedTasks { get; set; } = true;
		public bool AutoDeleteCompleted { get; set; } = false;
		public int AutoDeleteAfterDays { get; set; } = 30;
		public bool ShowDueDates { get; set; } = true;
		public bool ShowPriority { get; set; } = true;
		public bool EnableNotifications { get; set; } = true;
		public bool GroupByCategory { get; set; } = true;
		public TodoSortOrder SortOrder { get; set; } = TodoSortOrder.Priority;
		public bool ShowTodayOnStartup { get; set; } = true;
	}

	public enum TodoSortOrder
	{
		Manual,
		Priority,
		DueDate,
		Alphabetical,
		CreatedDate
	}
}


