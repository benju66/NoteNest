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
    /// Central coordinator for all tree operations and state management.
    /// Orchestrates data loading, operations, and state persistence without UI dependencies.
    /// </summary>
    public class TreeController : ITreeController
    {
        private readonly ITreeDataService _treeDataService;
        private readonly ITreeOperationService _treeOperationService;
        private readonly ITreeStateManager _treeStateManager;
        private readonly IAppLogger _logger;

        public event EventHandler<TreeChangedEventArgs> TreeChanged;

        public TreeController(
            ITreeDataService treeDataService,
            ITreeOperationService treeOperationService,
            ITreeStateManager treeStateManager,
            IAppLogger logger)
        {
            _treeDataService = treeDataService ?? throw new ArgumentNullException(nameof(treeDataService));
            _treeOperationService = treeOperationService ?? throw new ArgumentNullException(nameof(treeOperationService));
            _treeStateManager = treeStateManager ?? throw new ArgumentNullException(nameof(treeStateManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Loads and refreshes the entire tree data structure
        /// </summary>
        public async Task<TreeDataResult> LoadTreeDataAsync()
        {
            try
            {
                _logger.Debug("TreeController: Loading tree data...");
                
                var result = await _treeDataService.LoadTreeDataAsync();
                
                if (result.Success)
                {
                    TreeChanged?.Invoke(this, new TreeChangedEventArgs
                    {
                        ChangeType = TreeChangeType.Loaded,
                        Details = $"Loaded {result.TotalCategoriesLoaded} categories"
                    });
                    
                    _logger.Info($"TreeController: Successfully loaded tree with {result.TotalCategoriesLoaded} categories");
                }
                else
                {
                    _logger.Error($"TreeController: Failed to load tree data: {result.ErrorMessage}");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "TreeController: Unexpected error loading tree data");
                return new TreeDataResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Coordinates tree operations and ensures data consistency
        /// </summary>
        public async Task<TreeOperationResult<T>> ExecuteOperationAsync<T>(Func<Task<TreeOperationResult<T>>> operation)
        {
            try
            {
                _logger.Debug("TreeController: Executing tree operation...");
                
                var result = await operation();
                
                if (result.Success)
                {
                    // Notify about the change
                    TreeChanged?.Invoke(this, new TreeChangedEventArgs
                    {
                        ChangeType = TreeChangeType.CategoryCreated, // Generic for now
                        Details = result.StatusMessage
                    });
                    
                    _logger.Debug($"TreeController: Operation completed successfully: {result.StatusMessage}");
                }
                else
                {
                    _logger.Warning($"TreeController: Operation failed: {result.ErrorMessage}");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "TreeController: Unexpected error executing operation");
                return TreeOperationResult<T>.CreateFailure($"Unexpected error: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves the current tree expansion state
        /// </summary>
        public async Task SaveTreeStateAsync(List<string> expandedCategoryIds)
        {
            try
            {
                _logger.Debug("TreeController: Saving tree expansion state...");
                await _treeStateManager.SaveExpansionStateAsync(expandedCategoryIds);
                _logger.Debug("TreeController: Tree state saved successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "TreeController: Failed to save tree state");
            }
        }

        /// <summary>
        /// Finds a category node containing a specific note
        /// </summary>
        public TreeNodeData FindCategoryContainingNote(TreeNodeData rootCategory, string noteId)
        {
            if (rootCategory?.Notes?.Any(n => n.Id == noteId) == true)
                return rootCategory;

            if (rootCategory?.Children != null)
            {
                foreach (var child in rootCategory.Children)
                {
                    var found = FindCategoryContainingNote(child, noteId);
                    if (found != null) return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a note by ID in the tree data structure
        /// </summary>
        public TreeNodeData FindNoteById(List<TreeNodeData> rootNodes, string noteId)
        {
            foreach (var root in rootNodes)
            {
                var found = FindCategoryContainingNote(root, noteId);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Gets count of all categories in a subtree
        /// </summary>
        public int CountAllCategories(TreeNodeData root)
        {
            int count = 1; // Count this node
            
            if (root?.Children != null)
            {
                foreach (var child in root.Children)
                {
                    count += CountAllCategories(child);
                }
            }
            
            return count;
        }

        /// <summary>
        /// Gets count of all notes in a category subtree
        /// </summary>
        public int CountAllNotes(TreeNodeData category)
        {
            int count = category?.Notes?.Count ?? 0;
            
            if (category?.Children != null)
            {
                foreach (var child in category.Children)
                {
                    count += CountAllNotes(child);
                }
            }
            
            return count;
        }

        /// <summary>
        /// Gets all categories as a flat list from tree data
        /// </summary>
        public List<CategoryModel> GetAllCategoriesFlat(List<TreeNodeData> rootNodes)
        {
            var list = new List<CategoryModel>();
            
            void Walk(List<TreeNodeData> nodes)
            {
                foreach (var node in nodes)
                {
                    if (node?.Category != null)
                    {
                        list.Add(node.Category);
                        Walk(node.Children);
                    }
                }
            }
            
            Walk(rootNodes);
            return list;
        }
    }
}
