using System;
using System.Threading.Tasks;
using System.Windows.Input;
using MediatR;
using NoteNest.Application.Notes.Commands.CreateNote;
using NoteNest.Application.Notes.Commands.SaveNote;
using NoteNest.Application.Notes.Commands.DeleteNote;
using NoteNest.Application.Notes.Commands.RenameNote;
using NoteNest.UI.ViewModels.Common;
using NoteNest.UI.Services;

namespace NoteNest.UI.ViewModels.Notes
{
    /// <summary>
    /// Focused ViewModel for note operations - replaces note operation logic from MainViewModel
    /// </summary>
    public class NoteOperationsViewModel : ViewModelBase
    {
        private readonly IMediator _mediator;
        private readonly IDialogService _dialogService;
        private string _selectedCategoryId;
        private bool _isProcessing;
        private string _statusMessage;

        public NoteOperationsViewModel(
            IMediator mediator,
            IDialogService dialogService)
        {
            _mediator = mediator;
            _dialogService = dialogService;
            
            InitializeCommands();
        }

        public string SelectedCategoryId
        {
            get => _selectedCategoryId;
            set => SetProperty(ref _selectedCategoryId, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set => SetProperty(ref _isProcessing, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand CreateNoteCommand { get; private set; }
        public ICommand SaveNoteCommand { get; private set; }
        public ICommand DeleteNoteCommand { get; private set; }
        public ICommand RenameNoteCommand { get; private set; }

        // Events for UI coordination
        public event Action<string> NoteCreated;
        public event Action<string> NoteDeleted;
        public event Action<string, string> NoteRenamed;

        private void InitializeCommands()
        {
            // Commands now accept ViewModel objects from context menu
            CreateNoteCommand = new AsyncRelayCommand<object>(ExecuteCreateNote, CanCreateNote);
            SaveNoteCommand = new AsyncRelayCommand<string>(ExecuteSaveNote, CanSaveNote);
            DeleteNoteCommand = new AsyncRelayCommand<object>(ExecuteDeleteNote, CanDeleteNote);
            RenameNoteCommand = new AsyncRelayCommand<object>(ExecuteRenameNote, CanRenameNote);
        }

        private async Task ExecuteCreateNote(object parameter)
        {
            // Extract CategoryViewModel from parameter
            var category = parameter as NoteNest.UI.ViewModels.Categories.CategoryViewModel;
            var categoryId = category?.Id ?? SelectedCategoryId;
            
            if (string.IsNullOrEmpty(categoryId))
            {
                StatusMessage = "Please select a category first.";
                _dialogService.ShowInfo("Please select a category first.", "No Category Selected");
                return;
            }

            try
            {
                IsProcessing = true;
                StatusMessage = "Creating note...";

                // Show dialog to get note title
                var noteTitle = await _dialogService.ShowInputDialogAsync(
                    "New Note",
                    "Enter note title:",
                    $"Note {DateTime.Now:yyyy-MM-dd HH-mm}",
                    text => string.IsNullOrWhiteSpace(text) ? "Note title cannot be empty." : null);

                if (string.IsNullOrWhiteSpace(noteTitle))
                {
                    StatusMessage = "Note creation cancelled.";
                    return;
                }

                var command = new CreateNoteCommand
                {
                    CategoryId = categoryId,
                    Title = noteTitle,
                    InitialContent = "",
                    OpenInEditor = true
                };

                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    StatusMessage = $"Failed to create note: {result.Error}";
                    _dialogService.ShowError(result.Error, "Create Note Error");
                }
                else
                {
                    StatusMessage = $"Created note: {result.Value.Title}";
                    NoteCreated?.Invoke(result.Value.NoteId);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error creating note: {ex.Message}";
                _dialogService.ShowError(ex.Message, "Error");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task ExecuteSaveNote(string noteId)
        {
            if (string.IsNullOrEmpty(noteId))
                return;

            try
            {
                IsProcessing = true;
                StatusMessage = "Saving note...";

                // TODO: Get actual content from editor
                var command = new SaveNoteCommand
                {
                    NoteId = noteId,
                    Content = "Content from editor", // This would come from the actual editor
                    IsManualSave = true
                };

                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    StatusMessage = $"Failed to save note: {result.Error}";
                }
                else
                {
                    StatusMessage = "Note saved successfully";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving note: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task ExecuteDeleteNote(object parameter)
        {
            // Extract NoteItemViewModel from parameter
            var note = parameter as NoteNest.UI.ViewModels.Categories.NoteItemViewModel;
            var noteId = note?.Id ?? parameter?.ToString();
            
            if (string.IsNullOrEmpty(noteId))
                return;

            try
            {
                var confirmed = await _dialogService.ShowConfirmationDialogAsync(
                    $"Are you sure you want to delete '{note?.Title ?? "this note"}'? This action cannot be undone.",
                    "Confirm Delete");

                if (!confirmed)
                    return;

                IsProcessing = true;
                StatusMessage = "Deleting note...";

                var command = new DeleteNoteCommand
                {
                    NoteId = noteId,
                    DeleteFile = true
                };

                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    StatusMessage = $"Failed to delete note: {result.Error}";
                    _dialogService.ShowError(result.Error, "Delete Note Error");
                }
                else
                {
                    StatusMessage = $"Deleted note: {result.Value.DeletedNoteTitle}";
                    NoteDeleted?.Invoke(noteId);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error deleting note: {ex.Message}";
                _dialogService.ShowError(ex.Message, "Error");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task ExecuteRenameNote(object parameter)
        {
            // Extract NoteItemViewModel from parameter
            var note = parameter as NoteNest.UI.ViewModels.Categories.NoteItemViewModel;
            var noteId = note?.Id ?? parameter?.ToString();
            
            if (string.IsNullOrEmpty(noteId))
                return;

            try
            {
                IsProcessing = true;
                StatusMessage = "Renaming note...";

                // Show dialog to get new title
                var newTitle = await _dialogService.ShowInputDialogAsync(
                    "Rename Note",
                    "Enter new note title:",
                    note?.Title ?? "",
                    text => string.IsNullOrWhiteSpace(text) ? "Note title cannot be empty." : null);

                if (string.IsNullOrWhiteSpace(newTitle) || newTitle == note?.Title)
                {
                    StatusMessage = "Rename cancelled.";
                    return;
                }

                var command = new RenameNoteCommand
                {
                    NoteId = noteId,
                    NewTitle = newTitle,
                    UpdateFilePath = true
                };

                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    StatusMessage = $"Failed to rename note: {result.Error}";
                    _dialogService.ShowError(result.Error, "Rename Note Error");
                }
                else
                {
                    StatusMessage = $"Renamed note to: {result.Value.NewTitle}";
                    NoteRenamed?.Invoke(noteId, result.Value.NewTitle);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error renaming note: {ex.Message}";
                _dialogService.ShowError(ex.Message, "Error");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private bool CanCreateNote(object parameter) 
        {
            var category = parameter as NoteNest.UI.ViewModels.Categories.CategoryViewModel;
            var categoryId = category?.Id ?? SelectedCategoryId;
            return !IsProcessing && !string.IsNullOrEmpty(categoryId);
        }
        
        private bool CanSaveNote(string noteId) => !IsProcessing && !string.IsNullOrEmpty(noteId);
        
        private bool CanDeleteNote(object parameter) 
        {
            var note = parameter as NoteNest.UI.ViewModels.Categories.NoteItemViewModel;
            var noteId = note?.Id ?? parameter?.ToString();
            return !IsProcessing && !string.IsNullOrEmpty(noteId);
        }
        
        private bool CanRenameNote(object parameter)
        {
            var note = parameter as NoteNest.UI.ViewModels.Categories.NoteItemViewModel;
            var noteId = note?.Id ?? parameter?.ToString();
            return !IsProcessing && !string.IsNullOrEmpty(noteId);
        }
    }
}
