using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Core.Models;

namespace NoteNest.Core.Interfaces
{
    public interface ISearchProvider
    {
        Task<IEnumerable<NoteModel>> SearchNotesAsync(string searchTerm, SearchOptions options = null);
        Task<IEnumerable<NoteModel>> SearchByTagsAsync(IEnumerable<string> tags);
        Task<IEnumerable<NoteModel>> SearchByCategoryAsync(string categoryId);
        Task<IEnumerable<NoteModel>> GetRecentNotesAsync(int count = 10);
    }
    
    public class SearchOptions
    {
        public bool SearchInContent { get; set; } = true;
        public bool SearchInTitle { get; set; } = true;
        public bool CaseSensitive { get; set; } = false;
        public bool UseRegex { get; set; } = false;
        public string CategoryFilter { get; set; }
        public List<string> TagFilters { get; set; }
        
        public SearchOptions()
        {
            TagFilters = new List<string>();
        }
    }
}
