using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Interfaces.Services;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Services
{
    public class UITabFactory : ITabFactory
    {
        private readonly ISaveManager _saveManager;
        
        public UITabFactory(ISaveManager saveManager)
        {
            _saveManager = saveManager;
        }
        
        public ITabItem CreateTab(NoteModel note, string noteId)
        {
            System.Diagnostics.Debug.WriteLine($"[UITabFactory] CreateTab called: noteId={noteId}, note.Id={note.Id}, saveManager={(_saveManager != null ? "OK" : "NULL")}");
            
            // Ensure note has the correct ID
            note.Id = noteId;
            System.Diagnostics.Debug.WriteLine($"[UITabFactory] Note.Id synchronized to: {note.Id}");
            
            // Always create NoteTabItem which integrates with SaveManager
            var tabItem = new NoteTabItem(note, _saveManager);
            System.Diagnostics.Debug.WriteLine($"[UITabFactory] NoteTabItem created: tabNoteId={tabItem.NoteId}");
            
            return tabItem;
        }
    }
}
