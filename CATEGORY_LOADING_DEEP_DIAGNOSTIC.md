# 🔬 CATEGORY LOADING - DEEP DIAGNOSTIC INVESTIGATION

**Date:** October 18, 2025  
**Issue:** Categories still not loading after ALL fixes  
**Status:** Need deeper investigation  
**Fixes Applied:** Event sourcing + Database migration + CRUD commands  
**Result:** STILL NOT WORKING ❌

---

## 🚨 **CURRENT SITUATION**

### **What We've Fixed:**

1. ✅ Folder tag event sourcing (13 files)
2. ✅ Terminology fix ("folder" → "category")
3. ✅ Tag inheritance system (10 files)
4. ✅ Status notifier DI
5. ✅ Todo category CRUD (event-sourced commands)
6. ✅ CategorySyncService database migration (tree.db → projections.db)

### **What's Still Broken:**

❌ Categories added to TodoPlugin don't persist after app restart

---

## 🔍 **POTENTIAL ROOT CAUSES**

### **Theory 1: Projection Timing Issue** ⚠️

**Hypothesis:** CategoryStore.InitializeAsync() runs BEFORE projections catch up

**Flow:**
```
App Starts
  ↓
Services initialized (DI container)
  ↓
CategoryStore.InitializeAsync() runs IMMEDIATELY ← Too early?
  ↓
Queries tree_view projection
  ↓
Projection might be empty/stale? ← Not caught up yet
  ↓
Validation fails
```

**Investigation Needed:**
- Check startup order in Program.cs / App.xaml.cs
- Check if ProjectionHostedService runs before CategoryStore.InitializeAsync()
- Check if initial projection catch-up happens synchronously

---

### **Theory 2: user_preferences Not Persisting** ⚠️

**Hypothesis:** Data not actually saving to todos.db

**Flow:**
```
Add Category to Todos
  ↓
CategoryStore.AddAsync(category)
  ↓
SaveToDatabaseAsync()
  ↓
INSERT OR REPLACE INTO user_preferences
  ↓
Success logged: "Saved N categories" ✅
  ↓
App Restarts
  ↓
LoadCategoriesAsync()
  ↓
SELECT value FROM user_preferences
  ↓
Returns null/empty? ❌
```

**Investigation Needed:**
- Check logs for "Saved N categories"
- Check logs for "Loaded N categories"  
- Verify todos.db file exists and has user_preferences table
- Query todos.db directly to see if data is there

---

### **Theory 3: Validation Logic Bug** ⚠️

**Hypothesis:** Even with correct database, validation still fails

**Flow:**
```
LoadCategoriesAsync()
  ↓
Returns categories from user_preferences ✅
  ↓
For each category: IsCategoryInTreeAsync(categoryId)
  ↓
GetCategoryByIdAsync(categoryId)
  ↓
GetAllCategoriesAsync()
  ↓
Query tree_view ✅
  ↓
Filter: Where(n => n.NodeType == TreeNodeType.Category)
  ↓
FirstOrDefault(c => c.Id == categoryId)
  ↓
Returns null? ❌ Why?
```

**Possible Issues:**
- TreeNodeType enum mismatch?
- GUID format difference?
- NodeType not set correctly in projection?
- Case sensitivity issue?

**Investigation Needed:**
- Add verbose logging to CategorySyncService
- Log exact GUID being searched
- Log all categories returned from GetAllCategoriesAsync
- Log tree_view query results

---

### **Theory 4: Cache Poisoning** ⚠️

**Hypothesis:** CategorySyncService cache populated BEFORE projection updates

**Flow:**
```
App Starts
  ↓
CategorySyncService created (5-min cache empty)
  ↓
CategoryStore.InitializeAsync() runs
  ↓
Calls GetAllCategoriesAsync() ← First call, populates cache
  ↓
Cache populated with OLD/EMPTY data
  ↓
Projection catches up AFTER cache populated
  ↓
Cache has stale data for 5 minutes
  ↓
Validation uses stale cache
```

**Investigation Needed:**
- Check if cache is invalidated after projection catch-up
- Check timing of cache population vs projection updates
- Add cache invalidation to app startup

---

### **Theory 5: Wrong Connection String** ⚠️

**Hypothesis:** CategoryPersistenceService and CategorySyncService use different databases

