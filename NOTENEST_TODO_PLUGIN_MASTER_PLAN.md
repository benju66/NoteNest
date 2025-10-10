# 🎯 NoteNest Todo Plugin - Master Implementation Plan

**Date:** October 10, 2025  
**Status:** Phase 1 In Progress  
**Vision:** Unified task management system with automatic tagging and cross-linking

---

## 📋 **TABLE OF CONTENTS**

1. [Executive Summary](#executive-summary)
2. [Core Vision](#core-vision)
3. [Implementation Phases](#implementation-phases)
4. [Feature Specifications](#feature-specifications)
5. [Technical Architecture](#technical-architecture)
6. [Open Questions](#open-questions)
7. [Success Metrics](#success-metrics)

---

## 🎯 **EXECUTIVE SUMMARY**

### **Goal:**
Create a production-ready todo management system that seamlessly integrates with NoteNest's note-taking workflow through automatic categorization, intelligent tagging, and bidirectional linking.

### **Key Innovation:**
Automatic tag inheritance that creates interconnected relationships between categories, notes, and todos without manual effort.

### **Current Status:**
- ✅ Basic todo creation and storage
- ✅ Category sync from note tree
- ✅ Unified tree view (categories contain todos)
- ✅ Database persistence
- 🚧 RTF auto-categorization (implemented, needs testing)
- ⏳ Auto-tagging system (planned)
- ⏳ Rich metadata (planned)

---

## 🌟 **CORE VISION**

### **The Big Picture:**

```
┌─────────────────────────────────────────────────────────────┐
│                    NOTE TREE (Source of Truth)              │
│  📁 Projects/                                               │
│    └─ ProjectA Plan.rtf                                     │
│       - Contains: "[buy materials]"                         │
│       - Auto-tagged: #Projects                              │
└─────────────────────────────────────────────────────────────┘
                            ↓
                    RTF Parser Extracts
                            ↓
┌─────────────────────────────────────────────────────────────┐
│                    TODO TREE (Task Management)              │
│  📁 Projects/ (added via context menu)                      │
│    └─ ☐ Buy materials                                      │
│       - Auto-tagged: #Projects                              │
│       - Backlink: → ProjectA Plan.rtf (line 45)            │
│       - Due: Oct 15                                         │
│       - Priority: High                                      │
└─────────────────────────────────────────────────────────────┘
                            ↓
                    Unified Search
                            ↓
┌─────────────────────────────────────────────────────────────┐
│  🔍 Search "Projects" → Results:                            │
│    📝 Notes: ProjectA Plan.rtf, Project Budget.rtf          │
│    📁 Categories: Projects, Projects Archive                │
│    ☐ Todos: Buy materials, Submit proposal (3 more...)     │
│    🏷️  Tags: #Projects (links all related items)           │
└─────────────────────────────────────────────────────────────┘
```

### **User Workflow:**

1. **User creates note** in "Projects/ProjectA" folder
2. **Note auto-tagged** with "Projects"
3. **User adds category** to todo tree via context menu
4. **Category auto-tagged** with "Projects"
5. **User types `[buy materials]`** in note
6. **Todo auto-extracted** and placed under "Projects" category
7. **Todo auto-tagged** with "Projects"
8. **Search "Projects"** → Finds note, category, and todo
9. **Click todo** → Opens source note at exact line

**Result:** Zero manual categorization or tagging required!

---

## 🚀 **IMPLEMENTATION PHASES**

### **PHASE 1: Core Linking (Foundation)** 
**Priority:** CRITICAL  
**Duration:** 2-3 days  
**Status:** 🚧 In Progress

#### **1.1 Test RTF Auto-Categorization** ✅ Implemented, Needs Testing
- Verify TodoSyncService extracts todos from notes
- Verify todos appear under correct category
- Verify source note/line tracking works
- Test edge cases (category not added, note moved, etc.)

**Acceptance Criteria:**
- [ ] Create note in "Projects" folder
- [ ] Add "Projects" to todo tree
- [ ] Type `[test task]` in note
- [ ] Save note
- [ ] Todo appears under "Projects" in todo tree within 2 seconds

#### **1.2 Add "Orphaned" Category** ⏳ Not Started
- Create special system category for orphaned todos
- Auto-move todos when source category deleted
- Provide manual reassignment UI
- Style differently (gray icon, italic text)

**Implementation:**
```csharp
// Special category ID
private static readonly Guid OrphanedCategoryId = Guid.Parse("00000000-0000-0000-0000-000000000001");

// Auto-created on startup if not exists
await EnsureOrphanedCategoryExists();

// Move todos when category deleted
await MoveTodosToOrphaned(deletedCategoryId);
```

#### **1.3 Add Backlink UI** ⏳ Not Started
- Display source note name in todo item
- Make clickable link
- Open note at specific line on click
- Show tooltip with note preview

**UI Mockup:**
```
☐ Buy materials
  ↳ From: ProjectA Plan.rtf (line 45) [📄 Open]
```

#### **1.4 Verify Category Persistence** ⏳ Not Started
- Stress test: Add 50+ categories
- Test: App restart preserves all
- Test: Category rename reflects in todos
- Test: Category delete triggers orphan handling

---

### **PHASE 2: Auto-Tagging System (The Core)**
**Priority:** HIGH  
**Duration:** 1-2 weeks  
**Status:** ⏳ Not Started

#### **2.1 Database Schema**
```sql
-- Already exists in todos.db
CREATE TABLE global_tags (
    tag TEXT PRIMARY KEY NOT NULL COLLATE NOCASE,
    color TEXT,              -- Hex color code
    category TEXT,           -- "project", "priority", "context", etc.
    icon TEXT,               -- Emoji or icon identifier
    usage_count INTEGER NOT NULL DEFAULT 0,
    created_at INTEGER NOT NULL
);

-- Link todos to tags (many-to-many)
CREATE TABLE todo_tags (
    todo_id INTEGER NOT NULL,
    tag TEXT NOT NULL,
    FOREIGN KEY (todo_id) REFERENCES todos(id) ON DELETE CASCADE,
    FOREIGN KEY (tag) REFERENCES global_tags(tag) ON DELETE CASCADE
);

-- NEW: Link notes to tags
CREATE TABLE note_tags (
    note_id TEXT NOT NULL,   -- GUID as TEXT
    tag TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    FOREIGN KEY (tag) REFERENCES global_tags(tag) ON DELETE CASCADE
);

-- NEW: Link categories to tags
CREATE TABLE category_tags (
    category_id TEXT NOT NULL,  -- GUID as TEXT
    tag TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    FOREIGN KEY (tag) REFERENCES global_tags(tag) ON DELETE CASCADE
);
```

#### **2.2 Auto-Tagging Rules**

**Rule 1: Note Created in Category**
```csharp
// When note created in "Projects/ProjectA" folder
→ Auto-tag note: "Projects"
→ Auto-tag note: "ProjectA"
```

**Rule 2: Todo Category Created from Note Tree**
```csharp
// When user adds "Projects" to todo tree
→ Auto-tag category: "Projects"
→ Inherit parent tags if nested
```

**Rule 3: Todo Created in Category**
```csharp
// When todo created under "Projects" category
→ Auto-tag todo: "Projects"
→ If from note, also inherit note's tags
```

**Rule 4: Tag Inheritance**
```
Parent: "Projects" (tagged: #Work, #2025)
  ↓
Child: "ProjectA" 
  ↓ Inherits parent tags
  → Tagged: #Work, #2025, #Projects, #ProjectA
```

#### **2.3 Tag Service Implementation**

```csharp
public interface ITagService
{
    // Auto-tagging
    Task AutoTagNoteAsync(Guid noteId, string categoryPath);
    Task AutoTagTodoCategoryAsync(Guid categoryId, string categoryPath);
    Task AutoTagTodoAsync(int todoId, Guid categoryId, Guid? sourceNoteId);
    
    // Tag management
    Task<Tag> GetOrCreateTagAsync(string tagName, string color = null);
    Task<List<Tag>> GetTagsForItemAsync(string itemId, TaggedItemType type);
    Task AddTagToItemAsync(string itemId, TaggedItemType type, string tag);
    Task RemoveTagFromItemAsync(string itemId, TaggedItemType type, string tag);
    
    // Search
    Task<List<TaggedItem>> SearchByTagAsync(string tag);
    Task<List<Tag>> GetAllTagsAsync();
}

public enum TaggedItemType
{
    Note,
    TodoCategory,
    Todo,
    NoteCategory
}
```

#### **2.4 Tag Display UI**

**Option A: Inline Tags (Recommended)**
```
☐ Buy materials #Projects #Urgent #Q4
  ↳ From: ProjectA Plan.rtf
```

**Option B: Tag Pills**
```
☐ Buy materials
  [Projects] [Urgent] [Q4]
  ↳ From: ProjectA Plan.rtf
```

**Option C: Sidebar + Inline**
```
Left Panel:           Main Content:
🏷️ TAGS              ☐ Buy materials #Projects
├─ Projects (5)         ↳ From: ProjectA Plan.rtf
├─ Urgent (3)         
└─ Q4 (12)            ☐ Submit proposal #Projects #Urgent
                        ↳ From: Proposal Draft.rtf
```

---

### **PHASE 3: Rich Todo Metadata**
**Priority:** MEDIUM  
**Duration:** 1 week  
**Status:** ⏳ Not Started

#### **3.1 Priority Levels**

**Model:**
```csharp
public enum TodoPriority
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Urgent = 4
}

// In TodoItem
public TodoPriority Priority { get; set; }
```

**UI:**
```
🔴 ☐ Critical bug fix        (Urgent - Red)
🟠 ☐ Review code              (High - Orange)
🟡 ☐ Update docs              (Medium - Yellow)
🟢 ☐ Clean up comments        (Low - Green)
⚪ ☐ Research new library      (None - Gray)
```

#### **3.2 Due Dates**

**Model:**
```csharp
public DateTime? DueDate { get; set; }
public bool IsOverdue => DueDate.HasValue && DueDate.Value < DateTime.Now && !IsCompleted;
public int? DaysUntilDue => DueDate.HasValue ? (int)(DueDate.Value - DateTime.Now).TotalDays : null;
```

**UI:**
```
☐ Buy materials              📅 Oct 15 (in 5 days)
☐ Submit report              📅 Oct 11 (tomorrow)
☐ Review budget              📅 Oct 9 (⚠️ overdue by 1 day)
```

#### **3.3 Recurring Tasks**

**Model:**
```csharp
public class RecurrenceRule
{
    public RecurrenceType Type { get; set; }  // Daily, Weekly, Monthly, Yearly
    public int Interval { get; set; }         // Every N days/weeks/months
    public int LeadTimeDays { get; set; }     // Show N days before due
    public DateTime? EndDate { get; set; }     // Stop recurring after this date
}

// In TodoItem
public RecurrenceRule Recurrence { get; set; }
```

**Examples:**
```
"Submit monthly report"
- Due: 1st of every month
- Lead time: 5 days
- Shows: 26th of previous month
- Reappears: 1st of next month after completion

"Weekly team meeting prep"
- Due: Every Monday
- Lead time: 1 day
- Shows: Sunday
- Reappears: Next Sunday after completion
```

#### **3.4 Description & Context**

**Model:**
```csharp
public string Description { get; set; }  // Rich text/markdown
public string Notes { get; set; }        // Additional notes
public List<string> Attachments { get; set; }  // File paths
public List<Guid> LinkedNotes { get; set; }    // Related notes
```

**UI:**
```
☐ Buy materials #Projects
  📅 Oct 15 | 🔴 High Priority
  ↳ From: ProjectA Plan.rtf (line 45)
  
  📝 Description:
  Need to purchase materials for Phase 2 construction.
  Budget approved: $5,000
  
  📎 Attachments:
  - Materials List.xlsx
  - Vendor Quotes.pdf
  
  🔗 Related Notes:
  - Project Budget.rtf
  - Vendor Contacts.rtf
```

---

### **PHASE 4: Advanced Features**
**Priority:** LOW (Polish)  
**Duration:** 2-3 weeks  
**Status:** ⏳ Not Started

#### **4.1 Drag-and-Drop**

**Scenarios:**
1. **Todo → Category** - Move todo to different category
2. **Todo → Todo** - Reorder within category
3. **Category → Category** - Reorder categories
4. **Note Tree → Todo Tree** - Create todo from note drag

**Implementation:**
- Use WPF DragDrop framework
- Follow pattern from main app's TreeViewDragHandler
- Visual feedback during drag (ghost item)
- Confirm destructive moves

#### **4.2 Context Menus**

**Category Context Menu:**
```
📁 Projects [▼]
  ├─ Rename...
  ├─ Delete
  ├─ Add Todo...
  ├─ Set Color...
  ├─ Properties...
  └─ Remove from Todo Tree
```

**Todo Context Menu:**
```
☐ Buy materials [▼]
  ├─ Edit...
  ├─ Set Priority ›
  ├─ Set Due Date...
  ├─ Add Tag...
  ├─ Duplicate
  ├─ Move to ›
  ├─ Open Source Note
  └─ Delete
```

#### **4.3 Unified Search**

**Search Box (Top of App):**
```
┌──────────────────────────────────────────────────┐
│ 🔍 Search everywhere... (Ctrl+Shift+F)           │
└──────────────────────────────────────────────────┘
```

**Results Panel:**
```
Search: "budget"

📝 NOTES (3 results)
  ├─ Budget 2025.rtf (in Projects/)
  ├─ Budget Meeting Notes.rtf (in Work/)
  └─ Personal Budget.rtf (in Personal/)

📁 CATEGORIES (2 results)
  ├─ Budget Planning (Note Tree)
  └─ Budget (Todo Tree)

☐ TODOS (5 results)
  ├─ Review budget (Projects/)
  ├─ Submit budget report (Work/)
  └─ Update personal budget (Personal/)

🏷️ TAGS (1 result)
  └─ #budget (links 8 items)

[Filter: All | Notes | Todos | Categories | Tags]
```

#### **4.4 Per-Tree Local Search**

**Note Tree Filter:**
```
📝 Note Tree
┌────────────────────┐
│ 🔍 Filter notes... │
└────────────────────┘
├─ Projects/
├─ Work/
└─ Personal/
```

**Todo Tree Filter:**
```
☐ Todo Manager
┌────────────────────┐
│ 🔍 Filter todos... │
└────────────────────┘
├─ 📁 Projects
├─ 📁 Work
└─ 📁 Personal
```

#### **4.5 Smart Lists**

**Display Location:** Below categories or in separate expandable section

```
☐ TODO MANAGER

🎯 SMART LISTS
├─ 📅 Today (5)
├─ 📆 This Week (12)
├─ 🔥 High Priority (3)
├─ ⏰ Overdue (2)
├─ ⭐ Favorites (7)
├─ 📋 All (45)
└─ ✅ Completed (128)

📁 CATEGORIES
├─ 📁 Projects (8)
├─ 📁 Work (15)
└─ 📁 Personal (22)
```

**Smart List Logic:**
```csharp
public interface ISmartListProvider
{
    List<TodoItem> GetTodayTodos();      // Due today
    List<TodoItem> GetThisWeekTodos();   // Due this week
    List<TodoItem> GetHighPriorityTodos();  // Priority >= High
    List<TodoItem> GetOverdueTodos();    // Past due date, not completed
    List<TodoItem> GetFavoriteTodos();   // User-marked favorites
    List<TodoItem> GetAllTodos();        // All incomplete todos
    List<TodoItem> GetCompletedTodos();  // Completed todos
}
```

---

## 📐 **FEATURE SPECIFICATIONS**

### **RTF Auto-Categorization (Detailed)**

**Input:**
```rtf
File: C:\Notes\Projects\ProjectA\ProjectA Plan.rtf
Content:
Project kickoff meeting scheduled for Monday.

Todo items:
[buy materials] - Need supplies for Phase 2
[contact vendor] - Get quotes from 3 vendors
[submit proposal] - Deadline: Oct 15
```

**Processing Flow:**
```
1. User saves note (Ctrl+S)
   ↓
2. SaveManager fires NoteSaved event
   ↓
3. TodoSyncService.OnNoteSaved() triggered
   ↓
4. Extract note metadata:
   - NoteId: <GUID>
   - FilePath: C:\Notes\Projects\ProjectA\ProjectA Plan.rtf
   - CategoryId: <GUID of ProjectA folder>
   ↓
5. BracketTodoParser.Parse(rtfContent)
   ↓
6. Extracted todos:
   - "buy materials" (line 5, char 120)
   - "contact vendor" (line 6, char 180)
   - "submit proposal" (line 7, char 240)
   ↓
7. For each extracted todo:
   - Check if CategoryId exists in TodoPlugin.Categories
   - IF EXISTS:
     → Create TodoItem with CategoryId
     → Set SourceNoteId, SourceFilePath, SourceLineNumber
   - IF NOT EXISTS:
     → Create TodoItem without CategoryId (uncategorized)
   ↓
8. Save to todos.db
   ↓
9. TodoStore.CollectionChanged event fires
   ↓
10. UI updates (todo appears in tree)
```

**Edge Cases:**
- Category not added to todo tree → Todo uncategorized
- Note moved to different folder → Update todo's CategoryId
- Note deleted → Keep todo but mark source as unavailable
- Todo text changed in note → Update existing todo (stable ID matching)
- Todo removed from note → Delete from todo list

---

### **Tag Inheritance (Detailed)**

**Scenario: Nested Categories**
```
Note Tree:
└─ Work/
   └─ Projects/
      └─ ProjectA/
         └─ Meeting Notes.rtf

Tag Inheritance:
Work → Projects → ProjectA → Meeting Notes.rtf
```

**Auto-Tags Applied:**
```
Meeting Notes.rtf:
- #Work (from ancestor)
- #Projects (from ancestor)
- #ProjectA (from parent)
- #MeetingNotes (from filename)
```

**Todo Created from Note:**
```
☐ Follow up with client
  Tags:
  - #Work (inherited from note)
  - #Projects (inherited from note)
  - #ProjectA (inherited from note)
  - #FollowUp (auto-generated from text analysis)
```

---

## 🏗️ **TECHNICAL ARCHITECTURE**

### **Database Schema Summary**

```
todos.db
├─ todos                    ← Todo items
├─ todo_tags               ← Todo → Tag mapping
├─ global_tags             ← Centralized tag repository
├─ note_tags               ← Note → Tag mapping (NEW)
├─ category_tags           ← Category → Tag mapping (NEW)
├─ user_preferences        ← UI state, selected categories
└─ schema_version          ← Database version tracking

tree.db (Main App)
├─ tree_nodes              ← Categories + Notes metadata
├─ search.db (FTS5)        ← Full-text search index
└─ (Used as source of truth)
```

### **Service Architecture**

```
┌─────────────────────────────────────────────────┐
│              NoteNest Main App                  │
│  ┌───────────────────────────────────────────┐ │
│  │  SaveManager (RTF Save Engine)            │ │
│  │    ↓ NoteSaved Event                      │ │
│  └───────────────────────────────────────────┘ │
└─────────────────────────────────────────────────┘
                    ↓
┌─────────────────────────────────────────────────┐
│              TodoPlugin Services                │
│                                                 │
│  ┌───────────────────────────────────────────┐ │
│  │  TodoSyncService (IHostedService)         │ │
│  │  - Listens to NoteSaved events            │ │
│  │  - Extracts todos via BracketTodoParser   │ │
│  │  - Reconciles with existing todos         │ │
│  │  - Auto-categorizes based on note path    │ │
│  └───────────────────────────────────────────┘ │
│                    ↓                            │
│  ┌───────────────────────────────────────────┐ │
│  │  TagService (NEW)                         │ │
│  │  - Auto-tags based on hierarchy           │ │
│  │  - Manages tag inheritance                │ │
│  │  - Provides tag search/filtering          │ │
│  └───────────────────────────────────────────┘ │
│                    ↓                            │
│  ┌───────────────────────────────────────────┐ │
│  │  CategorySyncService                      │ │
│  │  - Queries categories from tree.db        │ │
│  │  - 5-minute intelligent caching           │ │
│  │  - Validates category existence           │ │
│  └───────────────────────────────────────────┘ │
│                    ↓                            │
│  ┌───────────────────────────────────────────┐ │
│  │  CategoryStore (Observable)               │ │
│  │  - In-memory selected categories          │ │
│  │  - Persists to user_preferences           │ │
│  │  - Auto-loads on startup                  │ │
│  └───────────────────────────────────────────┘ │
│                    ↓                            │
│  ┌───────────────────────────────────────────┐ │
│  │  TodoStore (Observable)                   │ │
│  │  - In-memory todo collection              │ │
│  │  - Backed by todos.db                     │ │
│  │  - Real-time UI updates                   │ │
│  └───────────────────────────────────────────┘ │
└─────────────────────────────────────────────────┘
```

### **Event Flow**

```
User Action → Event → Handler → Update → Notify → UI Refresh

Example: User saves note with [todo]
1. User presses Ctrl+S
2. SaveManager.SaveAsync() executes
3. NoteSaved event fires
4. TodoSyncService.OnNoteSaved() handles event
5. Parser extracts todos
6. TodoRepository.InsertAsync() saves to DB
7. TodoStore.Add() adds to in-memory collection
8. CollectionChanged event fires
9. TodoListViewModel receives notification
10. UI updates (todo appears in tree)

Time: < 100ms from save to visible
```

---

## ❓ **OPEN QUESTIONS**

### **High Priority (Need Answer Before Phase 2):**

#### **Q1: Tag Display Style**
**Question:** How should tags appear in the UI?

**Options:**
- **A)** Inline hashtags: `☐ Buy materials #Projects #Urgent`
- **B)** Pill buttons: `☐ Buy materials [Projects] [Urgent]`
- **C)** Sidebar panel + inline
- **D)** Hover tooltip only

**Recommendation:** Option B (Pills) - More visual, easier to click

---

#### **Q2: Tag Colors**
**Question:** How are tag colors determined?

**Options:**
- **A)** Auto-color based on category (Projects = Blue)
- **B)** User-defined per tag
- **C)** System-generated (hash of tag name → color)
- **D)** No colors (just text)

