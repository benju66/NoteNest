# 🎯 TodoPlugin Category-Tree Sync - Complete Implementation Guide

**Date:** October 10, 2025  
**For:** Fresh implementation (new chat or developer)  
**Goal:** Sync TodoPlugin categories with note tree structure + context menu integration

---

## 📋 EXECUTIVE SUMMARY

### **What This Implements:**

**User Workflow:**
1. User right-clicks category "Work/Projects/ProjectA" in **note tree**
2. Context menu shows: "Add to Todo Categories"
3. User clicks → Category appears in **Todo panel** tree
4. User creates todo under "ProjectA" category
5. When RTF parser extracts `[todo]` from note in ProjectA → Auto-categorizes under ProjectA

**Benefits:**
- ✅ Todo categories mirror note organization
- ✅ Seamless integration between notes and todos
- ✅ No manual category management needed
- ✅ Extracted todos auto-organized by project/folder

---

## 🏗️ CURRENT ARCHITECTURE (Context)

### **Main App: Tree Database System**

**Database:** `tree.db` (SQLite)

```sql
CREATE TABLE tree_nodes (
    id TEXT PRIMARY KEY,           -- Guid as TEXT
    parent_id TEXT,                -- Hierarchical structure
    node_type TEXT,                -- 'category' or 'note'
    name TEXT,
    canonical_path TEXT,
    display_path TEXT,
    absolute_path TEXT,
    ...
);
```

**Repository:** `ITreeDatabaseRepository`
- Provides CRUD operations on tree nodes
- Queries categories and notes
- Located: `NoteNest.Infrastructure/Database/TreeDatabaseRepository.cs`

**ViewModels:**
- `CategoryTreeViewModel` - Manages tree display
- `CategoryViewModel` - Individual category node
- `CategoryOperationsViewModel` - CRUD operations (Create, Rename, Delete, Move)

**Pattern:** Clean Architecture with CQRS (MediatR commands)

---

### **TodoPlugin: Current State**

**Database:** `todos.db` (SQLite, separate from tree.db!)

```sql
CREATE TABLE todos (
    id TEXT PRIMARY KEY,
    text TEXT,
    category_id TEXT,              -- ← Links to tree_nodes.id
    source_note_id TEXT,           -- ← Links to source note
    source_type TEXT,              -- 'manual' or 'note'
    ...
);
```

**Category System (CURRENT - BROKEN):**
```csharp
// CategoryStore.cs - HARDCODED!
public CategoryStore()
{
    Add(new Category { Name = "Personal" });   // ← Hardcoded
    Add(new Category { Name = "Work" });       // ← Hardcoded  
    Add(new Category { Name = "Shopping" });   // ← Hardcoded
    // No connection to tree_nodes!
}
```

**RTF Integration (EXISTS):**
- `BracketTodoParser.cs` (442 lines) - Extracts `[todos]`
- `TodoSyncService.cs` (267 lines) - Background sync on note save
- Reconciliation logic - Add/orphan/update todos

**Domain Layer (JUST ADDED):**
- `TodoAggregate` - Rich domain model
- `TodoItemDto` - Database mapping
- `TodoMapper` - Converts UI ↔ Domain ↔ Database

---

## 🎯 WHAT NEEDS TO BE IMPLEMENTED

### **Feature 1: Context Menu Integration**

**User Action:**
```
Note Tree:
├── Personal/
├── Work/
│   ├── Projects/
│   │   └── ProjectA/  ← Right-click here
│   │       └── context menu: "Add to Todo Categories" ← NEW!
```

**Result:**
```
Todo Panel Category Tree:
├── [Smart Lists]
│   ├── Today
│   ├── Overdue
│   └── Favorites
├── [Categories]  ← NEW SECTION!
│   └── Work/
│       └── Projects/
│           └── ProjectA/ ← Category added!
```

---

### **Feature 2: Category Synchronization**

**Mechanism:** Query `tree_nodes` where `node_type = 'category'`

**Architecture:**
```
Note Tree Database (tree.db)
    ↓ Query
CategorySyncService
    ↓ Convert
Todo Categories (in-memory)
    ↓ Display
Todo Panel Tree View
```

---

### **Feature 3: Auto-Categorization**

**RTF Parser Flow:**
```
1. User saves note at: Work/Projects/ProjectA/meeting.rtf
2. TodoSyncService detects save event
3. BracketTodoParser extracts: "[call client]"
4. Auto-categorize:
   todo.CategoryId = note.ParentId  ← Note's category!
   todo.SourceNoteId = note.Id
5. Todo appears under "ProjectA" in Todo panel ✅
```

---

## 📊 IMPLEMENTATION PLAN

### **PHASE 1: Category Sync Infrastructure (3 hours)**

#### **Step 1.1: Create CategorySyncService (1 hour)**

**Location:** `NoteNest.UI/Plugins/TodoPlugin/Services/CategorySyncService.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Infrastructure.Database;
using NoteNest.UI.Plugins.TodoPlugin.Models;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Trees;

namespace NoteNest.UI.Plugins.TodoPlugin.Services
{
    /// <summary>
    /// Synchronizes TodoPlugin categories with the main app's note tree structure.
    /// Categories are queried live from tree_nodes database.
    /// </summary>
    public interface ICategorySyncService
    {
        Task<List<Category>> GetAllCategoriesAsync();
        Task<Category?> GetCategoryByIdAsync(Guid categoryId);
        Task<List<Category>> GetRootCategoriesAsync();
        Task<List<Category>> GetChildCategoriesAsync(Guid parentId);
        Task<bool> IsCategoryInTreeAsync(Guid categoryId);
    }
    
    public class CategorySyncService : ICategorySyncService
    {
        private readonly ITreeDatabaseRepository _treeRepository;
        private readonly IAppLogger _logger;
        
        public CategorySyncService(
            ITreeDatabaseRepository treeRepository,
            IAppLogger logger)
        {
            _treeRepository = treeRepository ?? throw new ArgumentNullException(nameof(treeRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Get all categories from note tree database
        /// </summary>
        public async Task<List<Category>> GetAllCategoriesAsync()
        {
            try
            {
                // Query tree_nodes where node_type = 'category'
                var treeNodes = await _treeRepository.GetAllNodesAsync(includeDeleted: false);
                
                var categories = treeNodes
                    .Where(n => n.NodeType == TreeNodeType.Category)
                    .Select(n => new Category
                    {
                        Id = n.Id,
                        ParentId = n.ParentId,
                        Name = n.Name,
                        Order = n.SortOrder,
                        CreatedDate = n.CreatedAt,
                        ModifiedDate = n.ModifiedAt
                    })
                    .ToList();
                
                _logger.Debug($"[CategorySync] Loaded {categories.Count} categories from tree");
                return categories;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategorySync] Failed to load categories from tree");
                return new List<Category>();
            }
        }
        
        /// <summary>
        /// Get single category by ID
        /// </summary>
        public async Task<Category?> GetCategoryByIdAsync(Guid categoryId)
        {
            try
            {
                var treeNode = await _treeRepository.GetNodeByIdAsync(categoryId);
                
                if (treeNode == null || treeNode.NodeType != TreeNodeType.Category)
                    return null;
                
                return new Category
                {
                    Id = treeNode.Id,
                    ParentId = treeNode.ParentId,
                    Name = treeNode.Name,
                    Order = treeNode.SortOrder,
                    CreatedDate = treeNode.CreatedAt,
                    ModifiedDate = treeNode.ModifiedAt
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[CategorySync] Failed to get category: {categoryId}");
                return null;
            }
        }
        
        /// <summary>
        /// Get root categories (no parent)
        /// </summary>
        public async Task<List<Category>> GetRootCategoriesAsync()
        {
            try
            {
                var rootNodes = await _treeRepository.GetRootNodesAsync();
                
                return rootNodes
                    .Where(n => n.NodeType == TreeNodeType.Category)
                    .Select(n => new Category
                    {
                        Id = n.Id,
                        ParentId = n.ParentId,
                        Name = n.Name,
                        Order = n.SortOrder
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategorySync] Failed to get root categories");
                return new List<Category>();
            }
        }
        
        /// <summary>
        /// Get child categories of a parent
        /// </summary>
        public async Task<List<Category>> GetChildCategoriesAsync(Guid parentId)
        {
            try
            {
                var children = await _treeRepository.GetChildrenAsync(parentId);
                
                return children
                    .Where(n => n.NodeType == TreeNodeType.Category)
                    .Select(n => new Category
                    {
                        Id = n.Id,
                        ParentId = n.ParentId,
                        Name = n.Name,
                        Order = n.SortOrder
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[CategorySync] Failed to get children: {parentId}");
                return new List<Category>();
            }
        }
        
        /// <summary>
        /// Check if category exists in tree (validation)
        /// </summary>
        public async Task<bool> IsCategoryInTreeAsync(Guid categoryId)
        {
            try
            {
                var node = await _treeRepository.GetNodeByIdAsync(categoryId);
                return node != null && node.NodeType == TreeNodeType.Category;
            }
            catch
            {
                return false;
            }
        }
    }
}
```

