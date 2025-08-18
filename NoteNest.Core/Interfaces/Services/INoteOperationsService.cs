using System;
using System.Threading.Tasks;
using NoteNest.Core.Models;

namespace NoteNest.Core.Interfaces.Services
{
    public interface INoteOperationsService
    {
        Task<NoteModel> CreateNoteAsync(CategoryModel category, string title, string content = "");
        Task SaveNoteAsync(NoteModel note);
        Task DeleteNoteAsync(NoteModel note);
        Task<bool> RenameNoteAsync(NoteModel note, string newName);
        Task<bool> MoveNoteAsync(NoteModel note, CategoryModel targetCategory);
        // Removed: SaveAllNotesAsync (use workspace or state service for batch saves)
        
        // Methods for tracking open notes (for SaveAll functionality)
        void TrackOpenNote(NoteModel note);
        void UntrackOpenNote(NoteModel note);
        void ClearTrackedNotes();
    }
}