**Recommendation:** Option A (Auto-color) with Option B override

---

#### **Q3: Recurring Task Lead Time**
**Question:** When should recurring tasks appear before due date?

**Example:**
```
Task: "Submit monthly report"
Due: 1st of every month
Lead time: 5 days

Should it appear on the 26th of previous month?
```

**Options:**
- **A)** Show exactly N days before (26th for 5 days lead)
- **B)** Show on next app launch after lead time reached
- **C)** Show in "Upcoming" smart list only
- **D)** Always visible but grayed out until lead time

**Recommendation:** Option A + Option C (visible in Upcoming list early, appears in main list at lead time)

---

### **Medium Priority (Can Defer to Phase 3/4):**

#### **Q4: Orphaned Todo Cleanup**
**Question:** What happens to orphaned todos over time?

**Options:**
- **A)** Stay in "Orphaned" category forever (manual cleanup)
- **B)** Auto-delete after N days
- **C)** Auto-archive after N days
- **D)** Prompt user periodically

**Recommendation:** Option A with Option D (prompt monthly: "You have 5 orphaned todos. Review?")

---

#### **Q5: Note Tree Tag Visibility**
**Question:** Should tags appear in the note tree itself?

**Options:**
- **A)** Yes, next to note/folder names: `📁 Projects #Work`
- **B)** No, only in search/todo tree
- **C)** Optional (user setting)
- **D)** Hover tooltip only

