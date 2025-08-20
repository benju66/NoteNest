using System;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Services;

namespace NoteNest.UI.Services
{
    public class TabCloseService : ITabCloseService
    {
        private readonly INoteOperationsService _noteOperations;
        private readonly IWorkspaceService _workspace;
        private readonly IDialogService _dialog;
        private readonly IAppLogger _logger;
        private readonly IWorkspaceStateService _workspaceState;
        
        public TabCloseService(
            INoteOperationsService noteOperations,
            IWorkspaceService workspace,
            IDialogService dialog,
            IAppLogger logger,
            IWorkspaceStateService workspaceState)
        {
            _noteOperations = noteOperations ?? throw new ArgumentNullException(nameof(noteOperations));
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _workspaceState = workspaceState ?? throw new ArgumentNullException(nameof(workspaceState));
        }
        
        public async Task<bool> CloseTabWithPromptAsync(ITabItem tab)
        {
            if (tab == null) return false;
            
            try
            {
                if (tab.IsDirty)
                {
                    try
                    {
                        // Auto-save using WorkspaceStateService
                        System.Diagnostics.Debug.WriteLine($"[Close] Attempting auto-save for tab id={tab?.Note?.Id} title={tab?.Title}");
                        _workspaceState.UpdateNoteContent(tab.Note.Id, tab.Content ?? string.Empty);
                        var result = await _workspaceState.SaveNoteAsync(tab.Note.Id);
                        System.Diagnostics.Debug.WriteLine($"[Close] Auto-save result success={result?.Success} noteId={result?.NoteId}");
                        if (result?.Success == true)
                        {
                            _logger.Info($"Auto-saved on close: {tab.Title}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Auto-save on close failed: {tab.Title}");
                        System.Diagnostics.Debug.WriteLine($"[Close][ERROR] Auto-save failed for {tab?.Title}: {ex.Message}");
                        // Fallback to prompt only if auto-save fails
                        await PromptAndMaybeSaveAsync(tab);
                    }
                }
                
                await _workspace.CloseTabAsync(tab);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error closing tab: {tab.Title}");
                _dialog.ShowError($"Error closing tab: {ex.Message}", "Close Error");
                return false;
            }
        }

        private async Task PromptAndMaybeSaveAsync(ITabItem tab)
        {
            var result = await _dialog.ShowYesNoCancelAsync(
                $"Save changes to '{tab.Title}'?",
                "Unsaved Changes");
            
            if (result == null)
            {
                _logger.Debug($"Close cancelled for tab: {tab.Title}");
                throw new OperationCanceledException();
            }
            
            if (result == true)
            {
                System.Diagnostics.Debug.WriteLine($"[Close] User chose Save for tab id={tab?.Note?.Id} title={tab?.Title}");
                _workspaceState.UpdateNoteContent(tab.Note.Id, tab.Content ?? string.Empty);
                var save = await _workspaceState.SaveNoteAsync(tab.Note.Id);
                System.Diagnostics.Debug.WriteLine($"[Close] Prompt save result success={save?.Success} noteId={save?.NoteId}");
                _logger.Info($"Saved and closing tab: {tab.Title}");
            }
            else
            {
                _logger.Info($"Closing without saving: {tab.Title}");
            }
        }
        
        public async Task<bool> CloseAllTabsWithPromptAsync()
        {
            var dirtyTabs = _workspace.OpenTabs.Where(t => t.IsDirty).ToList();
            
            if (dirtyTabs.Any())
            {
                var result = await _dialog.ShowYesNoCancelAsync(
                    $"You have {dirtyTabs.Count} unsaved note(s). Save all before closing?",
                    "Unsaved Changes");
                
                if (result == null) return false;
                
                if (result == true)
                {
                    foreach (var tab in dirtyTabs)
                    {
                        _workspaceState.UpdateNoteContent(tab.Note.Id, tab.Content ?? string.Empty);
                        await _workspaceState.SaveNoteAsync(tab.Note.Id);
                    }
                }
            }
            
            await _workspace.CloseAllTabsAsync();
            return true;
        }
    }
}


