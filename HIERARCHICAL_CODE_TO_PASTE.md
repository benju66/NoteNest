# 📝 Hierarchical Lookup Code - Ready to Paste

## 🎯 Instructions

1. Open `NoteNest.UI\Plugins\TodoPlugin\Infrastructure\Sync\TodoSyncService.cs`
2. Go to **line 202** (find: `if (noteNode == null)`)
3. **Select and DELETE lines 202-240** (the entire `if (noteNode == null) { ... }` block)
4. **Paste the code below** in its place
5. Save file
6. Build: `dotnet build NoteNest.UI\NoteNest.UI.csproj`
7. Run app

---

## 📋 CODE TO PASTE (Lines 202-240)

```csharp
            if (noteNode == null)
            {
                _logger.Info($"[TodoSync] Note not in tree_view - starting HIERARCHICAL folder lookup");
                
                // HIERARCHICAL LOOKUP: Walk up folder tree to find nearest category
                var currentFolderPath = Path.GetDirectoryName(filePath);
                int level = 0;
                
                while (!string.IsNullOrEmpty(currentFolderPath) && level < 10)
                {
                    // Stop if we've reached or gone above the notes root
                    if (currentFolderPath.Length <= _notesRootPath.Length)
                    {
                        _logger.Debug($"[TodoSync] Reached notes root at level {level}");
                        break;
                    }
                    
                    // Convert to canonical format for database lookup
                    var relativePath = Path.GetRelativePath(_notesRootPath, currentFolderPath);
                    var canonicalFolderPath = relativePath.Replace('\\', '/').ToLowerInvariant();
                    
                    _logger.Info($"[TodoSync] HIERARCHICAL Level {level + 1}: Checking '{canonicalFolderPath}'");
                    
                    var folderNode = await _treeQueryService.GetByPathAsync(canonicalFolderPath);
                    
                    if (folderNode != null && folderNode.NodeType == TreeNodeType.Category)
                    {
                        categoryId = folderNode.Id;
                        _logger.Info($"[TodoSync] SUCCESS! Found at level {level + 1}: {folderNode.Name} (ID: {categoryId})");
                        
                        // Auto-add category to todo panel if not already there
                        await EnsureCategoryAddedAsync(categoryId.Value);
                        
                        await ReconcileTodosAsync(Guid.Empty, filePath, candidates, categoryId);
                        return;
                    }
                    
                    _logger.Debug($"[TodoSync] Not found at level {level + 1}, going up to parent...");
                    currentFolderPath = Path.GetDirectoryName(currentFolderPath);
                    level++;
                }
                
                // No matching category found in entire hierarchy
                _logger.Info($"[TodoSync] No category found after {level} hierarchical levels - creating uncategorized");
                await ReconcileTodosAsync(Guid.Empty, filePath, candidates, categoryId: null);
                return;
            }
```

---

## 🔍 Visual Guide

**BEFORE (lines 202-240):**
```csharp
if (noteNode == null)
{
    // Try parent folder (SINGLE LEVEL only)
    var parentNode = await ...
    if (parentNode != null) { ... }
}
```

**AFTER (lines 202-240):**
```csharp
if (noteNode == null)
{
    // HIERARCHICAL: Walk up tree
    while (level < 10) {  ← THIS IS THE KEY CHANGE!
        Try current level
        Go up if not found
    }
}
```

---

## ✅ Expected Log Output After Fix

When you create a note-linked task, you should see:

```
[TodoSync] Note not in tree_view - starting HIERARCHICAL folder lookup
[TodoSync] HIERARCHICAL Level 1: Checking 'projects/25-117 - op iii/daily notes'
[TodoSync] Not found at level 1, going up to parent...
[TodoSync] HIERARCHICAL Level 2: Checking 'projects/25-117 - op iii'
[TodoSync] SUCCESS! Found at level 2: 25-117 - OP III (ID: b9d84b31...)
```

Instead of the current:
```
[TodoSync] Looking up parent folder in tree_view: 'projects/25-117 - op iii/daily notes'
[TodoSync] Parent folder also not in tree DB yet
[TodoSync] Creating 1 uncategorized todos
```

---

## 🎯 What This Fixes

**Your Scenario:**
- Note: `25-117 - OP III\Daily Notes\Note.rtf`
- Category in todo panel: `25-117 - OP III`

**Current behavior (single-level):**
1. Looks for "Daily Notes" → Not found
2. Stops → Creates uncategorized ❌

**After fix (hierarchical):**
1. Looks for "Daily Notes" → Not found
2. **Goes up to "25-117 - OP III"** → Found! ✅
3. Uses that category ✅

---

## ⏱️ Estimated Time: 2 minutes

1. Open file (30 sec)
2. Find and delete lines 202-240 (30 sec)
3. Paste new code (10 sec)
4. Save and build (50 sec)

---

**Ready when you are! Let me know once you've pasted it and I'll help verify it worked.**

