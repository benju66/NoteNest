using System;
using System.Threading.Tasks;
using System.Windows.Input;
using MediatR;
using NoteNest.Application.Notes.Commands.CreateNote;
using NoteNest.UI.ViewModels.Common;
using NoteNest.UI.ViewModels.Categories;
using NoteNest.UI.Services;

namespace NoteNest.UI.ViewModels.Shell
{
    public class MainShellViewModel : ViewModelBase
    {
        private readonly IMediator _mediator;
        private readonly IDialogService _dialogService;
        private CategoryViewModel _selectedCategory;
        private bool _isLoading;
        private string _statusMessage;

        public MainShellViewModel(
            IMediator mediator,
            IDialogService dialogService,
            CategoryTreeViewModel categoryTree)
        {
            _mediator = mediator;
            _dialogService = dialogService;
            
            CategoryTree = categoryTree;
            
            InitializeCommands();
            SubscribeToEvents();
        }

        public CategoryTreeViewModel CategoryTree { get; }

        public CategoryViewModel SelectedCategory
        {
            get => _selectedCategory;
            set => SetProperty(ref _selectedCategory, value);
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

        public ICommand CreateNoteCommand { get; private set; }
        public ICommand SaveNoteCommand { get; private set; }
        public ICommand SaveAllCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }

        private void InitializeCommands()
        {
            CreateNoteCommand = new AsyncRelayCommand(ExecuteCreateNote, CanCreateNote);
            SaveNoteCommand = new AsyncRelayCommand(ExecuteSaveNote, CanSaveNote);
            SaveAllCommand = new AsyncRelayCommand(ExecuteSaveAll);
            RefreshCommand = new AsyncRelayCommand(ExecuteRefresh);
        }

        private async Task ExecuteCreateNote()
        {
            if (SelectedCategory == null)
            {
                StatusMessage = "Please select a category first.";
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = "Creating note...";

                var command = new CreateNoteCommand
                {
                    CategoryId = SelectedCategory.Id,
                    Title = $"New Note {DateTime.Now:yyyy-MM-dd HH-mm-ss}",
                    InitialContent = "",
                    OpenInEditor = true
                };

                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    StatusMessage = $"Failed to create note: {result.Error}";
                }
                else
                {
                    StatusMessage = $"Created note: {result.Value.Title}";
                    // TODO: Open note in editor and trigger rename
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error creating note: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private bool CanCreateNote() => SelectedCategory != null && !IsLoading;

        private async Task ExecuteSaveNote()
        {
            // TODO: Implement save current note
            StatusMessage = "Save note not yet implemented";
            await Task.CompletedTask;
        }

        private bool CanSaveNote()
        {
            // TODO: Check if there's a selected note that needs saving
            return false;
        }

        private async Task ExecuteSaveAll()
        {
            // TODO: Implement save all notes
            StatusMessage = "Save all not yet implemented";
            await Task.CompletedTask;
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
            CategoryTree.CategorySelected += OnCategorySelected;
        }

        private void OnCategorySelected(CategoryViewModel category)
        {
            SelectedCategory = category;
            (CreateNoteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }
}
