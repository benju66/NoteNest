using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MediatR;
using NoteNest.Application.Notes.Commands.SaveNote;
using NoteNest.UI.ViewModels.Common;
using NoteNest.UI.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Notes;
using NoteNest.Core.Models;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services;

namespace NoteNest.UI.ViewModels.Workspace
{
    /// <summary>
    /// Modern, focused workspace ViewModel - replaces workspace logic from MainViewModel
    /// </summary>
    public class ModernWorkspaceViewModel : ViewModelBase
    {
        private readonly IMediator _mediator;
        private readonly IDialogService _dialogService;
        private readonly IAppLogger _logger;
        private readonly INoteRepository _noteRepository;
        private readonly ISaveManager _saveManager;
        private NoteTabItem _selectedTab;
        private bool _isLoading;
        private string _statusMessage;

        public ModernWorkspaceViewModel(
            IMediator mediator,
            IDialogService dialogService,
            IAppLogger logger,
            INoteRepository noteRepository,
            ISaveManager saveManager)
        {
            _mediator = mediator;
            _dialogService = dialogService;
            _logger = logger;
            _noteRepository = noteRepository;
            _saveManager = saveManager;
            
            OpenTabs = new ObservableCollection<NoteTabItem>();
            InitializeCommands();
        }

        public ObservableCollection<NoteTabItem> OpenTabs { get; }

