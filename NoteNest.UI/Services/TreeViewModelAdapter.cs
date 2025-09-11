using System.Collections.Generic;
using System.Linq;
using NoteNest.Core.Models;
using NoteNest.UI.ViewModels;
using NoteNest.Core.Services;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Adapter that converts between UI-agnostic tree data and UI ViewModels.
    /// This maintains separation of concerns while allowing UI to work with familiar models.
    /// </summary>
    public class TreeViewModelAdapter
    {
        private readonly NoteService _noteService;

        public TreeViewModelAdapter(NoteService noteService)
        {
            _noteService = noteService;
        }

        /// <summary>
        /// Converts tree data result to UI-ready collections
        /// </summary>
        public TreeUICollections ConvertToUICollections(TreeDataResult treeData)
        {
            var result = new TreeUICollections();

            // Convert root nodes to CategoryTreeItems
            foreach (var rootNode in treeData.RootNodes)
            {
                var categoryTreeItem = ConvertToCategoryTreeItem(rootNode);
                result.RootCategories.Add(categoryTreeItem);
            }

            // Convert pinned categories
            foreach (var pinnedNode in treeData.PinnedCategoryNodes)
            {
                var categoryTreeItem = ConvertToCategoryTreeItem(pinnedNode);
                result.PinnedCategories.Add(categoryTreeItem);
            }

            // Convert pinned notes
            foreach (var pinnedNote in treeData.PinnedNotes)
            {
                var noteTreeItem = new NoteTreeItem(pinnedNote.Note);
                var pinnedNoteItem = new PinnedNoteItem(noteTreeItem, pinnedNote.CategoryName);
                result.PinnedNotes.Add(pinnedNoteItem);
            }

            return result;
        }

        /// <summary>
        /// Converts a TreeNodeData to CategoryTreeItem recursively
        /// </summary>
        private CategoryTreeItem ConvertToCategoryTreeItem(TreeNodeData node)
        {
            // Create CategoryTreeItem without NoteService to prevent automatic loading
            // We'll handle the notes ourselves since we already have the data
            var categoryTreeItem = new CategoryTreeItem(node.Category, null);

            // CRITICAL FIX: Temporarily disable collection change events during bulk operations
            // This prevents UI event storms that cause infinite loading
            categoryTreeItem.SuspendCollectionEvents();

            try
            {
                // Convert child categories
                foreach (var child in node.Children)
                {
                    var childTreeItem = ConvertToCategoryTreeItem(child);
                    categoryTreeItem.SubCategories.Add(childTreeItem);
                }

                // Pre-populate notes since we already have the data from TreeDataService
                foreach (var note in node.Notes)
                {
                    var noteTreeItem = new NoteTreeItem(note);
                    categoryTreeItem.Notes.Add(noteTreeItem);
                }

                // Mark as loaded to prevent duplicate loading attempts
                categoryTreeItem.IsLoaded = true;

                // Set expansion state
                categoryTreeItem.IsExpanded = node.IsExpanded;
            }
            finally
            {
                // Re-enable collection change events and trigger one final update
                categoryTreeItem.ResumeCollectionEvents();
            }

            return categoryTreeItem;
        }
    }

    /// <summary>
    /// Container for UI-ready tree collections
    /// </summary>
    public class TreeUICollections
    {
        public List<CategoryTreeItem> RootCategories { get; set; } = new();
        public List<CategoryTreeItem> PinnedCategories { get; set; } = new();
        public List<PinnedNoteItem> PinnedNotes { get; set; } = new();
    }

    /// <summary>
    /// UI representation of a pinned note
    /// </summary>
    public class PinnedNoteItem
    {
        public NoteTreeItem Note { get; }
        public string CategoryName { get; }
        
        public PinnedNoteItem(NoteTreeItem note, string categoryName)
        {
            Note = note;
            CategoryName = categoryName;
        }
    }
}
