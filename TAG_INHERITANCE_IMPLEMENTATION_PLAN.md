# 🏗️ TAG INHERITANCE - COMPREHENSIVE IMPLEMENTATION PLAN

**Date:** October 17, 2025  
**User Decisions:**
1. ✅ Notes SHOULD inherit folder tags (Option A)
2. ✅ Existing items SHOULD be updated (Option A/C with NO UI freezes)
3. ✅ NoteTagDialog should show inherited tags (follows decision 1)

**Goal:** Zero UI freezes, perfect deduplication, event-driven architecture  
**Confidence:** 97%

---

## 🎯 **REQUIREMENTS**

### **R1: Note Tag Inheritance**
- When note created in tagged folder → automatically gets folder tags
- When note tag dialog opened → shows inherited folder tags (read-only)
- Deduplication: If parent folder has "25-117" AND subfolder has "25-117" → note gets it ONCE

### **R2: Existing Item Updates (Zero UI Freeze)**
- When folder tags set with "Inherit to Children" ✓ → update existing children
- Must NOT freeze UI (could be 100+ items)
- Must show progress/feedback to user
- Must handle errors gracefully

### **R3: Proper Deduplication**
- Union merge with case-insensitive comparison
- Tag "25-117" = Tag "25-117" (even if different casing)
- Prevent same tag being added twice

---

## 🏗️ **ARCHITECTURE DESIGN**

### **Pattern: Event-Driven Background Processing**