        public NoteTabItem SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (SetProperty(ref _selectedTab, value))
                {
                    TabSelected?.Invoke(value);
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand SaveTabCommand { get; private set; }
        public ICommand SaveAllTabsCommand { get; private set; }
        public ICommand CloseTabCommand { get; private set; }
        public ICommand CloseAllTabsCommand { get; private set; }

        // Events for coordination with other ViewModels
        public event Action<NoteTabItem> TabSelected;
        public event Action<NoteTabItem> TabClosed;
        public event Action<string> NoteOpened;

        private void InitializeCommands()
        {
            SaveTabCommand = new AsyncRelayCommand<NoteTabItem>(ExecuteSaveTab, CanSaveTab);
            SaveAllTabsCommand = new AsyncRelayCommand(ExecuteSaveAllTabs, CanSaveAllTabs);
            CloseTabCommand = new AsyncRelayCommand<NoteTabItem>(ExecuteCloseTab, CanCloseTab);
            CloseAllTabsCommand = new AsyncRelayCommand(ExecuteCloseAllTabs, CanCloseAllTabs);
        }

        public async Task OpenNoteAsync(string noteId, string noteTitle)
        {
            try
            {
                IsLoading = true;
                StatusMessage = $"Opening {noteTitle}...";

                // Check if already open
                var existingTab = OpenTabs.FirstOrDefault(t => t.NoteId == noteId);
                if (existingTab != null)
                {
                    SelectedTab = existingTab;
                    StatusMessage = $"Switched to {noteTitle}";
                    return;
                }

                // Load note from database and create NoteModel for RTF editor
                NoteModel noteModel = null;
                try
                {
                    var noteIdValue = NoteId.From(noteId);
                    var domainNote = await _noteRepository.GetByIdAsync(noteIdValue);
                    if (domainNote != null)
                    {
                        // Convert Domain.Note to Core.Models.NoteModel for RTF editor
                        noteModel = new NoteModel
                        {
                            Id = domainNote.Id.Value,
                            Title = domainNote.Title,
                            Content = domainNote.Content ?? "",
                            FilePath = domainNote.FilePath ?? "",
                            CategoryId = domainNote.CategoryId.Value,
                            LastModified = domainNote.UpdatedAt
                        };
                        
                        _logger.Info($"⚡ Loaded note from database for RTF editor: {noteModel.Title}");
                    }
                    else
                    {
                        _logger.Warning($"Note not found in database: {noteId}");
                        // Create placeholder note model
                        noteModel = new NoteModel
                        {
                            Id = noteId,
                            Title = noteTitle,
                            Content = "Note not found in database.",
                            FilePath = "",
                            CategoryId = "",
                            LastModified = DateTime.Now
                        };
                    }
                }
                catch (Exception loadEx)
                {
                    _logger.Error(loadEx, $"Failed to load note from database: {noteId}");
                    // Create error note model
                    noteModel = new NoteModel
                    {
                        Id = noteId,
                        Title = noteTitle,
                        Content = $"Error loading note: {loadEx.Message}",
                        FilePath = "",
                        CategoryId = "",
                        LastModified = DateTime.Now
                    };
                }

                // Create sophisticated NoteTabItem with RTF editor (correct parameter order)
                var noteTabItem = new NoteTabItem(noteModel, _saveManager);
                
                // Load content into RTF editor
                if (!string.IsNullOrEmpty(noteModel.Content))
                {
                    noteTabItem.LoadContent(noteModel.Content);
                }

                OpenTabs.Add(noteTabItem);
                SelectedTab = noteTabItem;
                
                StatusMessage = $"Opened {noteModel.Title} in RTF editor";
                NoteOpened?.Invoke(noteId);
                
                _logger.Info($"🎯 RTF editor integrated: {noteModel.Title}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to open note: {ex.Message}";
                _dialogService.ShowError(ex.Message, "Open Note Error");
                _logger.Error(ex, $"Exception opening note: {noteId}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ExecuteSaveTab(NoteTabItem tab)
        {
            if (tab == null)
                return;

            try
            {
                IsLoading = true;
                StatusMessage = $"Saving {tab.Title}...";

                // Use the RTF-aware save system
                await tab.SaveAsync();
                
                StatusMessage = $"Saved {tab.Title}";
                _logger.Info($"✅ RTF content saved: {tab.Title}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving {tab.Title}: {ex.Message}";
                _dialogService.ShowError(ex.Message, "Save Error");
                _logger.Error(ex, $"Exception saving RTF note: {tab.NoteId}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ExecuteSaveAllTabs()
        {
            var dirtyTabs = OpenTabs.Where(t => t.IsDirty).ToList();
            if (!dirtyTabs.Any())
            {
                StatusMessage = "No changes to save";
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = $"Saving {dirtyTabs.Count} tabs...";

                int savedCount = 0;
                int failedCount = 0;

                foreach (var tab in dirtyTabs)
                {
                    try
                    {
                        // Use RTF-aware save system
                        await tab.SaveAsync();
                        savedCount++;
                    }
                    catch
                    {
                        failedCount++;
                    }
                }

                if (failedCount > 0)
                {
                    StatusMessage = $"Saved {savedCount} tabs, {failedCount} failed";
                    _dialogService.ShowInfo($"Failed to save {failedCount} tabs", "Save Results");
                }
                else
                {
                    StatusMessage = $"Saved {savedCount} tabs";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during save all: {ex.Message}";
                _dialogService.ShowError(ex.Message, "Save All Error");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ExecuteCloseTab(NoteTabItem tab)
        {
            if (tab == null)
                return;

            try
            {
                // Check if dirty and prompt to save
                if (tab.IsDirty)
                {
                    var result = await _dialogService.ShowConfirmationDialogAsync(
                        $"'{tab.Title}' has unsaved changes. Save before closing?",
                        "Unsaved Changes");

                    if (result)
                    {
                        await ExecuteSaveTab(tab);
                    }
                }

                // Properly dispose RTF editor resources
                tab.Dispose();
                
                OpenTabs.Remove(tab);
                TabClosed?.Invoke(tab);
                
                // Select another tab if this was selected
                if (SelectedTab == tab)
                {
                    SelectedTab = OpenTabs.LastOrDefault();
                }

                StatusMessage = $"Closed {tab.Title}";
                _logger.Info($"🗑️ RTF tab closed and disposed: {tab.Title}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error closing tab: {ex.Message}";
                _dialogService.ShowError(ex.Message, "Close Tab Error");
            }
        }

        private async Task ExecuteCloseAllTabs()
        {
            var dirtyTabs = OpenTabs.Where(t => t.IsDirty).ToList();
            if (dirtyTabs.Any())
            {
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    $"You have {dirtyTabs.Count} tabs with unsaved changes. Save all before closing?",
                    "Unsaved Changes");

                if (result)
                {
                    await ExecuteSaveAllTabs();
                }
            }

            // Properly dispose all RTF editors
            foreach (var tab in OpenTabs.ToList())
            {
                try
                {
                    tab.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error disposing RTF tab: {tab.Title}");
                }
            }
            
            OpenTabs.Clear();
            SelectedTab = null;
            StatusMessage = "Closed all tabs";
            _logger.Info("🗑️ All RTF tabs closed and disposed");
        }

        private bool CanSaveTab(NoteTabItem tab) => !IsLoading && tab?.IsDirty == true;
        private bool CanSaveAllTabs() => !IsLoading && OpenTabs.Any(t => t.IsDirty);
        private bool CanCloseTab(NoteTabItem tab) => !IsLoading && tab != null;
        private bool CanCloseAllTabs() => !IsLoading && OpenTabs.Any();
    }
}
