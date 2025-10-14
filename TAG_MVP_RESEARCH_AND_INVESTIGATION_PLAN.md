# Tag MVP - Research & Investigation Plan

**Date:** 2025-10-14  
**Purpose:** Comprehensive understanding before implementation  
**Approach:** Methodical investigation to achieve 90%+ confidence  
**Status:** Research Plan Created

---

## 🎯 **Research Objectives**

**Goal:** Understand EVERY aspect of tag system before writing ANY code

**Target Confidence:** 90-95% before implementation begins

**Estimated Research Time:** 4-6 hours  
**Estimated Implementation Time:** 16 hours (after research)  
**Total:** 20-22 hours

---

## 📋 **Research Phase 1: Auto-Tagging Pattern Analysis (2 hours)**

### **Questions to Answer:**

**Q1: Folder Pattern Recognition**
- How to detect "25-117 - OP III" pattern reliably?
- Regex: `^\d{2}-\d{3}\s*-\s*(.+)$` - is this correct?
- What about variations? "25-117- OP III", "25-117 -OP III", etc.
- What if folder has similar pattern but isn't a project?
- How to handle false positives?

**Q2: Tag Generation Rules**
- "25-117 - OP III" → "25-117-OP-III" (replace spaces with hyphens?)
- "25-117 - OP III" → "25-117" (just project code?)
- "25-117 - OP III" → "OP-III" (just project name?)
- What's most useful for users?

**Q3: Category Folder Inclusion**
- Include "Projects" folder as tag? YES/NO
- Include "Work" folder as tag? YES/NO
- Which folders are "important enough" to tag?
- User-configurable or hardcoded?

**Q4: Folder Hierarchy Depth**
```
C:/Users/Burness/MyNotes/Notes/Projects/25-117 - OP III/Daily Notes/Test.rtf
                         ^^^^^ ^^^^^^^^ ^^^^^^^^ ^^^^^^^^^^^
                         Ignore? Include? Include? Include?
```
- Start tagging from "Notes" folder?
- Ignore system paths (C:, Users, MyNotes)?
- How deep to traverse?

**Research Deliverables:**
- [ ] Regex patterns tested with real folder names
- [ ] Tag generation rules defined with examples
- [ ] Category folder whitelist created
- [ ] Path parsing algorithm designed
- [ ] Test cases created (10+ folder path examples)

**Confidence Target:** 95%

---

## 📋 **Research Phase 2: Tag Propagation Design (1.5 hours)**

### **Questions to Answer:**

**Q1: Note → Todo Propagation**
```
Note: "Daily Notes/Meeting.rtf"
Tags: ["25-117-OP-III", "Projects", "Daily-Notes"]
Contains: [finish proposal]

Todo created from bracket:
Should inherit: All tags? Some tags? Which ones?
```
- Inherit ALL note tags?
- Filter out some (like "Daily-Notes" too specific)?
- User preference?

**Q2: Category → Todo Propagation (Quick Add)**
```
User selects category: "Daily Notes"
Category has tags: ["25-117-OP-III", "Projects"]
User quick-adds: "Review code"

Todo should get: All category tags? Auto-tags only? Manual tags only?
```
- Auto-tags only (system-managed)?
- All tags (auto + manual)?
- Just immediate parent's tags?

**Q3: Move Operation Tag Updates**
```
Todo: "Review code"
Current tags: ["25-117-OP-III", "Projects", "urgent"]
                ^^^^^^^^^^^^^^^^ ^^^^^^^^^^  ^^^^^^^
                Auto from path    Auto cat    Manual

Drag to: "23-197 - Callaway" category

Result tags should be:
Option A: ["23-197-Callaway", "Projects", "urgent"] (replace auto, keep manual)
Option B: ["25-117-OP-III", "23-197-Callaway", "Projects", "urgent"] (accumulate)
Option C: ["23-197-Callaway", "urgent"] (new auto only + manual)
```
- Which option makes most sense?
- How to distinguish auto vs manual tags?
- Store `is_auto` flag in database?

**Q4: Orphaned Todo Handling**
```
Todo: "Finish proposal"
Tags: ["25-117-OP-III", "Projects"]
Source note deleted → Todo marked orphaned

Should tags:
Option A: Stay unchanged (preserve context)
Option B: Be cleared (no longer relevant)
Option C: Add "orphaned" system tag
```

**Research Deliverables:**
- [ ] Propagation rules defined for each scenario
- [ ] Auto vs manual tag distinction designed
- [ ] Database schema for `is_auto` flag
- [ ] Move operation algorithm detailed
- [ ] Orphan handling rules decided

**Confidence Target:** 90%

---

## 📋 **Research Phase 3: Database Schema Analysis (1 hour)**

