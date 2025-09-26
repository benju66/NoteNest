using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Coordinates tab persistence and restoration operations.
    /// Extracted from MainViewModel to separate tab lifecycle concerns.
    /// </summary>
    public class TabPersistenceCoordinator : IDisposable
    {
        private readonly ITabPersistenceService _tabPersistence;
        private readonly ISaveManager _saveManager;
        private readonly IWorkspaceService _workspaceService;
        private readonly IAppLogger _logger;
        private readonly Func<bool> _isRecoveryInProgress;
        private readonly Func<HashSet<string>> _getPendingRecoveryNotes;
        private readonly Action<string> _removePendingRecoveryNote;
        private readonly Action _clearRecoveryInProgress;
        private bool _disposed;

        public TabPersistenceCoordinator(
            ITabPersistenceService tabPersistence,
            ISaveManager saveManager,
            IWorkspaceService workspaceService,
            IAppLogger logger,
            Func<bool> isRecoveryInProgress,
            Func<HashSet<string>> getPendingRecoveryNotes,
            Action<string> removePendingRecoveryNote,
            Action clearRecoveryInProgress)
        {
            _tabPersistence = tabPersistence ?? throw new ArgumentNullException(nameof(tabPersistence));
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _isRecoveryInProgress = isRecoveryInProgress ?? throw new ArgumentNullException(nameof(isRecoveryInProgress));
            _getPendingRecoveryNotes = getPendingRecoveryNotes ?? throw new ArgumentNullException(nameof(getPendingRecoveryNotes));
            _removePendingRecoveryNote = removePendingRecoveryNote ?? throw new ArgumentNullException(nameof(removePendingRecoveryNote));
            _clearRecoveryInProgress = clearRecoveryInProgress ?? throw new ArgumentNullException(nameof(clearRecoveryInProgress));

            // Subscribe to workspace events for persistence tracking
            _workspaceService.TabOpened += OnWorkspaceTabOpened;
            _workspaceService.TabClosed += OnWorkspaceTabClosed;
            _workspaceService.TabSelectionChanged += OnWorkspaceTabSelectionChangedForPersistence;
        }

        /// <summary>
        /// Restores tabs from persisted state during application startup
        /// </summary>
        public async Task RestoreTabsAsync()
        {
            var persistedState = await _tabPersistence.LoadAsync();
            if (persistedState?.Tabs == null) return;
            
            foreach (var tabInfo in persistedState.Tabs)
            {
                try
                {
                    if (!File.Exists(tabInfo.Path))
                    {
                        _logger.Warning($"Tab file no longer exists: {tabInfo.Path}");
                        continue;
                    }
                    
                    // Open the note
                    var noteId = await _saveManager.OpenNoteAsync(tabInfo.Path);
                    
                    // If tab was dirty and we have dirty content, restore it
                    if (tabInfo.IsDirty && !string.IsNullOrEmpty(tabInfo.DirtyContent))
                    {
                        // Verify the file hasn't changed since persistence
                        bool canRestoreDirty = false;
                        
                        if (!string.IsNullOrEmpty(tabInfo.FileContentHash))
                        {
                            try
                            {
                                var currentFileContent = await File.ReadAllTextAsync(tabInfo.Path);
                                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                                {
                                    var currentHash = Convert.ToBase64String(
                                        sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(currentFileContent))
                                    );
                                    
                                    if (currentHash == tabInfo.FileContentHash)
                                    {
                                        canRestoreDirty = true;
                                    }
                                    else
                                    {
                                        _logger.Warning($"File changed since last session, not restoring dirty content: {tabInfo.Path}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, $"Failed to verify file content for: {tabInfo.Path}");
                            }
                        }
                        
                        if (canRestoreDirty)
                        {
                            // Restore dirty content
                            _saveManager.UpdateContent(noteId, tabInfo.DirtyContent);
                            _logger.Info($"Restored dirty content for: {tabInfo.Path}");
                        }
                    }
                    
                    // Create tab
                    var note = new NoteModel
                    {
                        Id = noteId,
                        FilePath = tabInfo.Path,
                        Title = tabInfo.Title,
                        Content = _saveManager.GetContent(noteId)
                    };
                    
                    var tab = await _workspaceService.OpenNoteAsync(note);
                    
                    // Set active if needed
                    if (tabInfo.Id == persistedState.ActiveTabId)
                    {
                        _workspaceService.SelectedTab = tab;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to restore tab: {tabInfo.Path}");
                }
            }
        }

        #region Workspace Event Handlers

        private async void OnWorkspaceTabOpened(object sender, TabEventArgs e)
        {
            try 
            { 
                _tabPersistence.MarkChanged(); 
            } 
            catch { }
            
            // Check if this tab has recovered content that needs to be saved
            if (_isRecoveryInProgress() && e?.Tab?.Note != null && _getPendingRecoveryNotes().Contains(e.Tab.Note.Id))
            {
                await HandleRecoveredTabAsync(e);
            }
        }

        private void OnWorkspaceTabClosed(object sender, TabEventArgs e)
        {
            try 
            { 
                _tabPersistence.MarkChanged(); 
            } 
            catch { }
        }

        private void OnWorkspaceTabSelectionChangedForPersistence(object sender, TabChangedEventArgs e)
        {
            try 
            { 
                _tabPersistence.MarkChanged(); 
            } 
            catch { }
        }

        #endregion

        #region Recovery Integration

        private async Task HandleRecoveredTabAsync(TabEventArgs e)
        {
            try
            {
                _logger.Info($"Saving recovered content for opened tab: {e.Tab.Note.Title}");
                
                // Wait a moment for the tab to fully initialize
                await Task.Delay(500);
                
                // Force save the recovered content (bypass dirty check for recovery)
                // First ensure the tab knows it has dirty content
                if (e.Tab is NoteTabItem nti)
                {
                    nti.IsDirty = true;
                }
                
                bool success = false;
                if (e.Tab is ITabItem tabItem)
                {
                    success = await _saveManager.SaveNoteAsync(tabItem.NoteId);
                }
                
                if (success)
                {
                    _removePendingRecoveryNote(e.Tab.Note.Id);
                    _logger.Info($"Successfully saved recovered content for: {e.Tab.Note.Title}");
                    
                    // Clear recovery tracking if all notes are saved
                    if (_getPendingRecoveryNotes().Count == 0)
                    {
                        _clearRecoveryInProgress();
                        
                        // Recovery cleanup removed - StartupRecoveryService handles all recovery
                    }
                }
                else
                {
                    _logger.Warning($"Failed to save recovered content for: {e.Tab.Note.Title}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error saving recovered content for tab: {e.Tab?.Note?.Title}");
            }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Unsubscribe from workspace events
                if (_workspaceService != null)
                {
                    _workspaceService.TabOpened -= OnWorkspaceTabOpened;
                    _workspaceService.TabClosed -= OnWorkspaceTabClosed;
                    _workspaceService.TabSelectionChanged -= OnWorkspaceTabSelectionChangedForPersistence;
                }
                
                _disposed = true;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Error disposing TabPersistenceCoordinator: {ex.Message}");
            }
        }
    }
}
