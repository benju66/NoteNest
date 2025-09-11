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

        public TreeDataService(
            ICategoryManagementService categoryService,
            NoteService noteService,
            IAppLogger logger)
        {
            _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
            _noteService = noteService ?? throw new ArgumentNullException(nameof(noteService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                if (!flatCategories.Any())
                {
                    _logger.Info("TreeDataService: No categories loaded");
                    return new TreeDataResult { Success = true };
                }

                // Build tree structure
                _logger.Info("🔍 [DIAGNOSTIC] TreeDataService: Building tree structure");
                var rootCategories = flatCategories.Where(c => string.IsNullOrEmpty(c.ParentId)).ToList();
                _logger.Info($"🔍 [DIAGNOSTIC] TreeDataService: Found {rootCategories.Count} root categories");
                var builtRoots = new List<TreeNodeData>();

                foreach (var root in rootCategories.OrderByDescending(c => c.Pinned).ThenBy(c => c.Name))
                {
            _logger.Debug("Building root category node");
                    var rootNode = await BuildTreeNodeAsync(root, flatCategories, 0);
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
        /// Builds a tree node and recursively builds its children
        /// </summary>
        public async Task<TreeNodeData> BuildTreeNodeAsync(CategoryModel category, List<CategoryModel> allCategories, int level)
        {
            _logger.Debug($"Building tree node for category at level {level}");
            category.Level = level;
            
            var node = new TreeNodeData
            {
                Category = category,
                Level = level,
                IsExpanded = level < 2  // Default expansion logic
            };

            // Load notes for this category
            _logger.Debug("Loading notes for category");
            var notes = await _noteService.GetNotesInCategoryAsync(category);
            node.Notes = notes.ToList();
            _logger.Debug($"Loaded {notes?.Count() ?? 0} notes for category");

            // Build subcategories (pinned first, then by name for consistency with roots)
            var children = allCategories
                .Where(c => c.ParentId == category.Id)
                .OrderByDescending(c => c.Pinned)
                .ThenBy(c => c.Name)
                .ToList();

            foreach (var child in children)
            {
                var childNode = await BuildTreeNodeAsync(child, allCategories, level + 1);
                node.Children.Add(childNode);
            }

            return node;
        }

        // Pinning functionality removed - will be reimplemented with better architecture later
    }
}