### **Questions to Answer:**

**Q1: Existing vs Needed Tables**

**Already Exists (Todos):**
```sql
todo_tags (todo_id, tag, created_at)
global_tags (tag, color, category, icon, usage_count, created_at)
```

**Need to Create (Notes):**
```sql
note_tags (note_id, tag, is_auto, created_at) ?
```

**Need to Add (Tag Metadata):**
```sql
-- Should global_tags have is_system flag?
-- Should it have tag_type (auto/manual/system)?
-- Should it have source_pattern (which regex created it)?
```

**Q2: Category Tags Storage**
Categories are virtual (not in database normally)
- Store in user_preferences as JSON?
- Create category_tags table?
- Just compute from path each time?

**Q3: Tag Performance**
- Index strategy for tag lookups?
- Caching strategy for frequently-used tags?
- How many tags expected? (100? 1000? 10000?)

**Q4: FTS5 Integration**
```sql
-- Todos already have:
CREATE VIRTUAL TABLE todos_fts (tags, ...)

-- Notes need:
CREATE VIRTUAL TABLE notes_fts (tags, ...) ?
-- Or add to existing table?
```

**Research Deliverables:**
- [ ] Complete schema design
- [ ] Migration plan (if changes needed)
- [ ] Index strategy
- [ ] FTS5 integration plan
- [ ] Performance analysis

**Confidence Target:** 95%

---

## 📋 **Research Phase 4: UI/UX Design (1.5 hours)**

### **Questions to Answer:**

**Q1: Tooltip Design**
```
Current tooltip (notes):
┌─────────────────────────────┐
│ 📁 Folder                    │
│ Notes > Projects > 25-117    │
│ Items: 0 folders, 4 notes    │
└─────────────────────────────┘

With tags:
┌─────────────────────────────┐
│ 📁 Folder                    │
│ Notes > Projects > 25-117    │
│ 🏷️ Tags: 25-117-OP-III, Projects │ ← Add this?
│ Items: 0 folders, 4 notes    │
└─────────────────────────────┘
```
- Where in tooltip?
- How to format multiple tags?
- Max tags to show?

**Q2: Tag Icon Indicator**
```
Without tags: "📄 Meeting Notes"
With tags:    "📄 Meeting Notes 🏷️"  ← Icon appears on hover? Always?
```
- Always visible vs hover-only?
- Icon size, color, position?
- Different icon for auto vs manual tags?

**Q3: Tag Picker Dialog**
```
┌─ Add Tag to Todo ────────────┐
│ ┌───────────────────────────┐ │
│ │ 25-117                  [X]│ │ ← Input with autocomplete
│ └───────────────────────────┘ │
│                                │
│ Existing Tags:                 │
│ ☑ 25-117-OP-III  (125 items) │ ← Checkbox list
│ ☐ 23-197-Callaway (89 items) │
│ ☐ Projects      (450 items)  │
│                                │
│ Or type new tag...             │
│                                │
│ [Cancel]  [Add Tag]            │
└────────────────────────────────┘
```
- Dialog vs inline editing?
- Autocomplete from global_tags?
- Show usage count?
- Group by category?

**Q4: Tag Display in Lists**
```
Todo item in tree:
  ☐ Finish proposal
     Tags: 25-117-OP-III, urgent  ← Show inline? Tooltip only?
```
- Inline badges (takes space)?
- Icon indicator only (hover for details)?
- Tooltip only (cleanest)?

**Q5: Context Menu Integration**
```
Right-click todo:
├─ Edit
├─ Set Priority >
├─ Set Due Date
├─ Toggle Favorite
├─ Tags >                    ← NEW submenu
│  ├─ 25-117-OP-III  [X]    ← Remove (has X)
│  ├─ Projects       [X]
│  ├─ urgent         [X]
│  ├─ ─────────────
│  └─ Add Tag...
└─ Delete
```
- Tags submenu vs dialog?
- Show all tags vs just top 5?
- How to add new tags?

**Research Deliverables:**
- [ ] Mockups for all UI components
- [ ] Tooltip format designed
- [ ] Tag picker workflow defined
- [ ] Context menu structure planned
- [ ] UX tested with paper prototypes

**Confidence Target:** 90%

---

## 📋 **Research Phase 5: Search Integration Analysis (1 hour)**

### **Questions to Answer:**

**Q1: FTS5 Schema**
```sql
-- Current (todos):
CREATE VIRTUAL TABLE todos_fts (id, text, description, tags, ...)

-- Triggers keep tags column updated:
tags = (SELECT GROUP_CONCAT(tag, ' ') FROM todo_tags WHERE todo_id = new.id)

-- For notes - similar approach?
```
- Do notes already have FTS5 table?
- Does it have tags column?
- Do we need triggers?