**Recommendation:** Option D (Hover tooltip) - Keeps tree clean

---

#### **Q6: Smart List Ordering**
**Question:** How should todos be ordered within smart lists?

**Options:**
- **A)** By due date (soonest first)
- **B)** By priority (highest first)
- **C)** By creation date (newest first)
- **D)** User-definable per smart list

**Recommendation:** Option D with defaults (Today = by due time, High Priority = by priority, All = by creation date)

---

## 📊 **SUCCESS METRICS**

### **Phase 1 Success Criteria:**
- [ ] RTF extraction: 100% of `[todo]` items captured
- [ ] Auto-categorization: 95%+ accuracy (correct category)
- [ ] Performance: < 200ms from save to todo visible
- [ ] Reliability: 0 data loss after 1000 operations
- [ ] Orphaned handling: 100% moved correctly

### **Phase 2 Success Criteria:**
- [ ] Auto-tagging: 100% of items tagged correctly
- [ ] Tag inheritance: 100% accuracy across hierarchy
- [ ] Tag search: < 50ms for 10,000 tags
- [ ] UI responsiveness: No lag with 100+ tags

### **Phase 3 Success Criteria:**
- [ ] Priority UI: Color-coded correctly
- [ ] Due dates: Accurate to the minute
- [ ] Recurring tasks: 100% appear on schedule
- [ ] Descriptions: Render markdown correctly

