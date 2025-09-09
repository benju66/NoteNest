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
            // Ensure note has the correct ID
            note.Id = noteId;
            
            // Always create NoteTabItem which integrates with SaveManager
            return new NoteTabItem(note, _saveManager);
        }
    }
}