**Q2: Search Syntax**
```
User searches: "25-117"
Should find:
  - Notes with "25-117" in text ✅
  - Notes with tag "25-117-OP-III" ✅
  - Todos with "25-117" in text ✅
  - Todos with tag "25-117-OP-III" ✅

Advanced:
  "tag:25-117" - Only items with exact tag
  "#25-117" - Shorthand for tag:
  "25-117 urgent" - Items tagged 25-117 AND containing "urgent"
```
- Implement advanced syntax?
- Or just basic search with tag matching?

**Q3: Result Ranking**
```
Search "25-117":
  Result A: Note titled "25-117 Project Plan" (title match)
  Result B: Note tagged "25-117-OP-III" (tag match)
  Result C: Note with "call 25-117" in text (content match)

Ranking:
  1. Tag exact match (highest relevance)
  2. Title match
  3. Content match
```
- Boost tag matches in ranking?
- How much weight to give tags?

**Q4: Performance**
- How does FTS5 handle tag searches?
- Index optimization needed?
- Expected search speed with 1000+ items?

**Research Deliverables:**
- [ ] FTS5 schema verified/designed
- [ ] Search syntax specification
- [ ] Ranking algorithm defined
- [ ] Performance benchmarks
- [ ] Integration plan with existing search

**Confidence Target:** 90%

---

## 📋 **Research Phase 6: Integration Points Mapping (1 hour)**

### **Commands That Need Tag Support:**

**1. CreateNoteCommand**
- Extract auto-tags from file path
- Add to note_tags table
- Trigger FTS5 update

**2. CreateTodoCommand**
- Inherit tags from source note (if SourceNoteId)
- Inherit tags from selected category (if CategoryId)
- Add to todo_tags table

**3. MoveNoteCommand**
- Recalculate auto-tags from new path
- Preserve manual tags
- Update note_tags table
- Update FTS5 index

**4. MoveTodoCommand**  
- Recalculate auto-tags from new category
- Preserve manual tags
- Update todo_tags table
- Propagate to linked notes?

**5. AddTagCommand (New)**
- Validate tag name
- Add to item (note or todo)
- Update global_tags usage_count
- Mark as manual tag

**6. RemoveTagCommand (New)**
- Remove from item
- Decrement global_tags usage_count
- Cannot remove auto-tags

**Research Deliverables:**
- [ ] All integration points mapped
- [ ] Command modification plan for each
- [ ] New commands designed (AddTag, RemoveTag)
- [ ] Event flow for tag operations
- [ ] Testing plan for each integration

**Confidence Target:** 90%

---

## 📋 **Research Phase 7: Edge Cases & Error Scenarios (1 hour)**

### **Scenarios to Consider:**

**Scenario 1: Circular Tag Propagation**
```
Note: "Meeting.rtf"
  Auto-tagged: "25-117-OP-III"
  Contains: [finish proposal]

Todo: "finish proposal"
  Inherits tag: "25-117-OP-III"
  User adds manual tag: "urgent"

If we sync back to note:
  Note now has: [finish proposal] [urgent]?
  Creates circular update loop?
```
- How to prevent infinite loops?
- Should todos ever update note tags?

**Scenario 2: Tag Name Conflicts**
```
User types: "25-117"
Auto-tag exists: "25-117-OP-III"

Should:
  A) Create new tag "25-117"
  B) Suggest "25-117-OP-III"
  C) Warn about similarity
```

**Scenario 3: Tag Removal on Move**
```
Todo with 10 tags moved to new category
New category has different auto-tags
Result: All 10 tags replaced? Or added to?
```

**Scenario 4: Deleted Category**
```
Category: "25-117 - OP III" deleted from tree
100 todos tagged "25-117-OP-III"

Should:
  A) Keep tags (orphaned but searchable)
  B) Remove tags (no longer valid)
  C) Mark as "inactive" tags
```

**Scenario 5: Tag Name Changes**
```
Folder renamed: "25-117 - OP III" → "25-117 - OP IV"
100 items tagged "25-117-OP-III"

Should:
  A) Auto-update all tags to "25-117-OP-IV"
  B) Leave existing, new items get new tag
  C) Ask user
```

**Scenario 6: Performance with Many Tags**
```
Item with 20 tags
Search index with 10,000 tags
Tag picker showing 1,000 suggestions

How to:
  - Limit tags per item?
  - Optimize tag picker performance?
  - Handle large tag vocabularies?
```

**Research Deliverables:**
- [ ] All edge cases identified
- [ ] Handling strategy for each
- [ ] Validation rules defined
- [ ] Error messages designed
- [ ] Performance limits set

**Confidence Target:** 85%