### **Phase 4 Success Criteria:**
- [ ] Drag-and-drop: Smooth, no lag
- [ ] Context menus: All actions work
- [ ] Unified search: < 100ms for 10,000 items
- [ ] Smart lists: Real-time updates

---

## 🎯 **NEXT STEPS**

### **Immediate (Today):**
1. **Test RTF auto-categorization** - Verify existing code works
2. **Review unified tree view** - Confirm todos nest under categories
3. **Test category persistence** - Ensure database saves/loads correctly

### **This Week:**
1. Implement "Orphaned" category
2. Add backlink UI to todos
3. Complete Phase 1

### **Next Week:**
1. Design tag service architecture
2. Implement auto-tagging rules
3. Start Phase 2

---

## 📚 **REFERENCE DOCUMENTS**

- `UNIFIED_TREEVIEW_COMPLETE.md` - Tree view implementation details
- `CATEGORY_SYNC_IMPLEMENTATION_GUIDE.md` - Original category sync plan
- `CATEGORY_VALIDATION_COMPLETE.md` - Validation system
- `DATABASE_RECREATION_COMPLETE.md` - Database schema

---

## 📝 **REVISION HISTORY**

- **v1.0** - October 10, 2025 - Initial master plan created
- Future revisions will be tracked here

---

**END OF MASTER PLAN**