**Why Event-Driven:**
- ✅ Decoupled (handler doesn't know about UI)
- ✅ Non-blocking (background service processes events)
- ✅ Resilient (failures don't crash user operation)
- ✅ Extensible (multiple subscribers possible)
- ✅ Matches existing ProjectionHostedService pattern

**Flow:**
```
User Action: Set folder tags with "Inherit to Children" ✓
   ↓
SetFolderTagHandler saves CategoryTagsSet event to events.db
   ↓
Returns immediately (dialog closes, no freeze) ✅
   ↓
Background: CategoryTagsSet event processed
   ↓
Background: TagInheritancePropagationService picks it up
   ↓
Background: Queries all child items (notes, todos, subcategories)
   ↓
Background: Applies tags to each item (batched, throttled)
   ↓
UI: Items update gradually as tags applied (reactive)
```

**No UI Blocking:** ✅ Handler returns before bulk operation starts

---

## 📋 **IMPLEMENTATION PLAN**

### **Phase 1: Note Tag Inheritance (NEW Creation)** ⏱️ 2 hours

**Goal:** New notes automatically get folder tags

#### **Step 1.1: Update CreateNoteHandler**

**File:** `NoteNest.Application/Notes/Commands/CreateNote/CreateNoteHandler.cs`

**Changes:**
```csharp
public class CreateNoteHandler
{
    // Add dependency:
    private readonly ITagQueryService _tagQueryService;
    
    public async Task<Result<CreateNoteResult>> Handle(...)
    {
        // ... existing note creation code ...
        await _eventStore.SaveAsync(note);
        
        // NEW: Apply folder tags to newly created note
        await ApplyFolderTagsToNoteAsync(note.Id, categoryGuid);
        
        return Result.Ok(...);
    }
    
    private async Task ApplyFolderTagsToNoteAsync(Guid noteId, Guid categoryId)
    {
        try
        {
            // Get all applicable tags from folder hierarchy (with deduplication)
            var tags = await GetInheritedCategoryTagsAsync(categoryId);
            
            if (tags.Count > 0)
            {
                // Load note aggregate and set tags
                var note = await _eventStore.LoadAsync<Note>(noteId);
                note.SetTags(tags);
                await _eventStore.SaveAsync(note);
                await _projectionOrchestrator.CatchUpAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to apply folder tags to note (non-fatal)");
            // Don't fail note creation if tag inheritance fails
        }
    }
    
    private async Task<List<string>> GetInheritedCategoryTagsAsync(Guid categoryId)
    {
        // Query projections.db for category tags up the hierarchy
        var tags = await _tagQueryService.GetTagsForEntityAsync(categoryId, "category");
        
        // Get parent categories recursively
        var parentTags = await GetParentCategoryTagsAsync(categoryId);
        
        // Merge with deduplication (Union handles case-insensitive)
        var allTags = tags.Select(t => t.DisplayName)
            .Union(parentTags, StringComparer.OrdinalIgnoreCase)
            .ToList();
            
        return allTags;
    }
}
```

**Deduplication Strategy:**
- Use `Union()` with `StringComparer.OrdinalIgnoreCase`
- Same as TodoPlugin pattern (proven to work)
- "25-117" from parent + "25-117" from child = one tag ✅

---

### **Phase 2: Note Tag Dialog - Show Inherited** ⏱️ 30 minutes

**Goal:** NoteTagDialog displays inherited folder tags (read-only)

#### **Step 2.1: Fix NoteTagDialog.LoadTagsAsync()**

**File:** `NoteNest.UI/Windows/NoteTagDialog.xaml.cs`

**Current (line 69-71):**
```csharp
// TODO: Implement inherited tags via recursive category query
// For now, just load direct tags
var folderTags = new List<TagDto>();  // ❌ Always empty
```

**Replace With:**
```csharp
// Load inherited folder tags
var folderTags = await LoadInheritedFolderTagsAsync(_noteId);
```

**Add Method:**
```csharp
private async Task<List<TagDto>> LoadInheritedFolderTagsAsync(Guid noteId)
{
    try
    {
        // 1. Get note's category from projections
        var note = await GetNoteFromProjections(noteId);
        if (note == null || note.CategoryId == Guid.Empty)
            return new List<TagDto>();
            
        // 2. Get category's tags (including inherited from parents)
        var categoryTags = await _tagQueryService.GetTagsForEntityAsync(
            note.CategoryId, "category");
        
        // 3. Recursively get parent category tags
        var parentTags = await GetParentCategoryTagsRecursiveAsync(note.CategoryId);
        
        // 4. Merge with deduplication
        var allInheritedTags = categoryTags
            .Union(parentTags, new TagDtoComparer())
            .ToList();
            
        return allInheritedTags;
    }
    catch (Exception ex)
    {
        _logger.Error(ex, "Failed to load inherited folder tags");
        return new List<TagDto>();
    }
}
```

**Deduplication:**
- Custom comparer or DISTINCT on tag name (case-insensitive)
- Ensures "25-117" only appears once

---

### **Phase 3: Background Tag Propagation Service** ⏱️ 4 hours

**Goal:** Update existing items without freezing UI

#### **Step 3.1: Create TagPropagationService (Hosted Service)**

**New File:** `NoteNest.Infrastructure/Services/TagPropagationService.cs`

```csharp
/// <summary>
/// Background service that propagates category tags to child items.
/// Subscribes to CategoryTagsSet events and updates existing children.
/// Runs asynchronously without blocking UI.
/// </summary>
public class TagPropagationService : IHostedService
{
    private readonly Core.Services.IEventBus _eventBus;
    private readonly IEventStore _eventStore;
    private readonly ITreeQueryService _treeQueryService;
    private readonly ITagQueryService _tagQueryService;
    private readonly IProjectionOrchestrator _projectionOrchestrator;
    private readonly IAppLogger _logger;
    
    // Batching configuration
    private const int BATCH_SIZE = 10;  // Process 10 items at a time
    private const int BATCH_DELAY_MS = 100;  // 100ms between batches
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe to CategoryTagsSet events
        _eventBus.Subscribe<Domain.Common.IDomainEvent>(async domainEvent =>
        {
            if (domainEvent is Domain.Categories.Events.CategoryTagsSet e)
            {
                // Fire-and-forget background processing (non-blocking)
                _ = Task.Run(async () => 
                    await PropagateTagsToChildrenAsync(e), 
                    cancellationToken);
            }
        });
        
        return Task.CompletedTask;
    }
    
    private async Task PropagateTagsToChildrenAsync(CategoryTagsSet tagEvent)
    {
        try
        {
            if (!tagEvent.InheritToChildren)
            {
                _logger.Info($"InheritToChildren = false, skipping propagation");
                return;
            }
            
            _logger.Info($"🔄 Starting background tag propagation for category {tagEvent.CategoryId}");
            
            // Step 1: Get all descendant items (notes + subcategories recursively)
            var descendants = await GetAllDescendantsAsync(tagEvent.CategoryId);
            
            _logger.Info($"Found {descendants.Notes.Count} notes and {descendants.SubCategories.Count} subcategories to update");
            
            // Step 2: Calculate inherited tags for this category
            var inheritedTags = await GetInheritedTagsAsync(tagEvent.CategoryId);
            
            // Step 3: Merge category's own tags + inherited tags from parents
            var allTags = tagEvent.Tags
                .Union(inheritedTags, StringComparer.OrdinalIgnoreCase)
                .ToList();
            
            _logger.Info($"Merged tags: {string.Join(", ", allTags)}");
            
            // Step 4: Update notes in batches (avoid UI freeze)
            await UpdateNotesBatchedAsync(descendants.Notes, allTags);
            
            // Step 5: Recursively trigger updates for subcategories
            await TriggerSubcategoryUpdatesAsync(descendants.SubCategories);
            
            _logger.Info($"✅ Tag propagation complete for category {tagEvent.CategoryId}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to propagate tags for category {tagEvent.CategoryId}");
            // Don't throw - background task failures shouldn't crash app
        }
    }
    
    private async Task UpdateNotesBatchedAsync(List<Guid> noteIds, List<string> tags)
    {
        int processed = 0;
        
        for (int i = 0; i < noteIds.Count; i += BATCH_SIZE)
        {
            var batch = noteIds.Skip(i).Take(BATCH_SIZE).ToList();
            
            foreach (var noteId in batch)
            {
                try
                {
                    var note = await _eventStore.LoadAsync<Note>(noteId);
                    if (note != null)
                    {
                        // Merge note's manual tags with inherited folder tags
                        var combinedTags = note.Tags  // Manual tags
                            .Union(tags, StringComparer.OrdinalIgnoreCase)  // + Inherited (dedup)
                            .ToList();
                        
                        note.SetTags(combinedTags);
                        await _eventStore.SaveAsync(note);
                        processed++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to update note {noteId}: {ex.Message}");
                    // Continue with other notes
                }
            }
            
            // Small delay between batches to avoid overwhelming the system
            if (i + BATCH_SIZE < noteIds.Count)
                await Task.Delay(BATCH_DELAY_MS);
        }
        
        _logger.Info($"Updated {processed}/{noteIds.Count} notes with inherited tags");
        
        // Update projections once after all batches
        await _projectionOrchestrator.CatchUpAsync();
    }
    
    private async Task<DescendantItems> GetAllDescendantsAsync(Guid categoryId)
    {
        // Query projections.db tree_view with recursive CTE
        // Get all notes WHERE parent_id = categoryId (direct children)
        // Get all subcategories WHERE parent_id = categoryId
        // For each subcategory, recursively get ITS descendants
        
        // Returns: { Notes: [guid1, guid2...], SubCategories: [guid3, guid4...] }
    }
}
```

**Key Features:**
- ✅ **Non-blocking:** Runs in background via `Task.Run`
- ✅ **Batched:** Processes 10 items at a time
- ✅ **Throttled:** 100ms delay between batches
- ✅ **Resilient:** Individual failures don't stop entire operation
- ✅ **Deduplication:** Union with case-insensitive comparison

**Performance:**
- 100 items = 10 batches × 100ms delay = ~1 second total
- User sees immediate dialog close
- Items update gradually in background
- No UI freeze! ✅

---

### **Phase 4: Deduplication Strategy** ⏱️ 1 hour

**Goal:** Ensure "25-117" only appears once even if multiple ancestors have it

#### **Deduplication Points:**

**Point 1: Tag Collection (GetInheritedTagsAsync)**
```csharp
// In GetInheritedCategoryTagsAsync:
var parentTags = await GetAllAncestorTagsAsync(categoryId);

// Recursive walk UP tree:
var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

foreach (var ancestor in ancestors)
{
    var ancestorTags = await GetCategoryTagsAsync(ancestor.Id);
    foreach (var tag in ancestorTags)
    {
        tags.Add(tag);  // HashSet prevents duplicates
    }
}

return tags.ToList();  // No duplicates guaranteed
```

**Point 2: Merge with Existing Tags**
```csharp
// When applying to note:
var note = await _eventStore.LoadAsync<Note>(noteId);

var combinedTags = note.Tags  // Existing manual tags
    .Union(inheritedTags, StringComparer.OrdinalIgnoreCase)  // + Inherited
    .ToList();

note.SetTags(combinedTags);  // Deduped result
```

**Example:**
```
Scenario:
- Grandparent folder: ["25-117"]
- Parent folder: ["25-117", "OP-III"]  
- Note already has manual tags: ["draft"]

Step 1: Collect inherited
  - Grandparent contributes: ["25-117"]
  - Parent contributes: ["25-117", "OP-III"]
  - HashSet merge: ["25-117", "OP-III"] ✅ (no duplicate)

Step 2: Merge with existing
  - Existing: ["draft"]
  - Inherited: ["25-117", "OP-III"]
  - Union: ["draft", "25-117", "OP-III"] ✅

Result: Note has 3 tags, "25-117" appears exactly once ✅
```

---

## 🔄 **COMPLETE DATA FLOW**

### **Scenario 1: Set Tags on Folder with Children**

```
BEFORE:
Folder "25-117 - OP III" (no tags)
  ↓
  Note "Meeting.rtf" (no tags)
  Note "Spec.rtf" (manual tag: "v1")
  Subfolder "Subsection A" (no tags)
    ↓
    Note "Details.rtf" (no tags)
    Todo "Review details" (no tags)
  Todo "Main task" (no tags)

USER ACTION:
1. Right-click "25-117 - OP III"
2. Set Tags
3. Add tags: ["25-117", "project"]
4. Check "Inherit to Children" ✓
5. Click Save

IMMEDIATE (< 100ms):
- CategoryAggregate.SetTags(["25-117", "project"], inheritToChildren: true)
- CategoryTagsSet event saved to events.db
- Dialog closes ✅ (User not blocked!)

BACKGROUND (1-3 seconds):
- TagPropagationService receives CategoryTagsSet event
- Queries all descendants:
  - 3 notes found
  - 1 subcategory found
  - 1 todo found
- Updates in batches:
  
  Batch 1 (notes 1-2):
    - Note "Meeting.rtf": SetTags(["25-117", "project"])
    - Note "Spec.rtf": SetTags(["v1", "25-117", "project"]) ← Manual preserved!
    
  Batch 2 (note 3 + todo):
    - Note "Details.rtf": SetTags(["25-117", "project"]) 
    - Todo "Review details": Apply tags via TodoAggregate
    
  Batch 3 (subcategory recursion):
    - Subfolder "Subsection A": Gets inherited tags ["25-117", "project"]
    - Its children also get tags recursively

AFTER:
Folder "25-117 - OP III" (tags: ["25-117", "project"])
  ↓
  Note "Meeting.rtf" (tags: ["25-117", "project"]) ✅
  Note "Spec.rtf" (tags: ["v1", "25-117", "project"]) ✅ Manual preserved
  Subfolder "Subsection A" (inherited: ["25-117", "project"])
    ↓
    Note "Details.rtf" (tags: ["25-117", "project"]) ✅
    Todo "Review details" (tags: ["25-117", "project"]) ✅
  Todo "Main task" (tags: ["25-117", "project"]) ✅
```

**Time:** ~2 seconds for 6 items, user never blocked ✅

---

### **Scenario 2: Deduplication Example**

```
Folder Hierarchy:
  Projects (tags: ["25-117"])
    ↓
  25-117 - OP III (tags: ["25-117", "OP-III"])  ← Duplicate "25-117"
    ↓
  Note "Test.rtf" (manual: ["draft"])

Tag Calculation:
1. Get "25-117 - OP III" tags: ["25-117", "OP-III"]
2. Get parent "Projects" tags: ["25-117"]
3. Merge with Union:
   - HashSet: Add "25-117" ✅
   - HashSet: Add "OP-III" ✅
   - HashSet: Add "25-117" → Already exists, skipped ✅
   - Result: ["25-117", "OP-III"] (no duplicate!)
4. Merge with note's manual tags: ["draft"]
5. Final tags for note: ["draft", "25-117", "OP-III"] ✅

Perfect! "25-117" appears exactly once ✅
```

---

## 🚨 **CRITICAL: NO UI FREEZES**

### **Anti-Freeze Mechanisms:**

**1. Fire-and-Forget Pattern**
```csharp
// In SetFolderTagHandler (after save):
await _eventStore.SaveAsync(categoryAggregate);
await _projectionOrchestrator.CatchUpAsync();

// Publish event for background propagation
await _eventBus.PublishAsync(tagEvent);  // ← Subscribers don't block

return Result.Ok(...);  // ← Returns IMMEDIATELY
```

**Handler returns in <100ms, dialog closes instantly** ✅

**2. Background Processing**
```csharp
// TagPropagationService subscription:
_eventBus.Subscribe<CategoryTagsSet>(async e =>
{
    _ = Task.Run(async () =>  // ← Background thread
    {
        await PropagateTagsToChildrenAsync(e);
    });
});
```

**Event handler returns immediately** ✅

**3. Batched Updates**
```csharp
// Process 10 items at a time:
for (int i = 0; i < items.Count; i += 10)
{
    var batch = items.Skip(i).Take(10);
    // Process batch...
    await Task.Delay(100);  // Breathe between batches
}
```

**CPU gets breaks, UI stays responsive** ✅

**4. No Dispatcher Blocking**
```csharp
// DON'T DO THIS (blocks UI):
await Application.Current.Dispatcher.InvokeAsync(() => {
    foreach (var item in 100Items) // ❌ Locks UI for seconds
        collection.Add(item);
});

// DO THIS INSTEAD:
// Update database in background
// Let projections catchup update UI incrementally
```

**Background updates, UI reactivity** ✅

---

## 📊 **PERFORMANCE ESTIMATES**

### **Small Folder (10 items):**
- User sees: Dialog closes instantly (<100ms)
- Background: Updates complete in ~500ms
- User never notices delay

### **Medium Folder (50 items):**
- User sees: Dialog closes instantly
- Background: 5 batches × 100ms delay = ~1 second
- User might see tags appearing gradually (good feedback!)

### **Large Folder (200 items):**
- User sees: Dialog closes instantly
- Background: 20 batches × 100ms delay = ~3 seconds
- Items update progressively
- Optional: Status notification "Updated 200 items with tags"

**No UI freezes at any scale!** ✅

---

## 🎯 **DEDUPLICATION ALGORITHM**

### **Tag Merge Function (Reusable):**

```csharp
public static class TagMerger
{
    /// <summary>
    /// Merges tag lists with case-insensitive deduplication.
    /// Preserves original casing of first occurrence.
    /// </summary>
    public static List<string> MergeTagLists(
        params List<string>[] tagLists)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        
        foreach (var list in tagLists)
        {
            foreach (var tag in list)
            {
                if (seen.Add(tag))  // Returns false if already exists
                {
                    result.Add(tag);  // First occurrence wins
                }
            }
        }
        
        return result;
    }
}

// Usage:
var mergedTags = TagMerger.MergeTagLists(
    grandparentTags,  // ["25-117"]
    parentTags,       // ["25-117", "OP-III"]
    noteTags          // ["draft"]
);
// Result: ["25-117", "OP-III", "draft"] ✅
```

---

## 🏗️ **TREE QUERY FOR DESCENDANTS**

### **SQL Query (Use Projections):**

```sql
-- Get all descendant notes recursively
WITH RECURSIVE category_tree AS (
    -- Start with target category
    SELECT id, parent_id, 0 AS depth
    FROM tree_view
    WHERE id = @CategoryId AND node_type = 'category'
    
    UNION ALL
    
    -- Walk down to child categories
    SELECT tv.id, tv.parent_id, ct.depth + 1
    FROM tree_view tv
    INNER JOIN category_tree ct ON tv.parent_id = ct.id
    WHERE tv.node_type = 'category' AND ct.depth < 20
)
SELECT DISTINCT n.id
FROM category_tree ct
INNER JOIN tree_view n ON n.parent_id = ct.id
WHERE n.node_type = 'note';

-- Same pattern for todos (query todo_view)
```

**Efficient:** Single query gets all descendants ✅

---

## 🔧 **IMPLEMENTATION CHECKLIST**

### **Phase 1: Note Tag Inheritance (NEW Items)** ⏱️ 2h
- [ ] Add `ITagQueryService` to CreateNoteHandler
- [ ] Create `ApplyFolderTagsToNoteAsync()` method
- [ ] Create `GetInheritedCategoryTagsAsync()` with deduplication
- [ ] Test: Create note in tagged folder → note gets tags
- [ ] Test: Parent folder "25-117", child folder "25-117" → note gets tag once

### **Phase 2: NoteTagDialog Inherited Display** ⏱️ 30min
- [ ] Create `LoadInheritedFolderTagsAsync()` method
- [ ] Query note's category from projections
- [ ] Query category's inherited tags
- [ ] Populate `_inheritedTags` collection
- [ ] Test: Open note in tagged folder → shows inherited tags

### **Phase 3: Background Propagation Service** ⏱️ 4h
- [ ] Create `TagPropagationService : IHostedService`
- [ ] Subscribe to CategoryTagsSet events
- [ ] Create `PropagateTagsToChildrenAsync()` with batching
- [ ] Create `GetAllDescendantsAsync()` recursive query
- [ ] Create `UpdateNotesBatchedAsync()` with throttling
- [ ] Create `UpdateTodosBatchedAsync()` (TodoPlugin integration)
- [ ] Register service in DI
- [ ] Test: Set tags on folder with 50 items → no UI freeze

### **Phase 4: Todo Propagation** ⏱️ 1h
- [ ] Extend `BulkUpdateFolderTodosAsync()` to use batching
- [ ] Call from TagPropagationService
- [ ] Preserve manual tags, update auto-tags only
- [ ] Test: Set folder tags → existing todos get updated

### **Phase 5: Testing & Polish** ⏱️ 1h
- [ ] Test deduplication (parent + child same tag)
- [ ] Test large folders (100+ items)
- [ ] Test nested hierarchies (5 levels deep)
- [ ] Verify no duplicates in database
- [ ] Test search: type "25-117" → finds all items

**Total Estimated Time: 8.5 hours**

---

## ✅ **CONFIDENCE ASSESSMENT**

| **Component** | **Confidence** | **Reasoning** |
|---------------|----------------|---------------|
| Note tag inheritance (new) | 95% | Mirrors todo pattern exactly |
| Deduplication algorithm | 98% | Union + HashSet proven pattern |
| Background service pattern | 95% | Matches ProjectionHostedService |
| Batched updates | 97% | Standard async pattern |
| Event subscription | 98% | Matches TodoStore pattern |
| Zero UI freeze guarantee | 93% | Fire-and-forget + batching works |
| Recursive descendant query | 90% | SQL CTE pattern exists, needs adaptation |
| DI registration | 99% | Straightforward service registration |

**Overall Confidence: 95%**

---

## 🎯 **ARCHITECTURAL BENEFITS**

### **Event-Driven Design:**
```
SetFolderTagHandler (Fast Path):
  ↓ (50ms)
Save CategoryTagsSet event
  ↓
Return to user (dialog closes)
  ↓
[USER IS UNBLOCKED]

TagPropagationService (Background):
  ↓ (running asynchronously)
Processes event from event store
  ↓
Updates child items in batches
  ↓
User sees items getting tags gradually
```

**User never waits!** ✅

---

## 🚨 **RISKS & MITIGATION**

### **Risk 1: Large Folder (1000+ items)**
**Mitigation:**
- Batch size: 10 items
- Batch delay: 100ms
- Total time: ~10 seconds
- Still no UI freeze (background)
- Consider: Log message "Updating 1000 items in background..."

### **Risk 2: Event Processing Failure**
**Mitigation:**
- Try-catch around each item update
- Log failures, continue with others
- Projection catchup ensures eventual consistency

### **Risk 3: Duplicate Event Subscription**
**Mitigation:**
- Service only subscribes once (in StartAsync)
- Unsubscribe in StopAsync
- No duplicate processing

### **Risk 4: Deduplication Edge Cases**
**Mitigation:**
- Use StringComparer.OrdinalIgnoreCase consistently
- Test cases: "25-117" vs "25-117" vs "25-117 " (trimming)
- Proven pattern from TodoPlugin (already works)

---

## 📝 **TODO QUERIES NEEDED**

### **Query 1: Get All Descendant Notes**
```sql
WITH RECURSIVE category_tree AS (
    SELECT id FROM tree_view 
    WHERE id = @CategoryId AND node_type = 'category'
    UNION ALL
    SELECT tv.id FROM tree_view tv
    INNER JOIN category_tree ct ON tv.parent_id = ct.id
    WHERE tv.node_type = 'category'
)
SELECT n.id FROM tree_view n
INNER JOIN category_tree ct ON n.parent_id = ct.id
WHERE n.node_type = 'note';
```

### **Query 2: Get All Descendant Todos**
```sql
-- Query todo_view instead
SELECT id FROM todo_view
WHERE category_id IN (
    WITH RECURSIVE category_tree AS (...)
    SELECT id FROM category_tree
);
```

**Both efficient:** Indexed on parent_id and category_id ✅

---

## 🎓 **DESIGN DECISIONS**

### **Decision 1: Notes Get Auto-Tags (Like Todos)**

**Rationale:**
- Consistent with todo behavior
- Folder tags = organizational context
- Note can have manual tags + auto tags
- Union merge prevents duplicates
- Searchability: Find all "25-117" notes

**Implementation:**
- Note.Tags property (already added!)
- Source field: "auto-inherit" for folder tags, "manual" for user tags
- NoteTagDialog shows both (inherited read-only, manual editable)

---

### **Decision 2: Background Propagation (Not Synchronous)**

**Rationale:**
- User experience paramount (no freezing)
- Event sourcing enables this (event saved, processed async)
- Proven pattern (ProjectionHostedService does same thing)
- Failures don't block user action

**Implementation:**
- IHostedService subscribes to events
- Fire-and-forget background processing
- Batched + throttled for performance
- Projections catch up automatically

---

### **Decision 3: Preserve Manual Tags During Propagation**

**Rationale:**
- User intent must be respected
- Manual tags = explicit user choice
- Auto-tags = system-generated context
- Merge, don't replace

**Implementation:**
```csharp
// When updating note:
var existingManualTags = note.Tags.Where(IsManualTag);
var newInheritedTags = GetInheritedTags();
var combinedTags = existingManualTags.Union(newInheritedTags).ToList();
note.SetTags(combinedTags);
```

**BUT:** Current Note.Tags doesn't track source (manual vs auto)!

**Solution:** Use projections.db entity_tags.source field:
- Query manual tags: `WHERE source = 'manual'`
- Query auto tags: `WHERE source = 'auto-inherit'`
- Merge appropriately

---

## 🔍 **ADDITIONAL RESEARCH FINDINGS**

### **1. TodoPlugin Already Has Full Infrastructure:**
- `TagInheritanceService` - fully implemented
- `BulkUpdateFolderTodosAsync()` - ready to use
- `UpdateTodoTagsAsync()` - merges folder + note tags
- Just need to CALL it from event subscriber

### **2. Deduplication Already Works for Todos:**
- Line 122: `Union(noteTags, StringComparer.OrdinalIgnoreCase)`
- Proven in production
- Same pattern will work for notes

### **3. Background Service Pattern Exists:**
- `ProjectionHostedService` - polls event store
- `TodoSyncService` - monitors note saves
- Same pattern for TagPropagationService

### **4. Tree Query Recursive Patterns Exist:**
- `FolderTagRepository.GetInheritedTagsAsync()` - walks UP tree
- `FolderTagRepository.GetChildFolderIdsAsync()` - walks DOWN tree
- `TreeDatabaseRepository.GetNodeDescendantsAsync()` - full recursion
- Can adapt these patterns

---

## ✅ **FINAL RECOMMENDATION**

### **Implementation Approach:**

**Phase 1 (Quick Win - 2.5 hours):**
1. Note tag inheritance for new notes
2. NoteTagDialog inherited display
3. **Result:** Notes behave like todos, no bulk updates yet

**Phase 2 (Full Solution - 5 hours):**
4. TagPropagationService (background service)
5. Event-driven bulk updates
6. Batched processing
7. **Result:** Existing items updated, zero UI freezes

**Phase 3 (Polish - 1 hour):**
8. Comprehensive testing
9. Edge case handling
10. Documentation

**Total: 8.5 hours** for complete, production-grade implementation

---

## 🎯 **CONFIDENCE BOOSTER**

**Why I'm 95% Confident:**

✅ **All infrastructure exists:**
- Event store ✅
- Event bus ✅
- Background service pattern ✅
- Batching utilities ✅
- Recursive queries ✅
- Deduplication pattern ✅

✅ **Proven patterns:**
- TodoPlugin does this for todos (working)
- ProjectionHostedService does background processing (working)
- Union deduplication tested in production

✅ **No new concepts:**
- Extending existing patterns
- Reusing proven code
- No architectural changes needed

✅ **Risk mitigation:**
- Fire-and-forget = no blocking
- Batching = no resource exhaustion
- Try-catch = resilient to errors

**This is a well-understood problem with battle-tested solutions!** 🚀

---

## 📋 **QUESTIONS BEFORE IMPLEMENTATION**

1. **Confirm scope:** Implement ALL 3 phases (8.5 hours)?
2. **Or phased approach:** Phase 1 first (2.5 hours), test, then Phase 2?
3. **Batch size:** 10 items per batch okay? (Or adjust for your typical folder sizes)
4. **User feedback:** Silent background update or notification "Updated 50 items"?
5. **Manual tag preservation:** Should note manual tags be preserved when folder tags propagate?

---

**Ready for your approval to proceed with implementation!** 🎯

The plan is solid, risk is low, and the result will be a professional-grade tag inheritance system with zero UI freezes.

