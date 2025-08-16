using System;
using System.Collections.ObjectModel;
using NoteNest.Core.Interfaces.Split;
using NoteNest.Core.Interfaces.Services;

namespace NoteNest.Core.Models
{
    public class SplitPane : ISplitElement
    {
        public string Id { get; }
        public bool IsActive { get; set; }
        public double MinWidth => 150;
        public double MinHeight => 100;
        
        // Tabs in this pane
        public ObservableCollection<ITabItem> Tabs { get; }
        public ITabItem? SelectedTab { get; set; }
        
        public SplitPane()
        {
            Id = Guid.NewGuid().ToString();
            Tabs = new ObservableCollection<ITabItem>();
            IsActive = false;
        }
    }
}


