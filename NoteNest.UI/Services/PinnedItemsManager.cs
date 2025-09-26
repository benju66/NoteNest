using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Manages pinned items functionality including pinned categories and notes.
    /// Extracted from MainViewModel to separate pinning concerns.
    /// </summary>
    public class PinnedItemsManager : IDisposable
    {
        private readonly IAppLogger _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly Func<ObservableCollection<CategoryTreeItem>> _getCategories;
        private readonly Func<IPinService> _getPinService;
        private readonly Action<string> _notifyPropertyChanged;
        private bool _disposed;

        public PinnedItemsManager(
            IAppLogger logger,
            IServiceProvider serviceProvider,
            Func<ObservableCollection<CategoryTreeItem>> getCategories,
            Func<IPinService> getPinService,
            Action<string> notifyPropertyChanged)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _getCategories = getCategories ?? throw new ArgumentNullException(nameof(getCategories));
            _getPinService = getPinService ?? throw new ArgumentNullException(nameof(getPinService));
            _notifyPropertyChanged = notifyPropertyChanged ?? throw new ArgumentNullException(nameof(notifyPropertyChanged));
        }

        /// <summary>
        /// Refreshes both pinned categories and pinned notes collections
        /// </summary>
        public async Task RefreshPinnedItemsAsync(
            ObservableCollection<CategoryTreeItem> pinnedCategories,
            ObservableCollection<PinnedNoteItem> pinnedNotes)
        {
            try
            {
                _logger?.Debug("RefreshPinnedItemsAsync started");
                var originalPinnedNotesCount = pinnedNotes.Count;
                var originalPinnedCategoriesCount = pinnedCategories.Count;
                
                pinnedNotes.Clear();
                pinnedCategories.Clear();
                
                var pinnedNotesFound = 0;
                var pinnedCategoriesFound = 0;
                
                // Get pin service
                var pinService = _getPinService();
                if (pinService == null)
                {
                    _logger?.Warning("Pin service not available for refresh");
                    return;
                }
                
                // Get all pinned note IDs from the service
                var pinnedNoteIds = await pinService.GetPinnedNoteIdsAsync();
                var pinnedSet = new HashSet<string>(pinnedNoteIds, StringComparer.OrdinalIgnoreCase);
                
                var categories = _getCategories();
                foreach (var category in categories)
                {
                    // Categories still use existing Model.Pinned
                    if (category.Model.Pinned)
                    {
                        pinnedCategories.Add(category);
                        pinnedCategoriesFound++;
                        _logger?.Debug($"Found pinned category: {category.Name}");
                    }
                        
                    // Notes now use pin service
                    await CollectPinnedNotesFromCategoryAsync(category, pinnedSet, pinnedNotes);
                }
                
                pinnedNotesFound = pinnedNotes.Count;
                _logger?.Debug($"RefreshPinnedItemsAsync completed: Found {pinnedNotesFound} pinned notes, {pinnedCategoriesFound} pinned categories");
                _logger?.Debug($"Previous counts: {originalPinnedNotesCount} pinned notes, {originalPinnedCategoriesCount} pinned categories");
                
                _notifyPropertyChanged(nameof(MainViewModel.HasPinnedItems));
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to refresh pinned items");
            }
        }

        /// <summary>
        /// Recursively collects pinned notes from a category and its subcategories
        /// </summary>
        private async Task CollectPinnedNotesFromCategoryAsync(
            CategoryTreeItem category, 
            HashSet<string> pinnedNoteIds,
            ObservableCollection<PinnedNoteItem> pinnedNotes)
        {
            var pinService = _getPinService();
            
            foreach (var note in category.Notes)
            {
                bool isPinned = false;
                
                // First try direct ID match
                if (!string.IsNullOrEmpty(note.Model.Id) && pinnedNoteIds.Contains(note.Model.Id))
                {
                    isPinned = true;
                    _logger?.Debug($"Found pinned note by ID: {note.Title} (ID: {note.Model.Id})");
                }
                // Fallback: check by file path if ID doesn't match
                else if (!string.IsNullOrEmpty(note.Model.FilePath) && pinService != null)
                {
                    try
                    {
                        // Get the correct metadata ID for this file path
                        var metadataManager = _serviceProvider?.GetService<NoteNest.Core.Services.NoteMetadataManager>();
                        if (metadataManager != null)
                        {
                            var tempNote = new NoteNest.Core.Models.NoteModel
                            {
                                FilePath = note.Model.FilePath,
                                Id = note.Model.Id
                            };
                            var metadataId = await metadataManager.GetOrCreateNoteIdAsync(tempNote);
                            
                            if (pinnedNoteIds.Contains(metadataId))
                            {
                                isPinned = true;
                                _logger?.Debug($"Found pinned note by metadata ID: {note.Title} (Tree ID: {note.Model.Id}, Metadata ID: {metadataId})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Debug($"Failed to check metadata ID for {note.Title}: {ex.Message}");
                    }
                }
                
                if (isPinned)
                {
                    pinnedNotes.Add(new NoteNest.UI.Services.PinnedNoteItem(note, category.Name));
                }
                else
                {
                    _logger?.Debug($"Note not pinned: {note.Title} (ID: {note.Model.Id})");
                }
            }
            
            foreach (var sub in category.SubCategories)
            {
                // DON'T add subcategories here - they're already handled in the main loop
                // Just recurse to get their notes
                await CollectPinnedNotesFromCategoryAsync(sub, pinnedNoteIds, pinnedNotes);
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
                _logger?.Warning($"Error disposing PinnedItemsManager: {ex.Message}");
            }
        }
    }
}
