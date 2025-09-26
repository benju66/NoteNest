using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MediatR;
using NoteNest.Application.Notes.Commands.SaveNote;
using NoteNest.UI.ViewModels.Common;
using NoteNest.UI.Services;

namespace NoteNest.UI.ViewModels.Workspace
{
    /// <summary>
    /// Modern, focused workspace ViewModel - replaces workspace logic from MainViewModel
    /// </summary>
    public class ModernWorkspaceViewModel : ViewModelBase
    {
        private readonly IMediator _mediator;
        private readonly IDialogService _dialogService;
        private TabViewModel _selectedTab;
        private bool _isLoading;
        private string _statusMessage;

        public ModernWorkspaceViewModel(
            IMediator mediator,
            IDialogService dialogService)
        {
            _mediator = mediator;
            _dialogService = dialogService;
            
            OpenTabs = new ObservableCollection<TabViewModel>();
            InitializeCommands();
        }

        public ObservableCollection<TabViewModel> OpenTabs { get; }

        public TabViewModel SelectedTab
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
        public event Action<TabViewModel> TabSelected;
        public event Action<TabViewModel> TabClosed;
        public event Action<string> NoteOpened;

        private void InitializeCommands()
        {
            SaveTabCommand = new AsyncRelayCommand<TabViewModel>(ExecuteSaveTab, CanSaveTab);
            SaveAllTabsCommand = new AsyncRelayCommand(ExecuteSaveAllTabs, CanSaveAllTabs);
            CloseTabCommand = new AsyncRelayCommand<TabViewModel>(ExecuteCloseTab, CanCloseTab);
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

                // Create new tab
                var tabViewModel = new TabViewModel(noteId, noteTitle, "");
                OpenTabs.Add(tabViewModel);
                SelectedTab = tabViewModel;
                
                StatusMessage = $"Opened {noteTitle}";
                NoteOpened?.Invoke(noteId);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to open note: {ex.Message}";
                _dialogService.ShowError(ex.Message, "Open Note Error");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ExecuteSaveTab(TabViewModel tab)
        {
            if (tab == null)
                return;

            try
            {
                IsLoading = true;
                StatusMessage = $"Saving {tab.Title}...";

                var command = new SaveNoteCommand
                {
                    NoteId = tab.NoteId,
                    Content = tab.Content,
                    IsManualSave = true
                };

                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    StatusMessage = $"Failed to save {tab.Title}: {result.Error}";
                    _dialogService.ShowError(result.Error, "Save Error");
                }
                else
                {
                    tab.IsDirty = false;
                    StatusMessage = $"Saved {tab.Title}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving {tab.Title}: {ex.Message}";
                _dialogService.ShowError(ex.Message, "Save Error");
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
                        var command = new SaveNoteCommand
                        {
                            NoteId = tab.NoteId,
                            Content = tab.Content,
                            IsManualSave = false
                        };

                        var result = await _mediator.Send(command);
                        if (result.Success)
                        {
                            tab.IsDirty = false;
                            savedCount++;
                        }
                        else
                        {
                            failedCount++;
                        }
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

        private async Task ExecuteCloseTab(TabViewModel tab)
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

                OpenTabs.Remove(tab);
                TabClosed?.Invoke(tab);
                
                // Select another tab if this was selected
                if (SelectedTab == tab)
                {
                    SelectedTab = OpenTabs.LastOrDefault();
                }

                StatusMessage = $"Closed {tab.Title}";
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

            OpenTabs.Clear();
            SelectedTab = null;
            StatusMessage = "Closed all tabs";
        }

        private bool CanSaveTab(TabViewModel tab) => !IsLoading && tab?.IsDirty == true;
        private bool CanSaveAllTabs() => !IsLoading && OpenTabs.Any(t => t.IsDirty);
        private bool CanCloseTab(TabViewModel tab) => !IsLoading && tab != null;
        private bool CanCloseAllTabs() => !IsLoading && OpenTabs.Any();
    }

    /// <summary>
    /// Simple tab representation for the new architecture
    /// </summary>
    public class TabViewModel : ViewModelBase
    {
        private string _title;
        private string _content;
        private bool _isDirty;

        public TabViewModel(string noteId, string title, string content)
        {
            NoteId = noteId;
            Title = title;
            Content = content;
            IsDirty = false;
        }

        public string NoteId { get; }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Content
        {
            get => _content;
            set
            {
                if (SetProperty(ref _content, value))
                {
                    IsDirty = true;
                }
            }
        }

        public bool IsDirty
        {
            get => _isDirty;
            set => SetProperty(ref _isDirty, value);
        }
    }
}