**Possible Issue:**
- CategoryPersistenceService uses `todos.db` (correct)
- CategorySyncService NOW uses `projections.db` (correct)
- But maybe connection strings are wrong?

**Investigation Needed:**
- Verify connection string values
- Verify database files exist
- Check if databases are being created correctly

---

### **Theory 6: Projection Not Running** ⚠️

**Hypothesis:** TreeViewProjection not actually processing events

**Possible Issues:**
- Projection not registered correctly
- ProjectionHostedService not started
- Events not being saved to event store
- Event deserialization failing

**Investigation Needed:**
- Check logs for "Projection background service started"
- Check logs for "Processing event: CategoryCreated"
- Query events.db directly to see if events exist
- Query projections.db to see if tree_view is populated

---

## 🎯 **DIAGNOSTIC PLAN**

### **Step 1: Add Comprehensive Logging** 

**Add to CategoryStore.InitializeAsync():**
```csharp
_logger.Info($"[CategoryStore] === DIAGNOSTIC START ===");
_logger.Info($"[CategoryStore] Loading from user_preferences...");
var savedCategories = await _persistenceService.LoadCategoriesAsync();
_logger.Info($"[CategoryStore] Loaded {savedCategories.Count} categories from user_preferences");

foreach (var cat in savedCategories)
{
    _logger.Info($"[CategoryStore] Validating: {cat.Name} (ID: {cat.Id})");
    var stillExists = await _syncService.IsCategoryInTreeAsync(cat.Id);
    _logger.Info($"[CategoryStore] Validation result for {cat.Name}: {stillExists}");
}
```

**Add to CategorySyncService.GetAllCategoriesAsync():**
```csharp
_logger.Info($"[CategorySync] === QUERYING tree_view ===");
var treeNodes = await _treeQueryService.GetAllNodesAsync(includeDeleted: false);
_logger.Info($"[CategorySync] tree_view returned {treeNodes.Count} total nodes");

var categories = treeNodes
    .Where(n => n.NodeType == TreeNodeType.Category)
    .ToList();
    
_logger.Info($"[CategorySync] Filtered to {categories.Count} categories");

foreach (var cat in categories.Take(5))
{
    _logger.Info($"[CategorySync] Category in tree_view: {cat.Name} (ID: {cat.Id})");
}
```

---

### **Step 2: Direct Database Queries**

**Query events.db:**
```sql
SELECT aggregate_id, event_type, timestamp
FROM events
WHERE event_type LIKE '%CategoryCreated%'
ORDER BY timestamp DESC
LIMIT 5;
```

**Query projections.db:**
```sql
SELECT id, name, node_type, parent_id, created_at
FROM tree_view
WHERE node_type = 'category'
ORDER BY created_at DESC
LIMIT 5;
```

**Query todos.db:**
```sql
SELECT key, value, updated_at
FROM user_preferences
WHERE key = 'selected_categories';
```

---

### **Step 3: Verify Projection Registration**

**Check that TreeViewProjection is registered:**
```csharp
// CleanServiceConfiguration.cs line 480-483:
services.AddSingleton<IProjection>(provider =>
    new TreeViewProjection(
        projectionsConnectionString,
        provider.GetRequiredService<IAppLogger>()));
```

**Check that ProjectionOrchestrator is registered:**
```csharp
// CleanServiceConfiguration.cs line 496-501:
services.AddSingleton<ProjectionOrchestrator>(provider =>
    new ProjectionOrchestrator(
        provider.GetRequiredService<IEventStore>(),
        provider.GetServices<IProjection>(),
        provider.GetRequiredService<IEventSerializer>(),
        provider.GetRequiredService<IAppLogger>()));
```

**Check that ProjectionHostedService is registered:**
```csharp
// CleanServiceConfiguration.cs line 507:
services.AddHostedService<ProjectionHostedService>();
```

---

### **Step 4: Check Startup Order**

**Question:** What initializes first?

1. DI Container builds services
2. IHostedService.StartAsync() called (ProjectionHostedService)
3. Application.Startup event
4. MainWindow created
5. ViewModels created
6. CategoryStore.InitializeAsync() called

**Potential Issue:** CategoryStore.InitializeAsync() might run BEFORE ProjectionHostedService catches up!

---

### **Step 5: Check Projection Catch-Up**

