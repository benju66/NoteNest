using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using NoteNest.Core.Models;

namespace NoteNest.Core.Interfaces.Services
{
    /// <summary>
    /// Central coordinator for all tree operations and state management.
    /// Orchestrates data loading, operations, and state persistence.
    /// Works with pure data models to avoid UI dependencies.
    /// </summary>
    public interface ITreeController
    {
        /// <summary>
        /// Loads and refreshes the entire tree data structure
        /// </summary>
        Task<TreeDataResult> LoadTreeDataAsync();
        
        /// <summary>
        /// Coordinates tree operations and ensures data consistency
        /// </summary>
        Task<TreeOperationResult<T>> ExecuteOperationAsync<T>(Func<Task<TreeOperationResult<T>>> operation);
        
        /// <summary>
        /// Saves the current tree expansion state
        /// </summary>
        Task SaveTreeStateAsync(List<string> expandedCategoryIds);
        
        /// <summary>
        /// Finds a category node containing a specific note
        /// </summary>
        TreeNodeData FindCategoryContainingNote(TreeNodeData rootCategory, string noteId);
        
        /// <summary>
        /// Finds a note by ID in the tree data structure
        /// </summary>
        TreeNodeData FindNoteById(List<TreeNodeData> rootNodes, string noteId);
        
        /// <summary>
        /// Gets count of all categories in a subtree
        /// </summary>
        int CountAllCategories(TreeNodeData root);
        
        /// <summary>
        /// Gets count of all notes in a category subtree
        /// </summary>
        int CountAllNotes(TreeNodeData category);
        
        /// <summary>
        /// Gets all categories as a flat list from tree data
        /// </summary>
        List<CategoryModel> GetAllCategoriesFlat(List<TreeNodeData> rootNodes);
        
        /// <summary>
        /// Event raised when tree structure changes
        /// </summary>
        event EventHandler<TreeChangedEventArgs> TreeChanged;
    }

    /// <summary>
    /// Event arguments for tree change notifications
    /// </summary>
    public class TreeChangedEventArgs : EventArgs
    {
        public TreeChangeType ChangeType { get; set; }
        public string? AffectedNodeId { get; set; }
        public string? Details { get; set; }
    }

    /// <summary>
    /// Types of tree changes
    /// </summary>
    public enum TreeChangeType
    {
        Loaded,
        CategoryCreated,
        CategoryDeleted,
        CategoryRenamed,
        CategoryMoved,
        NoteCreated,
        NoteDeleted,
        NoteRenamed,
        NoteMoved,
        StateRestored
    }
}
