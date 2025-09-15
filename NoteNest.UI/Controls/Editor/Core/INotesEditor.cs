using System;
using System.Windows.Documents;
using NoteNest.Core.Models;

namespace NoteNest.UI.Controls.Editor.Core
{
    /// <summary>
    /// Common interface for all editor implementations
    /// </summary>
    public interface INotesEditor
    {
        // Core properties
        FlowDocument Document { get; }
        bool IsDirty { get; }
        string OriginalContent { get; }
        NoteFormat Format { get; }
        
        // Core operations
        void LoadContent(string content);
        string SaveContent();
        string GetQuickContent(); // For WAL protection
        void MarkClean();
        void MarkDirty();
        
        // Save coordination
        void ForceContentNotification(); // For immediate saves (tab switches, manual saves)
        
        // Formatting operations
        void InsertBulletList();
        void InsertNumberedList();
        void IndentSelection();
        void OutdentSelection();
        void ToggleBold();
        void ToggleItalic();
        
        // Events
        event EventHandler ContentChanged;
        event EventHandler<ListStateChangedEventArgs> ListStateChanged;
    }
}
