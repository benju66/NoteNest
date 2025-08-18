using System;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Services
{
    public class TabCloseService : ITabCloseService
    {
        private readonly INoteOperationsService _noteOperations;
        private readonly IWorkspaceService _workspace;
        private readonly IDialogService _dialog;
        private readonly IAppLogger _logger;
        
        public TabCloseService(
            INoteOperationsService noteOperations,
            IWorkspaceService workspace,
            IDialogService dialog,
            IAppLogger logger)
        {
            _noteOperations = noteOperations ?? throw new ArgumentNullException(nameof(noteOperations));
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task<bool> CloseTabWithPromptAsync(ITabItem tab)
        {
            if (tab == null) return false;
            
            try
            {
                if (tab.IsDirty)
                {
                    var result = await _dialog.ShowYesNoCancelAsync(
                        $"Save changes to '{tab.Title}'?",
                        "Unsaved Changes");
                    
                    if (result == null)
                    {
                        _logger.Debug($"Close cancelled for tab: {tab.Title}");
                        return false;
                    }
                    
                    if (result == true)
                    {
                        if (tab.Note != null && tab.Content != null)
                        {
                            tab.Note.Content = tab.Content;
                        }
                        await _noteOperations.SaveNoteAsync(tab.Note);
                        tab.IsDirty = false;
                        _logger.Info($"Saved and closing tab: {tab.Title}");
                    }
                    else
                    {
                        _logger.Info($"Closing without saving: {tab.Title}");
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
                        if (tab.Note != null && tab.Content != null)
                        {
                            tab.Note.Content = tab.Content;
                        }
                        await _noteOperations.SaveNoteAsync(tab.Note);
                        tab.IsDirty = false;
                    }
                }
            }
            
            await _workspace.CloseAllTabsAsync();
            return true;
        }
    }
}