**Design Patterns Used:**
- Repository Pattern (queries tree database)
- Adapter Pattern (converts TreeNode → Category)
- Interface Segregation (ICategorySyncService)

**Why This Design:**
- ✅ Separation of concerns (sync logic isolated)
- ✅ Testable (can mock ITreeDatabaseRepository)
- ✅ Async/await (non-blocking)
- ✅ Error handling (graceful degradation)

---

#### **Step 1.2: Update CategoryStore (30 min)**

**Location:** `NoteNest.UI/Plugins/TodoPlugin/Services/CategoryStore.cs`

**Current (Hardcoded):**
```csharp
public class CategoryStore : ICategoryStore
{
    private readonly SmartObservableCollection<Category> _categories;

    public CategoryStore()
    {
        _categories = new SmartObservableCollection<Category>();
        Add(new Category { Name = "Personal" });  // ← Remove
        Add(new Category { Name = "Work" });      // ← Remove
        Add(new Category { Name = "Shopping" });  // ← Remove
    }
    
    public ObservableCollection<Category> Categories => _categories;
}
```

**New (Dynamic):**
```csharp
public class CategoryStore : ICategoryStore
{
    private readonly SmartObservableCollection<Category> _categories;
    private readonly ICategorySyncService _syncService;
    private readonly IAppLogger _logger;
    private bool _isInitialized;

    public CategoryStore(
        ICategorySyncService syncService,
        IAppLogger logger)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _categories = new SmartObservableCollection<Category>();
    }
    
    /// <summary>
    /// Initialize by loading categories from tree database
    /// Call this once during plugin startup
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;
            
        try
        {
            _logger.Info("[CategoryStore] Loading categories from note tree...");
            
            var categories = await _syncService.GetAllCategoriesAsync();
            
            using (_categories.BatchUpdate())
            {
                _categories.Clear();
                _categories.AddRange(categories);
            }
            
            _isInitialized = true;
            _logger.Info($"[CategoryStore] Loaded {categories.Count} categories from tree");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[CategoryStore] Failed to load categories");
            _isInitialized = true; // Mark as initialized anyway (graceful degradation)
        }
    }
    
    /// <summary>
    /// Refresh categories from tree (call when tree structure changes)
    /// </summary>
    public async Task RefreshAsync()
    {
        await InitializeAsync(); // Reload from tree
    }

    public ObservableCollection<Category> Categories => _categories;

    public Category? GetById(Guid id)
    {
        return _categories.FirstOrDefault(c => c.Id == id);
    }

    // Add/Update/Delete methods stay the same (for potential manual categories)
    // But primarily read-only now (categories come from tree)
}
```

**Update Interface:**
```csharp
public interface ICategoryStore
{
    ObservableCollection<Category> Categories { get; }
    Task InitializeAsync();              // ← NEW
    Task RefreshAsync();                 // ← NEW
    Category? GetById(Guid id);
    void Add(Category category);
    void Update(Category category);
    void Delete(Guid id);
}
```