**ProjectionHostedService.cs:**
```csharp
public async Task StartAsync(CancellationToken cancellationToken)
{
    _logger.Info("🚀 Starting projection background service...");
    
    // Initial catch-up (should this be awaited synchronously?)
    await _orchestrator.CatchUpAsync();
    
    // Start background polling
    _timer = new Timer(...);
}
```

**Question:** Is the initial catch-up happening synchronously or asynchronously?

---

## 🎯 **MOST LIKELY CAUSE**

### **My Best Guess: Projection Timing**

**The Issue:**

```
App Starts
  ↓
DI Container initialized
  ↓
ProjectionHostedService.StartAsync() called
  ↓
Starts CatchUpAsync() in background ← ASYNC!
  ↓
MainWindow created ← DOESN'T WAIT!
  ↓
TodoPlugin loaded
  ↓
CategoryStore.InitializeAsync() runs ← TOO EARLY!
  ↓
Queries tree_view ← Projection not caught up yet!
  ↓
Returns empty/stale data
  ↓
Validation fails
  ↓
Categories removed
```

**Solution:** Wait for projections to catch up before initializing CategoryStore

---

## 🔧 **POTENTIAL FIXES**

### **Option A: Add Projection Wait in CategoryStore** ✅ RECOMMENDED

**Modify CategoryStore.InitializeAsync():**
```csharp
public async Task InitializeAsync()
{
    // WAIT for projections to catch up first
    var projectionOrchestrator = _serviceProvider.GetRequiredService<IProjectionOrchestrator>();
    await projectionOrchestrator.CatchUpAsync();
    
    _logger.Info("[CategoryStore] Projections caught up, now loading categories...");
    
    // ... rest of initialization ...
}
```

**Pros:**
- Ensures projections are up-to-date
- Simple one-line fix
- Safe (projections always current)

**Cons:**
- Slight startup delay
- Requires IProjectionOrchestrator dependency

---

### **Option B: Invalidate Cache After Projection Catch-Up**

**Modify ProjectionHostedService.StartAsync():**
```csharp
public async Task StartAsync(CancellationToken cancellationToken)
{
    await _orchestrator.CatchUpAsync();
    
    // Invalidate all caches after catch-up
    var categorySyncService = _serviceProvider.GetService<ICategorySyncService>();
    categorySyncService?.InvalidateCache();
}
```

**Pros:**
- Doesn't slow down initialization
- Ensures cache is fresh

**Cons:**
- Requires service locator
- Timing still might be wrong

---

### **Option C: Remove Validation**

**Modify CategoryStore.InitializeAsync():**
```csharp
// SKIP validation - trust user_preferences
var validCategories = savedCategories;  // Don't validate!
```

**Pros:**
- Simple
- Fast startup

**Cons:**
- Can leave orphaned categories
- Not robust

---

### **Option D: Add Retry Logic to Validation**

**Modify CategoryStore.InitializeAsync():**
```csharp
foreach (var category in savedCategories)
{
    var stillExists = await _syncService.IsCategoryInTreeAsync(category.Id);
    
    if (!stillExists)
    {
        // Retry after invalidating cache
        _syncService.InvalidateCache();
        await Task.Delay(100);  // Let projection catch up
        stillExists = await _syncService.IsCategoryInTreeAsync(category.Id);
    }
    
    if (stillExists)
    {
        validCategories.Add(category);
    }
}
```

**Pros:**
- Handles timing issues gracefully
- Doesn't require new dependencies

**Cons:**
- Adds startup delay
- Hacky (sleep/retry)

---

## 📋 **RECOMMENDED APPROACH**

### **Option A: Wait for Projections**

**Add IProjectionOrchestrator dependency to CategoryStore:**

1. Add field: `private readonly IProjectionOrchestrator _projectionOrchestrator;`
2. Add constructor parameter
3. In InitializeAsync(), call: `await _projectionOrchestrator.CatchUpAsync();`

**Impact:**
- Ensures projections are current before validation
- Clean, robust solution
- Slight startup delay (acceptable)

**Confidence:** 95%

---

## 🎯 **NEXT STEPS**

1. **Add diagnostic logging** to understand exact failure point
2. **Check database files** to verify data is there
3. **Implement Option A** (wait for projections)
4. **Test again**

**Awaiting user feedback on which diagnostic approach to take.**


