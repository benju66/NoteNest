using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces.Split;

namespace NoteNest.Tests.Services
{
	[TestFixture]
	public class TabPersistenceServiceTests
	{
		private string _tempRoot;
		private ConfigurationService _config;
		private ITabPersistenceService _service;
		private StubWorkspaceService _workspace;

		[SetUp]
		public async Task Setup()
		{
			_tempRoot = Path.Combine(Path.GetTempPath(), "NoteNestTests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_tempRoot);
			Directory.CreateDirectory(Path.Combine(_tempRoot, ".metadata"));

			_config = new ConfigurationService();
			_config.Settings.DefaultNotePath = _tempRoot;
			_config.Settings.MetadataPath = Path.Combine(_tempRoot, ".metadata");
			await _config.SaveSettingsAsync();

		_workspace = new StubWorkspaceService();
		var mockSaveManager = new MockSaveManager();
		_service = new TabPersistenceService(_config, AppLogger.Instance, mockSaveManager);
		}

		[TearDown]
		public void Teardown()
		{
			try { _service?.Dispose(); } catch { }
			try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); } catch { }
		}

		[Test]
		public async Task SaveAndLoad_RoundTrip_WritesAndReadsState()
		{
			// Arrange
			var tab1 = new TestTabItem(new NoteModel { Id = "tab1", Title = "One", FilePath = Path.Combine(_tempRoot, "one.md"), Content = "Hello" });
			var tab2 = new TestTabItem(new NoteModel { Id = "tab2", Title = "Two", FilePath = Path.Combine(_tempRoot, "two.md"), Content = "World" });
			var tabs = new[] { tab1, tab2 };

			// Act
			await _service.SaveAsync(tabs, tab1.Note.Id, tab1.Content);
			var loaded = await _service.LoadAsync();

			// Assert
			Assert.That(loaded, Is.Not.Null);
			Assert.That(loaded.Tabs.Count, Is.EqualTo(2));
			Assert.That(loaded.ActiveTabId, Is.EqualTo("tab1"));
			Assert.That(loaded.ActiveTabContent, Is.EqualTo("Hello"));
			Assert.That(loaded.Tabs.Any(t => t.Title == "One"), Is.True);
		}

		[Test]
		public async Task MarkChanged_Debounce_SavesState()
		{
			// Arrange workspace snapshot used by debounce
			var tabA = new TestTabItem(new NoteModel { Id = "A", Title = "Alpha", FilePath = Path.Combine(_tempRoot, "a.md"), Content = "AlphaContent" });
			var tabB = new TestTabItem(new NoteModel { Id = "B", Title = "Beta", FilePath = Path.Combine(_tempRoot, "b.md"), Content = "BetaContent" });
			_workspace.OpenTabs.Add(tabA);
			_workspace.OpenTabs.Add(tabB);
			_workspace.SelectedTab = tabB;

			// Act
			_service.MarkChanged();
			await Task.Delay(1600); // wait for debounce and IO

			// Assert via service load (robust to formatting)
			var loaded = await _service.LoadAsync();
			Assert.That(loaded, Is.Not.Null);
			Assert.That(loaded.ActiveTabId, Is.EqualTo("B"));
		}

		private sealed class TestTabItem : ITabItem
		{
			public string Id => Note?.Id ?? string.Empty;
			public string Title => Note?.Title ?? string.Empty;
			public NoteModel Note { get; }
			public bool IsDirty { get; set; }
			public string Content { get; set; }
			public string NoteId => Note?.Id ?? string.Empty;
			public TestTabItem(NoteModel note)
			{
				Note = note;
				Content = note.Content;
			}
		}

		private sealed class StubWorkspaceService : IWorkspaceService
		{
			public ObservableCollection<ITabItem> OpenTabs { get; } = new ObservableCollection<ITabItem>();
			private ITabItem _selected;
			public ITabItem? SelectedTab { get => _selected; set => _selected = value; }
			public bool HasUnsavedChanges => OpenTabs.Any(t => t.IsDirty);
			public event EventHandler<TabChangedEventArgs>? TabSelectionChanged;
			public event EventHandler<TabEventArgs>? TabOpened;
			public event EventHandler<TabEventArgs>? TabClosed;
			public System.Collections.ObjectModel.ObservableCollection<SplitPane> Panes { get; } = new System.Collections.ObjectModel.ObservableCollection<SplitPane>();
			public System.Collections.ObjectModel.ObservableCollection<SplitPane> DetachedPanes { get; } = new System.Collections.ObjectModel.ObservableCollection<SplitPane>();
			public SplitPane? ActivePane { get; set; }
			public Task<ITabItem> OpenNoteAsync(NoteModel note) => Task.FromResult<ITabItem>(new TestTabItem(note));
			public Task<bool> CloseTabAsync(ITabItem tab) => Task.FromResult(true);
			public Task<bool> CloseAllTabsAsync() => Task.FromResult(true);
			public Task SaveAllTabsAsync() => Task.CompletedTask;
			public ITabItem? FindTabByNote(NoteModel note) => null;
			public ITabItem? FindTabByPath(string filePath) => null;
			public Task<SplitPane> SplitPaneAsync(SplitPane pane, SplitOrientation orientation) => Task.FromResult(pane);
			public Task ClosePaneAsync(SplitPane pane) => Task.CompletedTask;
			public Task MoveTabToPaneAsync(ITabItem tab, SplitPane targetPane) => Task.CompletedTask;
			public Task MoveTabToPaneAsync(ITabItem tab, SplitPane targetPane, int targetIndex) => Task.CompletedTask;
			public void SetActivePane(SplitPane pane) { }
			public void RegisterPane(SplitPane pane) { }
			public void UnregisterPane(SplitPane pane) { }
			public System.Collections.Generic.IEnumerable<object> GetActivePanes() { yield break; }
			public Task<bool> MoveTabToPaneAsync(ITabItem tab, object targetPane) => Task.FromResult(false);
		}

		private class MockSaveManager : ISaveManager
		{
			public event EventHandler<NoteSavedEventArgs>? NoteSaved;
			public event EventHandler<SaveProgressEventArgs>? SaveStarted;
			public event EventHandler<SaveProgressEventArgs>? SaveCompleted;
			public event EventHandler<ExternalChangeEventArgs>? ExternalChangeDetected;

			public async Task<string> OpenNoteAsync(string filePath) => System.Guid.NewGuid().ToString();
			public void UpdateContent(string noteId, string content) { }
			public async Task<bool> SaveNoteAsync(string noteId) => true;
			public async Task<BatchSaveResult> SaveAllDirtyAsync() => new BatchSaveResult();
			public async Task<bool> CloseNoteAsync(string noteId) => true;
			public bool IsNoteDirty(string noteId) => false;
			public bool IsSaving(string noteId) => false;
			public string GetContent(string noteId) => "";
			public string? GetLastSavedContent(string noteId) => null;
			public string? GetFilePath(string noteId) => null;
			public string? GetNoteIdForPath(string filePath) => null;
			public IReadOnlyList<string> GetDirtyNoteIds() => new List<string>();
			public async Task<bool> ResolveExternalChangeAsync(string noteId, ConflictResolution resolution) => true;
			public void Dispose() { }
		}
	}
}


