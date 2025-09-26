using System;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Services;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Coordinates simple command execution operations for context menus.
    /// Extracted from MainViewModel to separate simple command concerns.
    /// </summary>
    public class SimpleCommandCoordinator : IDisposable
    {
        private readonly IAppLogger _logger;
        private readonly IDialogService _dialogService;
        private readonly Func<NoteTreeItem, Task> _deleteNoteAsync;
        private readonly Action<NoteTreeItem> _raiseNoteOpenRequested;
        private bool _disposed;

        public SimpleCommandCoordinator(
            IAppLogger logger,
            IDialogService dialogService,
            Func<NoteTreeItem, Task> deleteNoteAsync,
            Action<NoteTreeItem> raiseNoteOpenRequested)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _deleteNoteAsync = deleteNoteAsync ?? throw new ArgumentNullException(nameof(deleteNoteAsync));
            _raiseNoteOpenRequested = raiseNoteOpenRequested ?? throw new ArgumentNullException(nameof(raiseNoteOpenRequested));
        }

        /// <summary>
        /// Executes note deletion with user confirmation
        /// </summary>
        public async Task ExecuteDeleteNoteAsync(NoteTreeItem noteItem)
        {
            if (noteItem == null) return;
            
            try
            {
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    $"Are you sure you want to delete '{noteItem.Title}'?\n\nThis action cannot be undone.",
                    "Confirm Delete");
                
                if (result)
                {
                    await _deleteNoteAsync(noteItem);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to delete note");
                _dialogService?.ShowError($"Failed to delete note: {ex.Message}", "Delete Error");
            }
        }

        /// <summary>
        /// Executes note opening via split-pane system
        /// </summary>
        public void ExecuteOpenNote(NoteTreeItem noteItem)
        {
            if (noteItem == null) return;
            
            try
            {
                _raiseNoteOpenRequested(noteItem);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to open note");
                _dialogService?.ShowError($"Failed to open note: {ex.Message}", "Open Error");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // No specific resources to dispose currently
                _disposed = true;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Error disposing SimpleCommandCoordinator: {ex.Message}");
            }
        }
    }
}
