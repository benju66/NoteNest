using NoteNest.Core.Models;
using NoteNest.Core.Interfaces.Services;

namespace NoteNest.Core.Services
{
    public interface ITabFactory
    {
        ITabItem CreateTab(NoteModel note, string noteId);
    }
}
