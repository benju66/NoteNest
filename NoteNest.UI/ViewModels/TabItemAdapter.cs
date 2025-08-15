using System;
using System.ComponentModel;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;

namespace NoteNest.UI.ViewModels
{
    /// <summary>
    /// Adapter that allows NoteTabItem to work with IWorkspaceService
    /// </summary>
    public class TabItemAdapter : ITabItem
    {
        private readonly NoteTabItem _noteTabItem;
        
        public TabItemAdapter(NoteTabItem noteTabItem)
        {
            _noteTabItem = noteTabItem ?? throw new ArgumentNullException(nameof(noteTabItem));
            
            // Subscribe to property changes to keep in sync
            if (_noteTabItem is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged += OnNoteTabItemPropertyChanged;
            }
        }
        
        // ITabItem implementation
        public string Id => _noteTabItem.Note?.Id ?? Guid.NewGuid().ToString();
        public string Title => _noteTabItem.Title;
        public NoteModel Note => _noteTabItem.Note;
        
        public bool IsDirty
        {
            get => _noteTabItem.IsDirty;
            set => _noteTabItem.IsDirty = value;
        }
        
        public string Content
        {
            get => _noteTabItem.Content;
            set => _noteTabItem.Content = value;
        }
        
        // Access to underlying NoteTabItem for UI
        public NoteTabItem UnderlyingTab => _noteTabItem;
        
        private void OnNoteTabItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Handle any property synchronization if needed
        }
        
        public void Dispose()
        {
            if (_noteTabItem is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged -= OnNoteTabItemPropertyChanged;
            }
        }
    }
}