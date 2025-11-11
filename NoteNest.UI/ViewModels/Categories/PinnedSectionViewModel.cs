using System.Linq;
using System.Windows.Input;
using NoteNest.UI.Collections;
using NoteNest.UI.ViewModels.Common;
using NoteNest.Core.Commands;

namespace NoteNest.UI.ViewModels.Categories
{
    /// <summary>
    /// Represents the "PINNED" section at the top of the tree.
    /// Acts as a collapsible container for pinned categories and notes.
    /// </summary>
    public class PinnedSectionViewModel : ViewModelBase
    {
        private bool _isExpanded = true;
        
        public string Title => "PINNED";
        
        public SmartObservableCollection<object> Items { get; }
        
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }
        
        public bool HasItems => Items?.Any() ?? false;
        
        public ICommand ToggleExpandCommand { get; }
        
        public PinnedSectionViewModel()
        {
            Items = new SmartObservableCollection<object>();
            ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
            
            // Subscribe to collection changes to update HasItems
            Items.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasItems));
        }
    }
}

