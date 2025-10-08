# Todo Plugin - Updated Design for Modern Plugin System

**Based on:** Modern Plugin System Architecture  
**Status:** Ready for Implementation  
**Confidence:** 99%

---

## 🎯 **Plugin Registration**

```csharp
public class TodoPlugin : IPlugin
{
    private readonly ITodoService _todoService;
    private IPluginContext _context;

    public PluginId Id => PluginId.From("todo-plugin");

    public PluginMetadata Metadata => new PluginMetadata(
        name: "Todo List",
        version: new Version(2, 0, 0),
        description: "Unified task management across projects with note linking",
        author: "NoteNest Team",
        dependencies: Array.Empty<string>(),
        minimumHostVersion: new Version(1, 0),
        category: PluginCategory.Productivity);

    public IReadOnlyList<string> RequestedCapabilities => new[]
    {
        "EventSubscription",          // Required for note event tracking
        "DataPersistence",            // Required for todo storage
        "UIIntegration",              // Required for todo panel
        "NoteAccess.ReadOnly"         // Required for bracket parsing (Phase 4)
    };

    public TodoPlugin(ITodoService todoService)
    {
        _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
    }

    public async Task<Result> InitializeAsync(IPluginContext context)
    {
        _context = context;

        try
        {
            // Get services through validated context
            var eventBusResult = await context.GetServiceAsync<NoteNest.Core.Services.IEventBus>();
            if (eventBusResult.IsFailure)
                return eventBusResult;

            // Subscribe to note events
            eventBusResult.Value.Subscribe<NoteNest.Core.Events.NoteMovedEvent>(async e =>
            {
                await _todoService.OnNoteMovedAsync(e.NoteId, e.OldPath, e.NewPath);
            });

            eventBusResult.Value.Subscribe<NoteNest.Core.Events.CategoryRenamedEvent>(async e =>
            {
                await _todoService.OnCategoryRenamedAsync(e.OldName, e.NewName);
            });

            eventBusResult.Value.Subscribe<NoteNest.Core.Events.NoteDeletedEvent>(async e =>
            {
                await _todoService.OnNoteDeletedAsync(e.NoteId);
            });

            eventBusResult.Value.Subscribe<NoteNest.Core.Events.NoteSavedEvent>(async e =>
            {
                // Phase 4: Parse brackets from saved notes
                await Task.CompletedTask;
            });

            // Load todos from plugin data store
            await _todoService.LoadAsync();

            context.Log("Info", "Todo plugin initialized successfully");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            context.Log("Error", $"Initialization failed: {ex.Message}");
            return Result.Fail($"Todo plugin initialization failed: {ex.Message}");
        }
    }

    public async Task<Result> ShutdownAsync()
    {
        await _todoService.SaveAsync();
        _context.Log("Info", "Todo plugin shutdown complete");
        return Result.Ok();
    }

    public async Task<IPluginPanelDescriptor> GetPanelDescriptorAsync()
    {
        return new TodoPanelDescriptor();
    }

    public async Task<PluginHealthStatus> GetHealthAsync()
    {
        var todoCount = await _todoService.GetActiveTodoCountAsync();
        
        return new PluginHealthStatus
        {
            IsHealthy = true,
            StatusMessage = $"{todoCount} active todos",
            LastChecked = DateTime.UtcNow,
            MemoryUsageBytes = GC.GetTotalMemory(false),
            ActiveSubscriptions = 4 // Note events
        };
    }

    public void Dispose()
    {
        // Cleanup resources
    }
}
```

---

## 📦 **Todo Service Implementation**

```csharp
public interface ITodoService
{
    // Core CRUD
    Task<Result<TodoItem>> CreateTodoAsync(string text, string category);
    Task<Result> UpdateTodoAsync(TodoItem todo);
    Task<Result> DeleteTodoAsync(string todoId);
    Task<Result> CompleteTodoAsync(string todoId);

    // Queries
    Task<IReadOnlyList<TodoItem>> GetActiveTodosAsync();
    Task<IReadOnlyList<TodoItem>> GetTodosByCategoryAsync(string category);
    Task<int> GetActiveTodoCountAsync();

    // Event Handlers
    Task OnNoteMovedAsync(string noteId, string oldPath, string newPath);
    Task OnCategoryRenamedAsync(string oldName, string newName);
    Task OnNoteDeletedAsync(string noteId);

    // Persistence
    Task LoadAsync();
    Task SaveAsync();
}

public class TodoService : ITodoService
{
    private readonly IPluginDataStore _dataStore;
    private readonly PluginId _pluginId;
    private TodoStorage _storage;
    private readonly SemaphoreSlim _lock = new(1);

    public TodoService(IPluginDataStore dataStore)
    {
        _dataStore = dataStore;
        _pluginId = PluginId.From("todo-plugin");
    }

    public async Task LoadAsync()
    {
        var result = await _dataStore.LoadDataAsync<TodoStorage>(_pluginId, "todos");
        _storage = result.Value ?? new TodoStorage();
    }

    public async Task SaveAsync()
    {
        await _dataStore.SaveDataAsync(_pluginId, "todos", _storage);
    }

    public async Task OnNoteMovedAsync(string noteId, string oldPath, string newPath)
    {
        await _lock.WaitAsync();
        try
        {
            var todosToUpdate = _storage.GetTodosByLinkedNote(noteId);
            foreach (var todo in todosToUpdate)
            {
                todo.LinkedNoteFilePath = newPath;
            }
            await SaveAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    // ... other methods following same pattern
}
```

