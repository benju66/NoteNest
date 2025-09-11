namespace NoteNest.Core.Models
{
    /// <summary>
    /// Pure data model for tree operation requests - UI-agnostic
    /// </summary>
    public class TreeCategoryOperationRequest
    {
        public string CategoryId { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public CategoryModel CategoryModel { get; set; } = null!;
    }

    /// <summary>
    /// Pure data model for note operation requests - UI-agnostic
    /// </summary>
    public class TreeNoteOperationRequest
    {
        public string NoteId { get; set; } = string.Empty;
        public string NoteTitle { get; set; } = string.Empty;
        public NoteModel NoteModel { get; set; } = null!;
    }

    /// <summary>
    /// Request for moving a note to a category
    /// </summary>
    public class TreeNoteMoveRequest
    {
        public TreeNoteOperationRequest Note { get; set; } = null!;
        public TreeCategoryOperationRequest TargetCategory { get; set; } = null!;
    }

    /// <summary>
    /// Request for creating a subcategory
    /// </summary>
    public class TreeSubCategoryCreateRequest
    {
        public TreeCategoryOperationRequest ParentCategory { get; set; } = null!;
        public string NewCategoryName { get; set; } = string.Empty;
    }
}