---

## 📋 **Research Phase 8: Performance & Scalability (1 hour)**

### **Questions to Answer:**

**Q1: Tag Lookup Performance**
```sql
-- Lookup tags for item:
SELECT tag FROM note_tags WHERE note_id = ?

-- With 1000 items × 5 tags average = 5000 rows
-- Performance acceptable?
```
- Index strategy?
- Caching strategy?
- Lazy loading?

**Q2: Tag Autocomplete Performance**
```sql
-- Autocomplete as user types:
SELECT tag, usage_count FROM global_tags 
WHERE tag LIKE 'proj%' 
ORDER BY usage_count DESC 
LIMIT 20

-- With 10,000 global tags?
```
- Index on tag prefix?
- Cache top tags?
- Limit vocabulary size?

**Q3: FTS5 Search Performance**
```sql
-- Search by tag:
SELECT * FROM todos_fts WHERE tags MATCH '25-117-OP-III'

-- With 10,000 todos?
```
- FTS5 performance characteristics?
- Tag tokenization?
- Search optimization?

**Q4: UI Rendering Performance**
```
TreeView with 1000 items
Each item has tooltip showing 5 tags
Rendering performance?
```
- Virtualization needed?
- Tooltip generation lazy?
- Memory usage?

**Research Deliverables:**
- [ ] Performance benchmarks
- [ ] Optimization strategy
- [ ] Scalability limits defined
- [ ] Caching plan
- [ ] Index strategy

**Confidence Target:** 85%

---

## 📋 **Research Summary**

### **Total Research Time: 8-9 hours**

**Phase 1:** Auto-tagging patterns (2 hrs)  
**Phase 2:** Tag propagation design (1.5 hrs)  
**Phase 3:** Database schema (1 hr)  
**Phase 4:** UI/UX design (1.5 hrs)  
**Phase 5:** Search integration (1 hr)  
**Phase 6:** Integration points (1 hr)  
**Phase 7:** Edge cases (1 hr)  
**Phase 8:** Performance (1 hr)  

### **Deliverables:**

**Documentation:**
- [ ] Auto-tagging specification (regex, rules, examples)
- [ ] Tag propagation rules document
- [ ] Database schema design
- [ ] UI mockups and workflows
- [ ] Search syntax specification
- [ ] Integration point mapping
- [ ] Edge case handling guide
- [ ] Performance benchmarks

**Design Artifacts:**
- [ ] Folder pattern regex (tested)
- [ ] Tag generation algorithm
- [ ] Propagation flow diagrams
- [ ] Database migration scripts
- [ ] UI component sketches
- [ ] Test case scenarios (50+)

**Decision Documents:**
- [ ] Tag generation rules (approved)
- [ ] Propagation strategies (approved)
- [ ] Move operation behavior (approved)
- [ ] Auto vs manual distinction (approved)
- [ ] Performance limits (approved)

### **Confidence After Research: 90-95%**

**Then implementation:** 16 hours with high confidence

**Total project:** 24-25 hours (research + implementation)

---

## 🎯 **My Opinion**

**Research First: ✅ STRONGLY AGREE**

**Why:**
- Tag system is complex (40+ design decisions)
- Many interdependencies (notes, todos, categories, search)
- Edge cases abundant (move, delete, rename, orphan)
- UI has many options (need to choose best)
- Performance matters (could affect UX)

**8-9 hours research now saves 20+ hours of rework later!**

**Benefits:**
- High-confidence implementation (90%+)
- Fewer surprises during coding
- Better design decisions
- Less technical debt
- Faster implementation (know what to build)

**This is the RIGHT approach!** ✅

---

## 📊 **Research vs Implementation**

**Option A: Research Then Implement** ⭐
- Research: 8-9 hours (thorough)
- Implementation: 16 hours (confident)
- Total: 24-25 hours
- Success rate: 95%

**Option B: Implement While Designing**
- Research: 2 hours (minimal)
- Implementation: 20 hours (uncertain)
- Rework: 8 hours (fixing wrong decisions)
- Total: 30 hours
- Success rate: 70%

**Research-first saves 5-6 hours AND delivers better quality!**

---

## 🎯 **Recommended Approach**

**Week 1: Tag Research**
- 2-3 sessions × 3 hours each
- Complete all 8 research phases
- Document everything
- Make all design decisions
- Achieve 90%+ confidence

**Week 2: Tag Implementation**
- With clear design in hand
- Systematic implementation
- 16 hours total
- High success rate

**Result:**
- Well-designed tag system
- No rework needed
- Professional quality
- User satisfaction

**This is how enterprise software is built!** ✅

---

**Should we start Tag Research Phase 1 (Auto-Tagging Patterns)?**


