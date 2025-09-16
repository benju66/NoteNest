using System;
using System.Threading.Tasks;

namespace NoteNest.Core.Interfaces.Services
{
    // PHASE 1B: Extract interfaces for testability and future refactoring

    /// <summary>
    /// Interface for building tab UI components
    /// Single Responsibility: UI construction and layout
    /// </summary>
    public interface ITabUIBuilder
    {
        /// <summary>
        /// Build the complete tab UI asynchronously
        /// </summary>
        Task<object> BuildTabUIAsync();
        
        /// <summary>
        /// Build tab UI synchronously (for backward compatibility)
        /// </summary>
        object BuildTabUI();
    }

    /// <summary>
    /// Interface for coordinating save operations
    /// Single Responsibility: Save timing and coordination
    /// </summary>
    public interface ITabSaveCoordinator
    {
        /// <summary>
        /// Initialize the save coordinator with required services
        /// </summary>
        void Initialize(object saveManager, object taskRunner, string noteId);
        
        /// <summary>
        /// Handle content change notification
        /// </summary>
        void HandleContentChanged(string content);
        
        /// <summary>
        /// Perform manual save operation
        /// </summary>
        Task SaveAsync();
        
        /// <summary>
        /// Start auto-save timers
        /// </summary>
        void StartTimers();
        
        /// <summary>
        /// Stop all timers
        /// </summary>
        void StopTimers();
        
        /// <summary>
        /// Event fired when save state changes
        /// </summary>
        event EventHandler<bool> IsSavingChanged;
    }

    /// <summary>
    /// Interface for managing tab event subscriptions
    /// Single Responsibility: Event lifecycle management
    /// </summary>
    public interface ITabEventManager
    {
        /// <summary>
        /// Wire up events between components
        /// </summary>
        void WireEvents(object editor, Action<string> onContentChanged);
        
        /// <summary>
        /// Unwire all events
        /// </summary>
        void UnwireEvents();
        
        /// <summary>
        /// Check if events are currently wired
        /// </summary>
        bool AreEventsWired { get; }
    }

    /// <summary>
    /// Interface for tab state management
    /// Single Responsibility: Tab metadata and state
    /// </summary>
    public interface ITabStateManager
    {
        /// <summary>
        /// Get or set dirty state
        /// </summary>
        bool IsDirty { get; set; }
        
        /// <summary>
        /// Get or set content loaded state
        /// </summary>
        bool IsContentLoaded { get; set; }
        
        /// <summary>
        /// Get or set saving state
        /// </summary>
        bool IsSaving { get; set; }
        
        /// <summary>
        /// Event fired when dirty state changes
        /// </summary>
        event EventHandler<bool> DirtyStateChanged;
        
        /// <summary>
        /// Event fired when saving state changes
        /// </summary>
        event EventHandler<bool> SavingStateChanged;
    }

    /// <summary>
    /// Interface for content management operations
    /// Single Responsibility: Content extraction and loading
    /// </summary>
    public interface ITabContentManager
    {
        /// <summary>
        /// Extract current content from editor
        /// </summary>
        string ExtractContent();
        
        /// <summary>
        /// Extract content asynchronously
        /// </summary>
        Task<string> ExtractContentAsync();
        
        /// <summary>
        /// Load content into editor
        /// </summary>
        Task LoadContentAsync(string content);
        
        /// <summary>
        /// Get the visual UI element
        /// </summary>
        object GetVisualElement();
        
        /// <summary>
        /// Mark editor as clean (no unsaved changes)
        /// </summary>
        void MarkClean();
    }
}
