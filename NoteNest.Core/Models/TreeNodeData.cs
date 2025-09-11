using System.Collections.Generic;

namespace NoteNest.Core.Models
{
    /// <summary>
    /// Pure data representation of a tree node, UI-agnostic
    /// </summary>
    public class TreeNodeData
    {
        public CategoryModel Category { get; set; }
        public List<TreeNodeData> Children { get; set; } = new();
        public List<NoteModel> Notes { get; set; } = new();
        public bool IsExpanded { get; set; }
        public int Level { get; set; }
    }

    /// <summary>
    /// Result of tree data loading operation - UI-agnostic
    /// </summary>
    public class TreeDataResult
    {
        public List<TreeNodeData> RootNodes { get; set; } = new();
        public List<TreeNodeData> PinnedCategoryNodes { get; set; } = new();
        public List<PinnedNoteData> PinnedNotes { get; set; } = new();
        public int TotalCategoriesLoaded { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Pure data representation of a pinned note, UI-agnostic
    /// </summary>
    public class PinnedNoteData
    {
        public NoteModel Note { get; set; }
        public string CategoryName { get; set; }
        
        public PinnedNoteData(NoteModel note, string categoryName)
        {
            Note = note;
            CategoryName = categoryName;
        }
    }
}
