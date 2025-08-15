using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Services; // Add this for ContentCache

namespace NoteNest.Core.Services.Implementation
{
    public class WorkspaceService : IWorkspaceService, INotifyPropertyChanged
    {
        private readonly ContentCache _contentCache;
        private readonly NoteService _noteService;
        private readonly IServiceErrorHandler _errorHandler;
        private readonly IAppLogger _logger;
        
        private ObservableCollection<ITabItem> _openTabs;
        private ITabItem? _selectedTab;
        
        public ObservableCollection<ITabItem> OpenTabs
        {
            get => _openTabs;
            private set
            {
                _openTabs = value;
                OnPropertyChanged();
            }
        }
        
        public ITabItem? SelectedTab
        {
            get => _selectedTab;
            set
            {
                var oldTab = _selectedTab;
                _selectedTab = value;
                OnPropertyChanged();
                TabSelectionChanged?.Invoke(this, new TabChangedEventArgs 
                { 
                    OldTab = oldTab, 
                    NewTab = value 
                });
            }
        }
        
        public bool HasUnsavedChanges => OpenTabs?.Any(t => t.IsDirty) ?? false;
        
        public event EventHandler<TabChangedEventArgs>? TabSelectionChanged;
        public event EventHandler<TabEventArgs>? TabOpened;
        public event EventHandler<TabEventArgs>? TabClosed;
        public event PropertyChangedEventHandler? PropertyChanged;
        
        public WorkspaceService(
            ContentCache contentCache,
            NoteService noteService,
            IServiceErrorHandler errorHandler,
            IAppLogger logger)
        {
            _contentCache = contentCache ?? throw new ArgumentNullException(nameof(contentCache));
            _noteService = noteService ?? throw new ArgumentNullException(nameof(noteService));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _openTabs = new ObservableCollection<ITabItem>();
            
            _logger.Debug("WorkspaceService initialized");
        }
        
        public async Task<ITabItem> OpenNoteAsync(NoteModel note)
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
        
        public async Task<bool> CloseTabAsync(ITabItem tab)
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
        
        public async Task<bool> CloseAllTabsAsync()
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
        
        public async Task SaveAllTabsAsync()
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
        
        public ITabItem? FindTabByNote(NoteModel note)
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
        
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    // Temporary implementation of ITabItem for Phase 1
    internal class TabItem : ITabItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public NoteModel Note { get; set; }
        public bool IsDirty { get; set; }
        public string Content { get; set; }
        
        public TabItem(NoteModel note)
        {
            Note = note;
            Id = Guid.NewGuid().ToString();
            Title = note?.Title ?? "Untitled";
            Content = note?.Content ?? string.Empty;
            IsDirty = false;
        }
    }
}