**Why This Design:**
- ✅ Lazy loading (query on demand)
- ✅ Observable collection (UI updates automatically)
- ✅ Graceful degradation (errors don't crash)
- ✅ Async initialization (non-blocking)

---

#### **Step 1.3: Register in DI (15 min)**

**Location:** `NoteNest.UI/Composition/PluginSystemConfiguration.cs`

**Add:**
```csharp
public static IServiceCollection AddPluginSystem(this IServiceCollection services)
{
    // ... existing todo plugin services ...
    
    // NEW: Category sync service
    services.AddSingleton<NoteNest.UI.Plugins.TodoPlugin.Services.ICategorySyncService>(provider =>
        new NoteNest.UI.Plugins.TodoPlugin.Services.CategorySyncService(
            provider.GetRequiredService<ITreeDatabaseRepository>(),
            provider.GetRequiredService<IAppLogger>()
        ));
    
    // UPDATE: CategoryStore now needs CategorySyncService
    services.AddSingleton<ICategoryStore>(provider =>
        new CategoryStore(
            provider.GetRequiredService<ICategorySyncService>(),
            provider.GetRequiredService<IAppLogger>()
        ));
    
    // ... rest of services ...
}
```

---

#### **Step 1.4: Initialize CategoryStore (15 min)**

**Location:** `NoteNest.UI/ViewModels/Shell/MainShellViewModel.cs`

**Update InitializeTodoPluginAsync:**
```csharp
private async Task InitializeTodoPluginAsync()
{
    try
    {
        _logger.Info("[TodoPlugin] Initializing database...");
        
        // Register Dapper type handlers
        Dapper.SqlMapper.AddTypeHandler(new GuidTypeHandler());
        Dapper.SqlMapper.AddTypeHandler(new NullableGuidTypeHandler());
        
        // Initialize database schema
        var dbInitializer = _serviceProvider?.GetService<ITodoDatabaseInitializer>();
        if (dbInitializer != null)
        {
            await dbInitializer.InitializeAsync();
        }
        
        // NEW: Initialize CategoryStore (load from tree)
        var categoryStore = _serviceProvider?.GetService<ICategoryStore>();
        if (categoryStore != null)
        {
            await categoryStore.InitializeAsync();
            _logger.Info("[TodoPlugin] CategoryStore initialized from tree");
        }
        
        // Initialize TodoStore
        var todoStore = _serviceProvider?.GetService<ITodoStore>();
        if (todoStore is TodoStore store)
        {
            await store.InitializeAsync();
        }
    }
    catch (Exception ex)
    {
        _logger.Error(ex, "[TodoPlugin] Failed to initialize");
    }
}
```

---

### **PHASE 2: Context Menu Integration (2 hours)**

#### **Step 2.1: Add Context Menu Command (1 hour)**

**Location:** `NoteNest.UI/ViewModels/Categories/CategoryOperationsViewModel.cs`

**Add New Command:**
```csharp
public class CategoryOperationsViewModel : ViewModelBase
{
    // Existing fields...
    private readonly IServiceProvider _serviceProvider;
    
    // NEW: Command for adding to todo categories
    public ICommand AddToTodoCategoriesCommand { get; private set; }
    
    private void InitializeCommands()
    {
        // Existing commands...
        CreateCategoryCommand = new AsyncRelayCommand<object>(...);
        DeleteCategoryCommand = new AsyncRelayCommand<object>(...);
        
        // NEW: Add to todo categories
        AddToTodoCategoriesCommand = new AsyncRelayCommand<object>(
            ExecuteAddToTodoCategories, 
            CanAddToTodoCategories
        );
    }
    
    /// <summary>
    /// Adds the selected note tree category to TodoPlugin categories
    /// </summary>
    private async Task ExecuteAddToTodoCategories(object parameter)
    {
        try
        {
            // Extract CategoryViewModel from parameter
            var categoryViewModel = parameter as CategoryViewModel;
            if (categoryViewModel == null)
            {
                _logger.Warning("[CategoryOps] AddToTodoCategories called without CategoryViewModel");
                return;
            }
            
            _logger.Info($"[CategoryOps] Adding category to todos: {categoryViewModel.Name}");
            
            // Get TodoPlugin's CategoryStore
            var todoCategoryStore = _serviceProvider.GetService<
                NoteNest.UI.Plugins.TodoPlugin.Services.ICategoryStore>();
            
            if (todoCategoryStore == null)
            {
                _logger.Warning("[CategoryOps] TodoPlugin CategoryStore not available");
                await _dialogService.ShowMessageAsync(
                    "Todo Categories", 
                    "Todo plugin is not loaded.");
                return;
            }
            
            // Parse category ID
            if (!Guid.TryParse(categoryViewModel.Id, out var categoryId))
            {
                _logger.Error($"[CategoryOps] Invalid category ID: {categoryViewModel.Id}");
                return;
            }
            
            // Check if already added
            var existing = todoCategoryStore.GetById(categoryId);
            if (existing != null)
            {
                _logger.Info($"[CategoryOps] Category already in todos: {categoryViewModel.Name}");
                await _dialogService.ShowMessageAsync(
                    "Todo Categories",
                    $"'{categoryViewModel.Name}' is already in todo categories.");
                return;
            }
            
            // Add category to TodoPlugin
            var todoCategory = new NoteNest.UI.Plugins.TodoPlugin.Models.Category
            {
                Id = categoryId,
                ParentId = string.IsNullOrEmpty(categoryViewModel.ParentId) 
                    ? null 
                    : Guid.Parse(categoryViewModel.ParentId),
                Name = categoryViewModel.Name,
                Order = 0
            };
            
            todoCategoryStore.Add(todoCategory);
            
            _logger.Info($"✅ Category added to todos: {categoryViewModel.Name}");
            
            // Show success notification
            await _dialogService.ShowMessageAsync(
                "Todo Categories",
                $"'{categoryViewModel.Name}' added to todo categories!");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[CategoryOps] Failed to add category to todos");
            await _dialogService.ShowMessageAsync(
                "Error",
                $"Failed to add category: {ex.Message}");
        }
    }
    
    private bool CanAddToTodoCategories(object parameter)
    {
        // Can add if parameter is CategoryViewModel
        return parameter is CategoryViewModel;
    }
}
```

**Design Patterns:**
- Command Pattern (ICommand, AsyncRelayCommand)
- Service Locator (get TodoPlugin services via IServiceProvider)
- Validation (check if already added, valid ID, etc.)
- User feedback (success/error dialogs)

---

#### **Step 2.2: Update Context Menu XAML (30 min)**

**Location:** `NoteNest.UI/Views/CategoryTreeView.xaml` (or wherever context menu is defined)

**Add Menu Item:**
```xml
<ContextMenu x:Key="CategoryContextMenu">
    <!-- Existing menu items -->
    <MenuItem Header="New Folder..." 
              Command="{Binding CreateCategoryCommand}" 
              CommandParameter="{Binding}"/>
    <MenuItem Header="Rename..." 
              Command="{Binding RenameCategoryCommand}" 
              CommandParameter="{Binding}"/>
    <MenuItem Header="Delete" 
              Command="{Binding DeleteCategoryCommand}" 
              CommandParameter="{Binding}"/>
    <Separator/>
    
    <!-- NEW: Add to Todo Categories -->
    <MenuItem Header="Add to Todo Categories" 
              Command="{Binding Path=DataContext.CategoryOperations.AddToTodoCategoriesCommand, 
                                RelativeSource={RelativeSource AncestorType=TreeView}}" 
              CommandParameter="{Binding}">
        <MenuItem.Icon>
            <ContentControl Template="{StaticResource LucideCheck}" 
                            Width="14" Height="14"/>
        </MenuItem.Icon>
    </MenuItem>
</ContextMenu>
```

**Binding Explanation:**
- `Path=DataContext.CategoryOperations.AddToTodoCategoriesCommand`
  - Navigates to MainShellViewModel.CategoryOperations.AddToTodoCategoriesCommand
- `RelativeSource={RelativeSource AncestorType=TreeView}`
  - Finds the TreeView's DataContext (MainShellViewModel)
- `CommandParameter="{Binding}"`
  - Passes CategoryViewModel as parameter

**Alternative (if context menu is defined differently):**
```xml
<TreeView.Resources>
    <ContextMenu x:Key="CategoryItemContextMenu" 
                 DataContext="{Binding PlacementTarget.DataContext, 
                               RelativeSource={RelativeSource Self}}">
        <MenuItem Header="Add to Todo Categories"
                  Command="{Binding AddToTodoCategoriesCommand}"/>
    </ContextMenu>
</TreeView.Resources>

<Style TargetType="TreeViewItem">
    <Setter Property="ContextMenu" 
            Value="{StaticResource CategoryItemContextMenu}"/>
</Style>
```

---

#### **Step 2.3: Handle Category Refresh (30 min)**

**When to Refresh Todo Categories:**

**Scenario 1: User creates new folder in note tree**
```csharp
// In CategoryOperationsViewModel.ExecuteCreateCategory
private async Task ExecuteCreateCategory(object parameter)
{
    // ... create category in tree ...
    
    // NEW: Notify TodoPlugin to refresh categories
    var todoCategoryStore = _serviceProvider.GetService<ICategoryStore>();
    if (todoCategoryStore != null)
    {
        await todoCategoryStore.RefreshAsync();
        _logger.Info("[CategoryOps] Refreshed todo categories after create");
    }
}
```

**Scenario 2: User renames folder**
```csharp
// In CategoryOperationsViewModel.ExecuteRenameCategory
private async Task ExecuteRenameCategory(object parameter)
{
    // ... rename category in tree ...
    
    // NEW: Refresh todo categories (name updated)
    var todoCategoryStore = _serviceProvider.GetService<ICategoryStore>();
    if (todoCategoryStore != null)
    {
        await todoCategoryStore.RefreshAsync();
    }
}
```

**Scenario 3: User deletes folder**
```csharp
// In CategoryOperationsViewModel.ExecuteDeleteCategory
private async Task ExecuteDeleteCategory(object parameter)
{
    // ... delete category in tree ...
    
    // NEW: Remove from todo categories
    var todoCategoryStore = _serviceProvider.GetService<ICategoryStore>();
    if (todoCategoryStore != null)
    {
        // Option A: Remove from todos (todos become uncategorized)
        todoCategoryStore.Delete(categoryId);
        
        // Option B: Keep in todos but mark as "Deleted Category"
        // This preserves todos even if folder deleted
    }
}
```

**Design Pattern:**
- Observer Pattern (category changes trigger todo category updates)
- Event-driven Architecture
- Loose coupling (TodoPlugin doesn't know about main app changes)

---

### **PHASE 3: RTF Auto-Categorization (1 hour)**

#### **Step 3.1: Update TodoSyncService (45 min)**

**Location:** `NoteNest.UI/Plugins/TodoPlugin/Infrastructure/Sync/TodoSyncService.cs`

**Find the ReconcileTodosAsync method and update:**

```csharp
private async Task ReconcileTodosAsync(Guid noteGuid, string filePath, List<TodoCandidate> candidates)
{
    try
    {
        // STEP 1: Get the source note to determine category
        Guid? noteCategoryId = null;
        
        // Query tree to find note's parent category
        var noteNode = await _treeRepository.GetNodeByIdAsync(noteGuid);
        if (noteNode != null && noteNode.ParentId.HasValue)
        {
            noteCategoryId = noteNode.ParentId.Value;
            _logger.Debug($"[TodoSync] Note is in category: {noteCategoryId}");
        }
        
        // STEP 2: Get existing todos for this note
        var existingTodos = await _repository.GetByNoteIdAsync(noteGuid);
        
        // STEP 3: Build stable ID map for reconciliation
        var existingMap = existingTodos.ToDictionary(
            t => $"{t.SourceLineNumber}:{t.Text.GetHashCode():X8}",
            t => t
        );
        
        var seenIds = new HashSet<string>();
        
        // STEP 4: Process each extracted candidate
        foreach (var candidate in candidates)
        {
            var stableId = candidate.GetStableId();
            seenIds.Add(stableId);
            
            if (!existingMap.ContainsKey(stableId))
            {
                // NEW TODO: Create and auto-categorize
                var newTodo = new TodoItem
                {
                    Text = candidate.Text,
                    CategoryId = noteCategoryId,  // ← AUTO-CATEGORIZE!
                    SourceNoteId = noteGuid,
                    SourceFilePath = filePath,
                    SourceLineNumber = candidate.LineNumber,
                    SourceCharOffset = candidate.CharacterOffset,
                    IsOrphaned = false
                };
                
                await _repository.InsertAsync(newTodo);
                _logger.Info($"[TodoSync] ✅ Created todo from note [{candidate.Text}] under category {noteCategoryId}");
            }
            else
            {
                // EXISTING TODO: Update last seen timestamp
                var existing = existingMap[stableId];
                // Mark as seen (not orphaned)
                if (existing.IsOrphaned)
                {
                    existing.IsOrphaned = false;
                    await _repository.UpdateAsync(existing);
                    _logger.Info($"[TodoSync] Restored orphaned todo: {existing.Text}");
                }
            }
        }
        
        // STEP 5: Mark todos not found as orphaned
        foreach (var todo in existingTodos)
        {
            var stableId = $"{todo.SourceLineNumber}:{todo.Text.GetHashCode():X8}";
            if (!seenIds.Contains(stableId) && !todo.IsOrphaned)
            {
                todo.IsOrphaned = true;
                await _repository.UpdateAsync(todo);
                _logger.Info($"[TodoSync] ⚠️ Marked todo as orphaned: {todo.Text}");
            }
        }
        
        _logger.Info($"[TodoSync] Reconciliation complete: {candidates.Count} in note, {existingTodos.Count} in database");
    }
    catch (Exception ex)
    {
        _logger.Error(ex, "[TodoSync] Reconciliation failed");
    }
}
```

**Key Changes:**
1. Query `ITreeDatabaseRepository` to get note's parent category
2. Set `CategoryId = noteNode.ParentId` when creating todo
3. Todos automatically organized by note's location

**Dependency Injection:**
```csharp
public class TodoSyncService : IHostedService
{
    private readonly ITreeDatabaseRepository _treeRepository;  // ← ADD THIS
    
    public TodoSyncService(
        ISaveManager saveManager,
        ITodoRepository repository,
        BracketTodoParser parser,
        ITreeDatabaseRepository treeRepository,  // ← ADD THIS
        IAppLogger logger)
    {
        _treeRepository = treeRepository ?? throw new ArgumentNullException(nameof(treeRepository));
        // ... rest of constructor
    }
}
```

**Update DI Registration:**
```csharp
// In PluginSystemConfiguration.cs
services.AddHostedService<TodoSyncService>(provider =>
    new TodoSyncService(
        provider.GetRequiredService<ISaveManager>(),
        provider.GetRequiredService<ITodoRepository>(),
        provider.GetRequiredService<BracketTodoParser>(),
        provider.GetRequiredService<ITreeDatabaseRepository>(),  // ← ADD THIS
        provider.GetRequiredService<IAppLogger>()
    ));
```

---

### **PHASE 4: UI Enhancements (1 hour)**

#### **Step 4.1: Show Category Path in Todo Item (30 min)**

**Location:** `NoteNest.UI/Plugins/TodoPlugin/UI/ViewModels/TodoItemViewModel.cs`

**Add Property:**
```csharp
public class TodoItemViewModel : ViewModelBase
{
    private readonly TodoItem _todo;
    private readonly ICategorySyncService _categorySync;  // ← Inject
    
    // NEW: Display category breadcrumb
    public string CategoryPath
    {
        get
        {
            if (_todo.CategoryId == null)
                return string.Empty;
                
            // Get category from sync service
            var category = _categorySync.GetCategoryByIdAsync(_todo.CategoryId.Value).Result;
            if (category == null)
                return string.Empty;
                
            // Build breadcrumb (e.g., "Work > Projects > ProjectA")
            return BuildCategoryBreadcrumb(category);
        }
    }
    
    private string BuildCategoryBreadcrumb(Category category)
    {
        var parts = new List<string>();
        var current = category;
        
        while (current != null && parts.Count < 5) // Max 5 levels
        {
            parts.Insert(0, current.Name);
            
            if (current.ParentId.HasValue)
                current = _categorySync.GetCategoryByIdAsync(current.ParentId.Value).Result;
            else
                break;
        }
        
        return string.Join(" > ", parts);
    }
}
```

**Update XAML:**
```xml
<TextBlock Text="{Binding CategoryPath}"
           FontSize="11"
           Foreground="{DynamicResource SystemControlForegroundBaseMediumBrush}"
           Margin="0,2,0,0">
    <TextBlock.Style>
        <Style TargetType="TextBlock">
            <Style.Triggers>
                <DataTrigger Binding="{Binding CategoryPath}" Value="">
                    <Setter Property="Visibility" Value="Collapsed"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </TextBlock.Style>
</TextBlock>
```

---

#### **Step 4.2: Category Filter UI (30 min)**

**Location:** Update `TodoPanelView.xaml`

**Add Category Tree (Collapsible):**
```xml
<Expander Grid.Row="0" Header="Categories" IsExpanded="True">
    <TreeView ItemsSource="{Binding CategoryTreeViewModel.Categories}"
              SelectedItem="{Binding SelectedCategory, Mode=TwoWay}"
              MaxHeight="200">
        <TreeView.ItemTemplate>
            <HierarchicalDataTemplate ItemsSource="{Binding Children}">
                <StackPanel Orientation="Horizontal">
                    <ContentControl Template="{StaticResource LucideFolder}"
                                    Width="14" Height="14" Margin="0,0,4,0"/>
                    <TextBlock Text="{Binding Name}"/>
                    <TextBlock Text="{Binding TodoCount, StringFormat=' ({0})'}"
                               Foreground="{DynamicResource SystemControlForegroundBaseMediumBrush}"/>
                </StackPanel>
            </HierarchicalDataTemplate>
        </TreeView.ItemTemplate>
    </TreeView>
</Expander>
```

---

### **PHASE 5: Testing & Validation (2 hours)**

#### **Step 5.1: Unit Tests (1 hour)**

**Location:** `NoteNest.Tests/Plugins/TodoPlugin/Services/CategorySyncServiceTests.cs`

```csharp
[TestFixture]
public class CategorySyncServiceTests
{
    private Mock<ITreeDatabaseRepository> _mockTreeRepo;
    private Mock<IAppLogger> _mockLogger;
    private CategorySyncService _syncService;
    
    [SetUp]
    public void Setup()
    {
        _mockTreeRepo = new Mock<ITreeDatabaseRepository>();
        _mockLogger = new Mock<IAppLogger>();
        _syncService = new CategorySyncService(_mockTreeRepo.Object, _mockLogger.Object);
    }
    
    [Test]
    public async Task GetAllCategoriesAsync_ShouldReturnOnlyCategories()
    {
        // Arrange
        var treeNodes = new List<TreeNode>
        {
            CreateCategoryNode(Guid.NewGuid(), "Work"),
            CreateCategoryNode(Guid.NewGuid(), "Personal"),
            CreateNoteNode(Guid.NewGuid(), "meeting.rtf")  // Should be filtered out
        };
        
        _mockTreeRepo.Setup(r => r.GetAllNodesAsync(false))
            .ReturnsAsync(treeNodes);
        
        // Act
        var categories = await _syncService.GetAllCategoriesAsync();
        
        // Assert
        Assert.AreEqual(2, categories.Count);
        Assert.IsTrue(categories.All(c => c.Name == "Work" || c.Name == "Personal"));
    }
    
    [Test]
    public async Task GetCategoryByIdAsync_WithValidId_ShouldReturnCategory()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var treeNode = CreateCategoryNode(categoryId, "Test");
        
        _mockTreeRepo.Setup(r => r.GetNodeByIdAsync(categoryId))
            .ReturnsAsync(treeNode);
        
        // Act
        var category = await _syncService.GetCategoryByIdAsync(categoryId);
        
        // Assert
        Assert.IsNotNull(category);
        Assert.AreEqual("Test", category.Name);
    }
    
    private TreeNode CreateCategoryNode(Guid id, string name)
    {
        return TreeNode.CreateFromDatabase(
            id: id,
            parentId: null,
            canonicalPath: name.ToLower(),
            displayPath: name,
            absolutePath: $"C:\\Notes\\{name}",
            nodeType: TreeNodeType.Category,
            name: name
        );
    }
}
```

---

#### **Step 5.2: Integration Tests (1 hour)**

**Test Scenario: End-to-End Category Sync**

```csharp
[TestFixture]
public class CategorySyncIntegrationTests
{
    private string _treeDbPath;
    private string _todoDbPath;
    private ITreeDatabaseRepository _treeRepo;
    private ITodoRepository _todoRepo;
    private CategorySyncService _syncService;
    
    [SetUp]
    public async Task Setup()
    {
        // Create temp databases
        _treeDbPath = Path.Combine(Path.GetTempPath(), $"tree_{Guid.NewGuid()}.db");
        _todoDbPath = Path.Combine(Path.GetTempPath(), $"todo_{Guid.NewGuid()}.db");
        
        // Initialize repositories
        var logger = new MockLogger();
        var treeInit = new TreeDatabaseInitializer(_treeDbPath, logger);
        await treeInit.InitializeAsync();
        
        var todoInit = new TodoDatabaseInitializer(_todoDbPath, logger);
        await todoInit.InitializeAsync();
        
        _treeRepo = new TreeDatabaseRepository(_treeDbPath, logger);
        _todoRepo = new TodoRepository(_todoDbPath, logger);
        _syncService = new CategorySyncService(_treeRepo, logger);
    }
    
    [Test]
    public async Task CategoryCreatedInTree_ShouldBeAvailableForTodos()
    {
        // Arrange: Create category in tree
        var category = TreeNode.CreateCategory("C:\\Notes\\Work", "C:\\Notes");
        await _treeRepo.InsertNodeAsync(category);
        
        // Act: Sync to todo categories
        var todoCategories = await _syncService.GetAllCategoriesAsync();
        
        // Assert
        Assert.AreEqual(1, todoCategories.Count);
        Assert.AreEqual("Work", todoCategories[0].Name);
        Assert.AreEqual(category.Id, todoCategories[0].Id);
    }
    
    [Test]
    public async Task TodoExtractedFromNote_ShouldInheritCategory()
    {
        // Arrange: Create category and note in tree
        var category = TreeNode.CreateCategory("C:\\Notes\\Work", "C:\\Notes");
        await _treeRepo.InsertNodeAsync(category);
        
        var note = TreeNode.CreateNote("C:\\Notes\\Work\\meeting.rtf", "C:\\Notes", category.Id);
        await _treeRepo.InsertNodeAsync(note);
        
        // Act: Extract todo from note
        var parser = new BracketTodoParser(new MockLogger());
        var rtfContent = "{\\rtf1 Meeting notes [call John] }";
        var candidates = parser.ExtractFromRtf(rtfContent);
        
        // Create todo with note's category
        var todo = new TodoItem
        {
            Text = candidates[0].Text,
            CategoryId = note.ParentId,  // ← Should be category.Id
            SourceNoteId = note.Id
        };
        
        await _todoRepo.InsertAsync(todo);
        
        // Assert
        var retrieved = await _todoRepo.GetByIdAsync(todo.Id);
        Assert.AreEqual(category.Id, retrieved.CategoryId);
    }
}
```

---

## 🎯 IMPLEMENTATION CHECKLIST

### **Files to Create (3 new):**
```
✅ Services/CategorySyncService.cs           (200 lines)
✅ Tests/Services/CategorySyncServiceTests.cs (150 lines)
✅ Tests/Integration/CategorySyncTests.cs     (100 lines)
```

### **Files to Modify (5 existing):**
```
✅ Services/CategoryStore.cs                  (add InitializeAsync)
✅ Services/ICategoryStore.cs                 (add interface methods)
✅ Infrastructure/Sync/TodoSyncService.cs     (auto-categorize)
✅ ViewModels/CategoryOperationsViewModel.cs  (context menu command)
✅ Views/CategoryTreeView.xaml                (context menu item)
✅ Composition/PluginSystemConfiguration.cs   (DI registration)
✅ ViewModels/Shell/MainShellViewModel.cs     (initialize CategoryStore)
```

### **Dependencies to Add:**
```
TodoSyncService constructor:
└── ITreeDatabaseRepository  (query note tree)

CategoryStore constructor:
└── ICategorySyncService  (sync with tree)

CategoryOperationsViewModel:
└── IServiceProvider (access TodoPlugin services)
```

---

## 📐 ARCHITECTURE DIAGRAMS

### **Data Flow: Context Menu → Todo Category**

```
User Right-Clicks Category "ProjectA" in Note Tree
    ↓
CategoryViewModel (Note Tree)
    ↓
Context Menu → AddToTodoCategoriesCommand
    ↓
CategoryOperationsViewModel.ExecuteAddToTodoCategories()
    ↓
Gets TodoPlugin's CategoryStore via IServiceProvider
    ↓
Creates Category object (same ID as tree node!)
    ↓
CategoryStore.Add(category)
    ↓
TodoPlugin Category Tree Updates (ObservableCollection)
    ↓
UI Shows "ProjectA" in Todo Panel ✅
```

---

### **Data Flow: RTF Extraction → Auto-Categorization**

```
User Saves Note: Work/Projects/ProjectA/meeting.rtf
    ↓
ISaveManager.NoteSaved event fired
    ↓
TodoSyncService.OnNoteSaved() triggered
    ↓
BracketTodoParser.ExtractFromRtf() → finds "[call client]"
    ↓
Query ITreeDatabaseRepository.GetNodeByIdAsync(noteId)
    ↓
TreeNode.ParentId = ProjectA's Guid
    ↓
Create TodoItem with CategoryId = ProjectA
    ↓
TodoRepository.InsertAsync(todo)
    ↓
Todo appears under "ProjectA" category ✅
```

---

## 🔧 TECHNICAL DETAILS

### **Database Relationships:**

```
tree.db (Main App):
├── tree_nodes
│   ├── id = "guid-work"        (node_type='category', name='Work')
│   └── id = "guid-projecta"    (node_type='category', name='ProjectA', parent_id='guid-work')
│   └── id = "guid-meeting"     (node_type='note', name='meeting.rtf', parent_id='guid-projecta')

todos.db (TodoPlugin):
├── todos
│   └── id = "guid-todo-1"
│       ├── text = "call client"
│       ├── category_id = "guid-projecta"  ← Links to tree_nodes!
│       └── source_note_id = "guid-meeting"  ← Links to source note!
```

**Foreign Key Relationship (Informational):**
- `todos.category_id` references `tree_nodes.id`
- NOT enforced by database (different files)
- Application-level integrity

---

### **Category ID Synchronization:**

**Critical:** Use **same Guid** for category in both systems!

```csharp
// Note Tree: Category has Guid "550e8400-e29b-41d4-a716-446655440000"
var treeNode = new TreeNode { Id = Guid.Parse("550e8400-...") };

// Todo Category: Use SAME Guid!
var todoCategory = new Category { Id = Guid.Parse("550e8400-...") };

// Todo: Links to category via same Guid
var todo = new TodoItem { CategoryId = Guid.Parse("550e8400-...") };

// Lookup works: todos.category_id == tree_nodes.id ✅
```

**Why:**
- ✅ Single source of truth (tree database)
- ✅ Todos reference actual folders
- ✅ Category rename in tree → Updates everywhere
- ✅ No duplication or drift

---

## 🎯 DESIGN PATTERNS & BEST PRACTICES

### **1. Repository Pattern**
```csharp
ICategorySyncService
└── Abstracts tree database access
└── Testable (can mock)
└── Clean separation
```

### **2. Dependency Injection**
```csharp
services.AddSingleton<ICategorySyncService, CategorySyncService>();
└── Loose coupling
└── Testable
└── Configurable
```

### **3. Observer Pattern**
```csharp
CategoryStore uses ObservableCollection
└── UI auto-updates when categories change
└── No manual refresh needed
```

### **4. Command Pattern**
```csharp
AddToTodoCategoriesCommand
└── Encapsulates action
└── Supports undo/redo (future)
└── UI binding friendly
```

### **5. Async/Await Throughout**
```csharp
async Task InitializeAsync()
└── Non-blocking UI
└── Responsive application
└── Best practice for I/O
```

### **6. Error Handling**
```csharp
try-catch with logging
└── Graceful degradation
└── User-friendly errors
└── Application stays stable
```

### **7. Validation**
```csharp
Check if category exists
Check if already added
Validate Guid parsing
└── Defensive programming
└── Prevents invalid state
```

---

## 🧪 TESTING STRATEGY

### **Unit Tests (2 hours):**
```
CategorySyncServiceTests:
├── GetAllCategoriesAsync_ReturnsOnlyCategories
├── GetCategoryByIdAsync_WithValidId
├── GetRootCategoriesAsync_ReturnsRoots
└── IsCategoryInTreeAsync_ValidatesExistence

TodoSyncServiceTests:
├── ExtractTodo_AutoCategorizes
├── ExtractTodo_WithoutCategory_HandlesGracefully
└── ReconcileTodos_UpdatesExisting
```

### **Integration Tests (2 hours):**
```
CategorySyncIntegrationTests:
├── CategoryInTree_AvailableForTodos
├── ExtractedTodo_InheritsNoteCategory
└── CategoryDeleted_TodosHandledGracefully
```

### **Manual Testing (30 min):**
```
Test 1: Context Menu
├── Right-click category in note tree
├── Click "Add to Todo Categories"
└── ✅ Verify appears in todo panel

Test 2: RTF Extraction
├── Create note in Work/ProjectA
├── Type "[call John]"
├── Save note
└── ✅ Verify todo under ProjectA category

Test 3: Category Rename
├── Rename category in note tree
├── Refresh todo panel
└── ✅ Verify name updated in todos
```

---

## 📊 EDGE CASES TO HANDLE

### **1. Category Doesn't Exist in Tree**
```csharp
// Todo has category_id but category deleted
if (todo.CategoryId.HasValue)
{
    var exists = await _categorySync.IsCategoryInTreeAsync(todo.CategoryId.Value);
    if (!exists)
    {
        // Option A: Move to "Uncategorized"
        // Option B: Keep showing with "(Deleted)" indicator
        // Option C: Keep as-is (orphaned category)
    }
}
```

**Recommendation:** Option B (show with indicator)

---

### **2. Note Has No Parent Category**
```csharp
// Note at root level (no folder)
var note = await _treeRepository.GetNodeByIdAsync(noteId);
if (note.ParentId == null)
{
    // Extract todo without category
    var todo = new TodoItem
    {
        Text = bracketText,
        CategoryId = null,  // ← Uncategorized
        SourceNoteId = note.Id
    };
}
```

**Recommendation:** Allow uncategorized todos

---

### **3. User Deletes Category from Tree**
```csharp
// Handle in CategoryOperationsViewModel
private async Task ExecuteDeleteCategory(object parameter)
{
    // ... delete from tree ...
    
    // Notify user about todos
    var todoCount = await _todoRepo.GetByCategoryAsync(categoryId);
    if (todoCount.Any())
    {
        var result = await _dialogService.ShowConfirmAsync(
            "Delete Category",
            $"{todoCount.Count} todos are in this category. What should happen to them?",
            new[] 
            { 
                "Keep Uncategorized", 
                "Delete Todos", 
                "Cancel" 
            }
        );
        
        // Handle based on user choice
    }
}
```

---

### **4. Same Category Added Multiple Times**
```csharp
// In ExecuteAddToTodoCategories
var existing = todoCategoryStore.GetById(categoryId);
if (existing != null)
{
    await _dialogService.ShowMessageAsync(
        "Category already added to todos");
    return; // Don't add duplicate
}
```

---

## 🎯 UX FLOW EXAMPLES

### **Example 1: PM Organizing Project**

**User Workflow:**
```
1. User has folder structure:
   Work/
   └── Projects/
       ├── ProjectA/
       ├── ProjectB/
       └── ProjectC/

2. User right-clicks "ProjectA" → "Add to Todo Categories"
   → ProjectA appears in Todo panel

3. User adds manual todo: "Review requirements"
   → Selects ProjectA category
   → Todo organized under ProjectA ✅

4. User opens note: ProjectA/kickoff.rtf
   → Types "[send agenda to team]"
   → Saves note
   → Todo auto-appears under ProjectA ✅

5. Both manual and extracted todos organized together! ✅
```

---

### **Example 2: Budget Tracking**

```
1. User has: Personal/Finance/2024-Budget/
2. Adds to todo categories via context menu
3. Extracted todos from budget notes:
   - "[call bank about rates]"
   - "[review credit card statement]"
   → All auto-categorized under "2024-Budget" ✅
```

---

## 🏗️ ARCHITECTURAL EXCELLENCE

### **Clean Architecture Maintained:**

```
UI Layer (TodoPanelView)
    ↓
ViewModel Layer (CategoryTreeViewModel)
    ↓
Service Layer (CategorySyncService, CategoryStore)
    ↓
Repository Layer (ITreeDatabaseRepository)
    ↓
Database Layer (tree.db SQLite)
```

**Separation of Concerns:**
- ✅ UI doesn't know about database
- ✅ Services don't know about ViewModels
- ✅ Repository isolates data access
- ✅ Each layer has single responsibility

---

### **Design Principles Applied:**

**SOLID:**
- ✅ **S**ingle Responsibility (each class one job)
- ✅ **O**pen/Closed (extend without modifying)
- ✅ **L**iskov Substitution (interfaces)
- ✅ **I**nterface Segregation (focused interfaces)
- ✅ **D**ependency Inversion (depend on abstractions)

**DRY:**
- ✅ TreeNode → Category conversion in one place
- ✅ No duplicate category data

**KISS:**
- ✅ Simple query, simple conversion
- ✅ No over-engineering

**YAGNI:**
- ✅ Only implement what's needed
- ✅ Categories from tree (not separate system)

---

## 📊 PERFORMANCE CONSIDERATIONS

### **Optimization 1: Caching**

```csharp
public class CategorySyncService
{
    private List<Category>? _cachedCategories;
    private DateTime? _cacheTime;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);
    
    public async Task<List<Category>> GetAllCategoriesAsync()
    {
        // Check cache
        if (_cachedCategories != null && 
            _cacheTime.HasValue && 
            DateTime.Now - _cacheTime.Value < _cacheExpiration)
        {
            return _cachedCategories;
        }
        
        // Query database
        var categories = await QueryTreeDatabase();
        
        // Update cache
        _cachedCategories = categories;
        _cacheTime = DateTime.Now;
        
        return categories;
    }
    
    public void InvalidateCache()
    {
        _cachedCategories = null;
        _cacheTime = null;
    }
}
```

---

### **Optimization 2: Lazy Loading**

```csharp
// Don't load all categories on startup
// Load when Todo panel opened

public async Task OnTodoPanelOpened()
{
    if (!_categoryStore._isInitialized)
    {
        await _categoryStore.InitializeAsync();  // First time only
    }
}
```

---

### **Optimization 3: Batch Updates**

```csharp
// When multiple categories change
using (_categories.BatchUpdate())
{
    _categories.Clear();
    _categories.AddRange(newCategories);
}
// UI updates once (not per item)
```

---

## 🎯 ROBUSTNESS & ERROR HANDLING

### **Graceful Degradation:**

```csharp
// If tree database unavailable
public async Task<List<Category>> GetAllCategoriesAsync()
{
    try
    {
        return await _treeRepository.GetAllNodesAsync();
    }
    catch (Exception ex)
    {
        _logger.Error(ex, "Failed to load categories");
        return new List<Category>(); // Empty list, not crash
    }
}
```

---

### **Validation at Every Step:**

```csharp
// Before adding category to todos
private async Task ExecuteAddToTodoCategories(object parameter)
{
    // Validate parameter
    if (parameter is not CategoryViewModel categoryVm)
        return;
        
    // Validate ID format
    if (!Guid.TryParse(categoryVm.Id, out var categoryId))
        return;
        
    // Validate category exists in tree
    var exists = await _categorySync.IsCategoryInTreeAsync(categoryId);
    if (!exists)
    {
        await _dialogService.ShowMessageAsync("Category no longer exists");
        return;
    }
    
    // Validate not already added
    if (todoCategoryStore.GetById(categoryId) != null)
    {
        await _dialogService.ShowMessageAsync("Already added");
        return;
    }
    
    // NOW add (all validation passed)
    todoCategoryStore.Add(category);
}
```

---

### **Consistency Checks:**

```csharp
// Periodically verify category IDs still valid
public async Task ValidateCategoriesAsync()
{
    var todoCategories = _categoryStore.Categories;
    var invalidCategories = new List<Guid>();
    
    foreach (var category in todoCategories)
    {
        var exists = await _categorySync.IsCategoryInTreeAsync(category.Id);
        if (!exists)
        {
            invalidCategories.Add(category.Id);
            _logger.Warning($"Category no longer in tree: {category.Name}");
        }
    }
    
    // Mark todos in invalid categories as uncategorized
    foreach (var invalidId in invalidCategories)
    {
        var todos = await _todoRepo.GetByCategoryAsync(invalidId);
        foreach (var todo in todos)
        {
            todo.CategoryId = null;
            await _todoRepo.UpdateAsync(todo);
        }
    }
}
```

---

## 📝 IMPLEMENTATION ORDER

### **Day 1 (3-4 hours):**

**Morning (2 hours):**
1. Create `CategorySyncService.cs`
2. Update `ICategoryStore` interface
3. Update `CategoryStore.cs` implementation
4. Register in DI container

**Afternoon (2 hours):**
5. Add `AddToTodoCategoriesCommand`
6. Update context menu XAML
7. Update `TodoSyncService` auto-categorization
8. Initialize CategoryStore in MainShellViewModel

**Deliverable:** Category sync working

---

### **Day 2 (2 hours):**

**Testing:**
1. Unit tests (CategorySyncService)
2. Integration tests (end-to-end)
3. Manual testing (user workflows)

**Deliverable:** Tested and validated

---

### **Day 3 (1 hour):**

**Polish:**
1. Error handling refinement
2. User feedback (notifications)
3. Documentation

**Deliverable:** Production-ready

---

## 🎯 SUCCESS CRITERIA

### **Must Work:**
- ✅ Right-click category → "Add to Todo Categories" appears
- ✅ Click menu → Category appears in todo panel
- ✅ Create manual todo → Can select synced category
- ✅ Extract `[todo]` from note → Auto-categorizes
- ✅ Categories reflect current tree structure

### **Should Work:**
- ✅ Category renamed in tree → Updates in todos
- ✅ Category deleted in tree → Handled gracefully
- ✅ New category created in tree → Available for todos
- ✅ Performance acceptable (no lag)

### **Nice to Have:**
- ✅ Cache optimization (fast repeated access)
- ✅ Visual indicators (folder icons match)
- ✅ Breadcrumb display (show full path)

---

## ⚠️ POTENTIAL ISSUES & SOLUTIONS

### **Issue 1: Circular Dependency**

**Problem:** CategoryStore needs ITreeDatabaseRepository, both in DI

**Solution:** Use IServiceProvider or interface
```csharp
services.AddSingleton<ICategorySyncService>(provider =>
    new CategorySyncService(
        provider.GetRequiredService<ITreeDatabaseRepository>(),
        provider.GetRequiredService<IAppLogger>()
    ));
```

---

### **Issue 2: Tree Database Not Available**

**Problem:** TodoPlugin initializes before tree database loaded

**Solution:** Lazy initialization
```csharp
public async Task<List<Category>> Categories
{
    get
    {
        if (!_initialized)
            await InitializeAsync();
        return _categories;
    }
}
```

---

### **Issue 3: Context Menu Binding**

**Problem:** Context menu can't access commands in different ViewModel

**Solution:** RelativeSource binding
```xml
Command="{Binding Path=DataContext.CategoryOperations.AddToTodoCategoriesCommand,
                  RelativeSource={RelativeSource AncestorType=Window}}"
```

---

## 🎯 ALTERNATIVE APPROACHES

### **Approach A: Query Tree Live (Recommended)**

**Pros:**
- ✅ Always in sync
- ✅ No storage needed
- ✅ Simpler

**Cons:**
- ⚠️ Query overhead
- ⚠️ Depends on tree database

---

### **Approach B: Copy & Sync**

**Pros:**
- ✅ Independent
- ✅ Can have todo-only categories

**Cons:**
- ❌ Sync complexity
- ❌ Can drift out of sync
- ❌ More storage

---

### **Approach C: Event-Driven Sync**

**Pros:**
- ✅ Real-time updates
- ✅ Efficient

**Cons:**
- ❌ Complex event handling
- ❌ Requires event bus

**Recommendation:** Approach A (query live) for MVP, then optimize if needed

---

## 📋 CODE REVIEW CHECKLIST

Before considering implementation complete:

**Architecture:**
- [ ] Follows existing patterns (Repository, Service, ViewModel)
- [ ] Clean separation of concerns
- [ ] Dependency injection used correctly
- [ ] Async/await throughout

**Code Quality:**
- [ ] Null checks and validation
- [ ] Error handling with logging
- [ ] XML documentation comments
- [ ] Consistent naming conventions

**Testing:**
- [ ] Unit tests for service logic
- [ ] Integration tests for database
- [ ] Manual test cases documented

**User Experience:**
- [ ] Context menu appears correctly
- [ ] Categories load without lag
- [ ] Error messages user-friendly
- [ ] No crashes or exceptions

**Performance:**
- [ ] No unnecessary database queries
- [ ] Batch updates for collections
- [ ] Caching where appropriate
- [ ] Async operations don't block UI

---

## 🎯 FINAL DELIVERABLES

### **Code Files (8 modified/created):**
```
NEW:
├── Services/CategorySyncService.cs
├── Services/ICategorySyncService.cs
├── Tests/CategorySyncServiceTests.cs
└── Tests/CategorySyncIntegrationTests.cs

MODIFIED:
├── Services/CategoryStore.cs
├── Services/ICategoryStore.cs
├── Infrastructure/Sync/TodoSyncService.cs
├── ViewModels/CategoryOperationsViewModel.cs
├── Views/CategoryTreeView.xaml
├── Composition/PluginSystemConfiguration.cs
└── ViewModels/Shell/MainShellViewModel.cs
```

### **Documentation:**
```
├── Implementation guide (this document)
├── Test plan
├── User guide (context menu usage)
└── Architecture diagrams
```

---

## ✅ ESTIMATED EFFORT

| Phase | Time | Complexity | Value |
|-------|------|------------|-------|
| **CategorySyncService** | 1 hr | Medium | High |
| **CategoryStore Update** | 30 min | Low | High |
| **DI Registration** | 15 min | Low | Required |
| **Context Menu Command** | 1 hr | Medium | High |
| **Context Menu XAML** | 30 min | Low | Required |
| **Auto-Categorization** | 1 hr | Medium | High |
| **Testing** | 2 hrs | Medium | High |
| **Polish & Docs** | 1 hr | Low | Medium |

**Total:** 6-7 hours for complete implementation ✅

---

## 🚀 GETTING STARTED (For New Chat)

### **Pre-requisites Understanding:**

1. **Read existing code:**
   - `NoteNest.Infrastructure/Database/TreeDatabaseRepository.cs`
   - `NoteNest.UI/Plugins/TodoPlugin/Services/CategoryStore.cs`
   - `NoteNest.UI/Plugins/TodoPlugin/Infrastructure/Sync/TodoSyncService.cs`

2. **Understand patterns:**
   - Clean Architecture (Domain/Application/Infrastructure/UI)
   - Repository Pattern (data access)
   - MVVM Pattern (ViewModels, Commands)
   - Dependency Injection (constructor injection)

3. **Verify dependencies:**
   - ITreeDatabaseRepository available
   - MediatR configured
   - DialogService available

---

### **Implementation Steps:**

**Step 1:** Create `CategorySyncService` (query tree database)  
**Step 2:** Update `CategoryStore` (use CategorySyncService)  
**Step 3:** Register in DI  
**Step 4:** Add context menu command  
**Step 5:** Update TodoSyncService (auto-categorize)  
**Step 6:** Test thoroughly  
**Step 7:** Polish UX

---

## ✅ CONFIDENCE ASSESSMENT

**Implementation Confidence:** 95%

**Why High:**
- ✅ Clear architecture (matches existing patterns)
- ✅ Dependencies available (ITreeDatabaseRepository exists)
- ✅ Patterns proven (same as main app)
- ✅ Isolated scope (TodoPlugin only)
- ✅ Can test incrementally

**Risks:**
- Context menu binding (5% - might need debugging)
- TreeNode query performance (minimal risk)

**Mitigation:**
- Follow existing context menu patterns
- Add caching if needed
- Test with large category trees

---

## 🎯 NEXT STEPS AFTER IMPLEMENTATION

**After Category Sync Works:**

1. ✅ Test with real project structure
2. ✅ Collect user feedback
3. 🤔 Consider: Do users need tagging BEYOND folders?
4. 🤔 If yes: Implement tagging system (6 weeks)
5. 🤔 If no: Category sync sufficient! ✅

**Timeline:**
```
Week 1: Implement category sync (6-7 hours)
Week 2-4: User testing and feedback
Month 2+: Tags (if data shows need)
```

---

## 📋 SUMMARY FOR NEW CHAT

**What's Being Built:**
> Context menu integration allowing users to add note tree categories to TodoPlugin.  
> RTF-extracted todos auto-categorize based on note location.

**Architecture:**
> Clean Architecture, Repository Pattern, MVVM, Dependency Injection, Async/Await

**Existing Infrastructure:**
> - Tree database (tree.db) with category nodes
> - Todo database (todos.db) with category_id field
> - RTF parser + sync service (working)
> - Domain layer (TodoAggregate, value objects)

**What's Needed:**
> - CategorySyncService (query tree)
> - CategoryStore update (dynamic loading)
> - Context menu command
> - Auto-categorization in sync service

**Time:** 6-7 hours  
**Confidence:** 95%  
**Value:** Solves major UX gap ✅

---

**This is a complete, production-ready implementation plan following all existing patterns and best practices.** ✅

