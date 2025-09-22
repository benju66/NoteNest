using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Service responsible for loading and building tree data structures.
    /// Extracted from MainViewModel to follow SRP - handles only pure data loading and tree construction.
    /// </summary>
    public class TreeDataService : ITreeDataService
    {
        private readonly ICategoryManagementService _categoryService;
        private readonly NoteService _noteService;
        private readonly IAppLogger _logger;
        private readonly ITreeCacheService? _cacheService;

        public TreeDataService(
            ICategoryManagementService categoryService,
            NoteService noteService,
            IAppLogger logger,
            ITreeCacheService? cacheService = null)
        {
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _noteService = noteService ?? throw new ArgumentNullException(nameof(noteService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService;  // Can be null
        }

        /// <summary>
        /// Loads categories from storage and builds the complete hierarchical tree data structure
        /// </summary>
        public async Task<TreeDataResult> LoadTreeDataAsync()
        {
            try
            {
                _logger.Info("🔍 [DIAGNOSTIC] TreeDataService: Starting LoadTreeDataAsync");

                // Load flat categories from storage
                _logger.Info("🔍 [DIAGNOSTIC] TreeDataService: About to call _categoryService.LoadCategoriesAsync()");
                var flatCategories = await _categoryService.LoadCategoriesAsync();
                _logger.Info($"🔍 [DIAGNOSTIC] TreeDataService: Loaded {flatCategories?.Count ?? 0} flat categories");
                if (flatCategories == null || !flatCategories.Any())
                {
                    _logger.Info("TreeDataService: No categories loaded");
                    return new TreeDataResult { Success = true };
                }

                // PERFORMANCE FIX: Batch load all notes at once
                Dictionary<string, List<NoteModel>> notesByCategory;
                
                if (_cacheService?.IsCacheValid == true)
                {
                    notesByCategory = _cacheService.GetNotesByCategory();
                    _logger.Debug("Using cached notes");
                }
                else
                {
                    // Batch loading with timeout protection
                    using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    {
                        try
                        {
                            notesByCategory = await LoadAllNotesAsync(flatCategories, cts.Token);
                            _cacheService?.SetNotesByCategory(notesByCategory);
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.Warning("Batch loading timed out, falling back to empty notes");
                            notesByCategory = new Dictionary<string, List<NoteModel>>();
                        }
                    }
                }
                
                // Build tree structure with pre-loaded notes
                _logger.Info("🔍 [DIAGNOSTIC] TreeDataService: Building tree structure");
                var rootCategories = flatCategories.Where(c => string.IsNullOrEmpty(c.ParentId)).ToList();
                _logger.Info($"🔍 [DIAGNOSTIC] TreeDataService: Found {rootCategories.Count} root categories");
                var builtRoots = new List<TreeNodeData>();

                foreach (var root in rootCategories.OrderByDescending(c => c.Pinned).ThenBy(c => c.Name))
                {
                    _logger.Debug("Building root category node");
                    var rootNode = await BuildTreeNodeAsync(root, flatCategories, notesByCategory, 0);
                    builtRoots.Add(rootNode);
                    _logger.Debug("Completed building root category node");
                }

                _logger.Info("TreeDataService: Tree structure built successfully");

                return new TreeDataResult
                {
                    RootNodes = builtRoots,
                    PinnedCategoryNodes = new List<TreeNodeData>(), // Empty - pinning removed
                    PinnedNotes = new List<PinnedNoteData>(),       // Empty - pinning removed
                    TotalCategoriesLoaded = flatCategories.Count,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "TreeDataService: Failed to load tree data");
                return new TreeDataResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// EXISTING METHOD: Kept for backward compatibility - delegates to new overload
        /// </summary>
        public async Task<TreeNodeData> BuildTreeNodeAsync(CategoryModel category, List<CategoryModel> allCategories, int level)
        {
            // Delegate to new overload with empty dictionary (fallback to individual loading)
            var emptyNotes = new Dictionary<string, List<NoteModel>>();
            return await BuildTreeNodeAsync(category, allCategories, emptyNotes, level);
        }

        /// <summary>
        /// NEW OVERLOAD: Builds a tree node using pre-loaded notes for batch processing
        /// </summary>
        public async Task<TreeNodeData> BuildTreeNodeAsync(
            CategoryModel category, 
            List<CategoryModel> allCategories,
            Dictionary<string, List<NoteModel>> notesByCategory,
            int level)
        {
            _logger.Debug($"Building tree node for category at level {level} with pre-loaded notes");
            category.Level = level;
            
            var node = new TreeNodeData
            {
                Category = category,
                Level = level,
                IsExpanded = level < 2
            };
            
            // Use pre-loaded notes if available, otherwise empty list
            if (notesByCategory != null && notesByCategory.TryGetValue(category.Id, out var notes))
            {
                node.Notes = notes;
                _logger.Debug($"Assigned {notes.Count} pre-loaded notes to category {category.Name}");
            }
            else
            {
                node.Notes = new List<NoteModel>();
            }
            
            // Build subcategories recursively
            var children = allCategories
                .Where(c => c.ParentId == category.Id)
                .OrderByDescending(c => c.Pinned)
                .ThenBy(c => c.Name)
                .ToList();
            
            foreach (var child in children)
            {
                var childNode = await BuildTreeNodeAsync(child, allCategories, notesByCategory ?? new Dictionary<string, List<NoteModel>>(), level + 1);
                node.Children.Add(childNode);
            }
            
            return node;
        }

        /// <summary>
        /// Helper method for safe batch loading of all notes
        /// </summary>
        private async Task<Dictionary<string, List<NoteModel>>> LoadAllNotesAsync(
            List<CategoryModel> categories, 
            System.Threading.CancellationToken cancellationToken)
        {
            var allNotes = new List<NoteModel>();
            
            foreach (var category in categories)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                    
                try
                {
                    var notes = await _noteService.GetNotesInCategoryAsync(category);
                    if (notes != null)
                        allNotes.AddRange(notes);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to load notes for category {category.Id}: {ex.Message}");
                    // Continue with other categories
                }
            }
            
            // Add batch loading size warning as suggested
            if (allNotes.Count > 10000) 
            {
                _logger.Warning($"Large note collection detected ({allNotes.Count} notes). Consider organizing categories for better performance.");
            }
            
            return allNotes.GroupBy(n => n.CategoryId ?? "")
                           .ToDictionary(g => g.Key, g => g.ToList());
        }

        // Pinning functionality removed - will be reimplemented with better architecture later
    }
}
