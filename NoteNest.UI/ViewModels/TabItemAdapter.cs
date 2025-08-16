using System;
using System.ComponentModel;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;

namespace NoteNest.UI.ViewModels
{
    /// <summary>
    /// Enhanced adapter that allows NoteTabItem to work with IWorkspaceService
    /// Handles bidirectional synchronization and proper disposal
    /// </summary>
    public class TabItemAdapter : ITabItem, IDisposable
    {
        private readonly NoteTabItem _noteTabItem;
        private bool _disposed;
        
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
            set
            {
                if (_noteTabItem.IsDirty != value)
                {
                    _noteTabItem.IsDirty = value;
                }
            }
        }
        
        public string Content
        {
            get => _noteTabItem.Content;
            set
            {
                if (_noteTabItem.Content != value)
                {
                    _noteTabItem.Content = value;
                }
            }
        }
        
        // Access to underlying NoteTabItem for UI
        public NoteTabItem UnderlyingTab => _noteTabItem;
        
        private void OnNoteTabItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Handle specific property changes that might affect service layer
            switch (e.PropertyName)
            {
                case nameof(NoteTabItem.IsDirty):
                    // hook for notifying service if needed
                    break;
                case nameof(NoteTabItem.Content):
                    // hook for autosave triggers if needed
                    break;
            }
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                if (_noteTabItem is INotifyPropertyChanged npc)
                {
                    npc.PropertyChanged -= OnNoteTabItemPropertyChanged;
                }
                _disposed = true;
            }
        }

        public override bool Equals(object obj)
        {
            if (obj is TabItemAdapter other)
            {
                return ReferenceEquals(_noteTabItem, other._noteTabItem);
            }
            return false;
        }
        
        public override int GetHashCode()
        {
            return _noteTabItem?.GetHashCode() ?? 0;
        }
        
        public override string ToString()
        {
            return $"TabItemAdapter: {Title} (ID: {Id})";
        }
    }
}