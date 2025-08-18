using System.Threading.Tasks;

namespace NoteNest.Core.Interfaces.Services
{
    public interface ITabCloseService
    {
        Task<bool> CloseTabWithPromptAsync(ITabItem tab);
        Task<bool> CloseAllTabsWithPromptAsync();
    }
}


