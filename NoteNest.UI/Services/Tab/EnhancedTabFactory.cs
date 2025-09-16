using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.UI.Services.Tab;

namespace NoteNest.UI.Services.Tab
{
    /// <summary>
    /// PHASE 1B: Enhanced tab factory using dependency injection
    /// Maintains backward compatibility while enabling future testability
    /// </summary>
    public class EnhancedTabFactory : ITabFactory, IDisposable
    {
        private readonly ISaveManager _saveManager;
        private readonly ISupervisedTaskRunner _taskRunner;
        private readonly ITabUIBuilderFactory _uiBuilderFactory;
        private readonly ConcurrentDictionary<string, WeakReference> _tabCache = new();
        
        // For backward compatibility, provide default factory
        public EnhancedTabFactory(
            ISaveManager saveManager, 
            ISupervisedTaskRunner taskRunner = null,
            ITabUIBuilderFactory uiBuilderFactory = null)
        {
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            _taskRunner = taskRunner; // Allow null
            _uiBuilderFactory = uiBuilderFactory ?? new DefaultTabUIBuilderFactory();
        }

        public ITabItem CreateTab(NoteModel note, string noteId)
        {
            return CreateTabAsync(note, noteId).GetAwaiter().GetResult();
        }

        public async Task<ITabItem> CreateTabAsync(NoteModel note, string noteId)
        {
            System.Diagnostics.Debug.WriteLine($"[EnhancedTabFactory] Creating tab: noteId={noteId}, note.Id={note.Id}");
            
            // Ensure note has the correct ID
            note.Id = noteId;
            
            // Check cache for existing tab
            if (_tabCache.TryGetValue(noteId, out var weakRef))
            {
                if (weakRef.IsAlive && weakRef.Target is EnhancedNoteTabItem existingTab)
                {
                    await existingTab.EnsureInitializedAsync();
                    System.Diagnostics.Debug.WriteLine($"[EnhancedTabFactory] Reused existing tab for {note.Title}");
                    return existingTab;
                }
                else
                {
                    // Clean up dead reference
                    _tabCache.TryRemove(noteId, out _);
                    System.Diagnostics.Debug.WriteLine($"[EnhancedTabFactory] Cleaned up dead tab reference for {note.Title}");
                }
            }
            
            // Create new tab with dependency injection
            var newTab = await CreateNewTabAsync(note, noteId);
            _tabCache[noteId] = new WeakReference(newTab);
            
            System.Diagnostics.Debug.WriteLine($"[EnhancedTabFactory] Created enhanced tab for {note.Title}");
            return newTab;
        }
        
        private async Task<EnhancedNoteTabItem> CreateNewTabAsync(NoteModel note, string noteId)
        {
            // Create components using dependency injection
            var uiBuilder = _uiBuilderFactory.CreateUIBuilder(note);
            var saveCoordinator = new DefaultTabSaveCoordinator();
            var eventManager = new DefaultTabEventManager();
            var stateManager = new DefaultTabStateManager();
            
            // Create the enhanced tab item
            var tab = new EnhancedNoteTabItem(
                note,
                uiBuilder,
                saveCoordinator,
                eventManager,
                stateManager,
                _saveManager,
                _taskRunner);
            
            // Initialize asynchronously
            await tab.InitializeAsync();
            
            return tab;
        }

        public void RemoveTab(string noteId)
        {
            if (_tabCache.TryRemove(noteId, out var weakRef))
            {
                if (weakRef.IsAlive && weakRef.Target is IDisposable disposableTab)
                {
                    disposableTab.Dispose();
                    System.Diagnostics.Debug.WriteLine($"[EnhancedTabFactory] Disposed and removed tab: {noteId}");
                }
            }
        }

        public void Dispose()
        {
            // Clean up all cached tabs
            foreach (var kvp in _tabCache.ToArray())
            {
                if (kvp.Value.IsAlive && kvp.Value.Target is IDisposable tab)
                {
                    tab.Dispose();
                }
            }
            _tabCache.Clear();
            System.Diagnostics.Debug.WriteLine("[EnhancedTabFactory] Disposed all cached tabs");
        }
    }

    /// <summary>
    /// Factory for creating UI builders
    /// Enables different UI strategies in the future
    /// </summary>
    public interface ITabUIBuilderFactory
    {
        ITabUIBuilder CreateUIBuilder(NoteModel note);
    }

    /// <summary>
    /// Default implementation that creates the current RTF UI
    /// </summary>
    public class DefaultTabUIBuilderFactory : ITabUIBuilderFactory
    {
        public ITabUIBuilder CreateUIBuilder(NoteModel note)
        {
            return new DefaultTabUIBuilder(note);
        }
    }
}