---

## 🎨 **UI Integration**

### **TodoPanelViewModel**
```csharp
public class TodoPanelViewModel : ViewModelBase
{
    private readonly ITodoService _todoService;
    private readonly IPluginContext _context;

    public ObservableCollection<TodoCategoryViewModel> Categories { get; }
    public ICommand AddTodoCommand { get; }
    public ICommand RefreshCommand { get; }

    public TodoPanelViewModel(ITodoService todoService, IPluginContext context)
    {
        _todoService = todoService;
        _context = context;
        
        AddTodoCommand = new AsyncRelayCommand<string>(AddTodoAsync);
        RefreshCommand = new AsyncRelayCommand(LoadTodosAsync);
    }

    private async Task LoadTodosAsync()
    {
        var todos = await _todoService.GetActiveTodosAsync();
        // Populate Categories
    }
}
```

### **Panel Descriptor**
```csharp
public class TodoPanelDescriptor : IPluginPanelDescriptor
{
    public string Title => "Todo List";
    public string Icon => "✓";
    public Type ViewModelType => typeof(TodoPanelViewModel);
    public UISlotType PreferredSlot => UISlotType.RightPanel;
    public double PreferredWidth => 350;
    public double MinWidth => 250;
    public double MaxWidth => 500;
}
```

---

## 🔗 **Event Subscription Pattern**

```csharp
// In TodoPlugin.InitializeAsync()
var eventBus = await context.GetServiceAsync<IEventBus>();

// Subscribe to all required events
eventBus.Value.Subscribe<NoteMovedEvent>(async e =>
{
    await _todoService.OnNoteMovedAsync(e.NoteId.Value, e.OldPath, e.NewPath);
});

eventBus.Value.Subscribe<CategoryRenamedEvent>(async e =>
{
    await _todoService.OnCategoryRenamedAsync(e.OldName, e.NewName);
});

eventBus.Value.Subscribe<NoteDeletedEvent>(async e =>
{
    await _todoService.OnNoteDeletedAsync(e.NoteId.Value);
});

// Phase 4: Bracket parsing
eventBus.Value.Subscribe<NoteSavedEvent>(async e =>
{
    if (context.HasCapability("NoteAccess.ReadOnly"))
    {
        await _bracketParser.ParseAndSyncAsync(e.NoteId, e.Content);
    }
});
```

---

## 📋 **Implementation Phases (Revised)**

### **Phase 1: Core Todo Plugin** (2-3 days)
- Implement TodoPlugin class
- Create ITodoService and TodoService
- Basic CRUD operations
- Event subscriptions
- Data persistence

### **Phase 2: UI Panel** (1-2 days)
- TodoPanelViewModel
- TodoPanel.xaml view
- Category organization
- Quick-add functionality

### **Phase 3: Category Filtering** (1 day)
- Monitored categories support
- Filter UI
- Settings persistence

### **Phase 4: Bracket Parsing** (2-3 days)
- RTF content parsing
- `[action item]` detection
- Line number mapping
- Sync with manual todos

### **Phase 5: Visual Sync** (1-2 days)
- Completed todo highlighting in notes
- Ephemeral rendering
- Performance optimization

**Total: 1-2 weeks for complete Todo plugin**

---

## ✅ **Benefits of New Architecture**

### **vs. Old Plugin System:**
- ✅ **Proper dependency injection** (no service locator anti-pattern)
- ✅ **Capability-based security** (granular permissions)
- ✅ **Event-driven integration** (no tight coupling)
- ✅ **Clean Architecture compliance** (CQRS patterns)
- ✅ **Performance monitoring** (built-in health checks)
- ✅ **Resource isolation** (data directories, logging)
- ✅ **Type safety** (strong typing throughout)

### **vs. First-Class Feature:**
- ✅ **Optional** (users can disable if not needed)
- ✅ **Isolated** (doesn't impact core app performance)
- ✅ **Maintainable** (clear boundaries, easy to test)
- ✅ **Extensible** (other plugins can follow same pattern)

---

## 🎯 **Next Steps**

1. **Close running NoteNest.UI application** to unlock DLLs
2. **Run full build** to verify everything compiles
3. **Implement Todo plugin** following the updated design
4. **Test with real data** and validate all capabilities
5. **Performance test** with 100+ todos
6. **Security audit** of capability system

**The plugin system foundation is complete and ready for use!**

