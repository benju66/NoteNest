using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using NoteNest.UI.Services;
using Microsoft.Win32;

namespace NoteNest.UI.Windows
{
	public partial class IntegrityDiagnosticsWindow : Window
	{
		private readonly IntegrityCheckerService _checker;
		public ObservableCollection<IntegrityIssue> Issues { get; } = new ObservableCollection<IntegrityIssue>();

		public IntegrityDiagnosticsWindow(IntegrityCheckerService checker)
		{
			_checker = checker;
			InitializeComponent();
			IssuesList.ItemsSource = Issues;
			_ = LoadAsync();
		}

		private async Task LoadAsync()
		{
			var found = await _checker.ScanAsync();
			Issues.Clear();
			foreach (var i in found) Issues.Add(i);
		}

		private async void Relink_Click(object sender, RoutedEventArgs e)
		{
			if (IssuesList?.SelectedItem is IntegrityIssue issue && issue.Task != null)
			{
				var dlg = new OpenFileDialog
				{
					Filter = "Notes (*.md;*.txt)|*.md;*.txt|All files (*.*)|*.*"
				};
				if (dlg.ShowDialog(this) == true)
				{
					issue.Task.LinkedNoteFilePath = dlg.FileName;
					// Persist via service update
					await _checker.ClearLinkAsync(issue.Task); // Clear first
					issue.Task.LinkedNoteId = issue.Task.LinkedNoteId; // keep id
					// Re-add path by updating task
					var sp = (Application.Current as App)?.ServiceProvider;
					var todos = sp?.GetService(typeof(NoteNest.UI.Plugins.Todo.Services.ITodoService)) as NoteNest.UI.Plugins.Todo.Services.ITodoService;
					if (todos != null)
					{
						await todos.UpdateTaskAsync(issue.Task);
					}
					await LoadAsync();
				}
			}
		}

		private async void Clear_Click(object sender, RoutedEventArgs e)
		{
			if (IssuesList?.SelectedItem is IntegrityIssue issue && issue.Task != null)
			{
				await _checker.ClearLinkAsync(issue.Task);
				await LoadAsync();
			}
		}

		private async void Ignore_Click(object sender, RoutedEventArgs e)
		{
			if (IssuesList?.SelectedItem is IntegrityIssue issue && issue.Task != null)
			{
				await _checker.IgnoreTaskAsync(issue.Task.Id);
				await LoadAsync();
			}
		}
	}
}


