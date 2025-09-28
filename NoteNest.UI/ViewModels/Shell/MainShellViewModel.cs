using System;
using System.Threading.Tasks;
using System.Windows.Input;
using MediatR;
using NoteNest.Application.Notes.Commands.CreateNote;
using NoteNest.UI.ViewModels.Common;
using NoteNest.UI.ViewModels.Categories;
using NoteNest.UI.ViewModels.Notes;
using NoteNest.UI.ViewModels.Workspace;
using NoteNest.UI.Services;
using System.Linq;

namespace NoteNest.UI.ViewModels.Shell
{
    public class MainShellViewModel : ViewModelBase
    {
        private readonly IMediator _mediator;
        private readonly IDialogService _dialogService;
        private bool _isLoading;
        private string _statusMessage;

        public MainShellViewModel(
            IMediator mediator,
            IDialogService dialogService,
            CategoryTreeViewModel categoryTree,
            NoteOperationsViewModel noteOperations,
            CategoryOperationsViewModel categoryOperations,
            ModernWorkspaceViewModel workspace)
        {
            _mediator = mediator;
            _dialogService = dialogService;
            
            // Composed ViewModels - each with single responsibility
            CategoryTree = categoryTree;
            NoteOperations = noteOperations;
            CategoryOperations = categoryOperations;
            Workspace = workspace;
            
            InitializeCommands();
            SubscribeToEvents();
        }

        // Focused ViewModels - Clean Architecture approach
        public CategoryTreeViewModel CategoryTree { get; }
        public NoteOperationsViewModel NoteOperations { get; }
        public CategoryOperationsViewModel CategoryOperations { get; }
        public ModernWorkspaceViewModel Workspace { get; }

        // Expose selected category from CategoryTree
        public CategoryViewModel SelectedCategory => CategoryTree.SelectedCategory;

        // Additional aggregated properties for convenience
        public bool HasOpenTabs => Workspace.OpenTabs.Any();

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

        // Commands are now delegated to focused ViewModels
        public ICommand CreateNoteCommand => NoteOperations.CreateNoteCommand;
        public ICommand SaveNoteCommand => Workspace.SaveTabCommand;
        public ICommand SaveAllCommand => Workspace.SaveAllTabsCommand;
        public ICommand RefreshCommand { get; private set; }

        private void InitializeCommands()
        {
            RefreshCommand = new AsyncRelayCommand(ExecuteRefresh);
        }

        private async Task ExecuteRefresh()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Refreshing...";
                
                await CategoryTree.RefreshAsync();
                
                StatusMessage = "Refreshed";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Refresh failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void SubscribeToEvents()
        {
            // Wire up cross-ViewModel communication
            CategoryTree.CategorySelected += OnCategorySelected;
            CategoryTree.NoteSelected += OnNoteSelected;
            CategoryTree.NoteOpenRequested += OnNoteOpenRequested;
            
            NoteOperations.NoteCreated += OnNoteCreated;
            NoteOperations.NoteDeleted += OnNoteDeleted;
            
            Workspace.TabSelected += OnTabSelected;
            Workspace.NoteOpened += OnNoteOpened;
        }

        private void OnCategorySelected(CategoryViewModel category)
        {
            // Update NoteOperations with selected category
            NoteOperations.SelectedCategoryId = category?.Id;
            
            // Update status
            StatusMessage = category != null ? $"Selected: {category.Name}" : "No category selected";
        }

        private async void OnNoteCreated(string noteId)
        {
            // Open the newly created note in the workspace
            var createdNote = CategoryTree.SelectedCategory; // This would need proper note lookup
            if (createdNote != null)
            {
                await Workspace.OpenNoteAsync(noteId, "New Note"); // Title would come from creation result
                StatusMessage = "Note created and opened";
            }
        }

        private void OnNoteDeleted(string noteId)
        {
            // Close tab if note was open
            var openTab = Workspace.OpenTabs.FirstOrDefault(t => t.NoteId == noteId);
            if (openTab != null)
            {
                Workspace.OpenTabs.Remove(openTab);
            }
            StatusMessage = "Note deleted";
        }

        private void OnTabSelected(TabViewModel tab)
        {
            StatusMessage = tab != null ? $"Editing: {tab.Title}" : "No note selected";
        }

        private void OnNoteOpened(string noteId)
        {
            StatusMessage = "Note opened in editor";
        }

        // =============================================================================
        // NEW NOTE INTERACTION HANDLERS - Clean Architecture Event Orchestration
        // =============================================================================

        private void OnNoteSelected(NoteItemViewModel note)
        {
            if (note != null)
            {
                StatusMessage = $"Selected note: {note.Title}";
                // Could add preview pane logic here in the future
            }
        }

        private async void OnNoteOpenRequested(NoteItemViewModel note)
        {
            if (note == null) return;

            try
            {
                IsLoading = true;
                StatusMessage = $"Opening {note.Title}...";

                // Open note in workspace using ModernWorkspaceViewModel
                await Workspace.OpenNoteAsync(note.Id, note.Title);

                StatusMessage = $"Opened {note.Title}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to open {note.Title}: {ex.Message}";
                _dialogService.ShowError($"Failed to open note: {ex.Message}", "Open Error");
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
