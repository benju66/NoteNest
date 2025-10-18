# 🔍 EVENT DESERIALIZATION ISSUE - INVESTIGATION

**Date:** October 18, 2025  
**Issue:** Note-linked tasks still not working after type fix  
**New Problem:** "Failed to deserialize event TodoCreatedEvent at position 131"  
**Status:** Investigating

---

## 🚨 **NEW ISSUE DISCOVERED**

### **Error from Logs:**
```
2025-10-17 21:45:56.724 [ERR] Failed to deserialize event TodoCreatedEvent
2025-10-17 21:45:56.724 [ERR] Failed to deserialize event TodoCreatedEvent at position 131
```

**Repeated dozens of times!**

---

## 🔍 **WHAT'S HAPPENING**

### **The Problem:**

**Old events** in events.db database (created before our refactoring) contain TodoId value objects that were serialized with the **old structure**. When projections try to catch up and process these events, they fail to deserialize.

### **Why:**

1. ✅ TodoCreatedEvent is NOW discovered correctly (log shows "Registered event type: TodoCreatedEvent")
2. ✅ Event count increased from 45 to 54 (9 todo events added)
3. ❌ But projection tries to process event at position 131 (old event from before fix)
4. ❌ Deserialization fails (TodoId structure changed)
5. ❌ Projection gets stuck in error loop
6. ❌ New events never get processed

---

## 📊 **ROOT CAUSES (MULTIPLE ISSUES)**

### **Issue 1: Old Events Can't Be Deserialized**

**Events.db contains:**
```json
{
  "TodoId": {
    "Value": "abc123"  // Old TodoId structure
  },
  "Text": "call John",
  "CategoryId": null
}
```

**Current TodoId expects:**
```csharp
public class TodoId : NoteNest.Domain.Common.ValueObject  // Changed base class!
```

**Result:** JSON deserializer fails because TodoId structure/namespace changed

---

### **Issue 2: No Custom Converter for TodoId**

**JsonEventSerializer.cs lines 34-36:**
```csharp
_options.Converters.Add(new NoteIdJsonConverter());  // ✅ Has converter
_options.Converters.Add(new CategoryIdJsonConverter());  // ✅ Has converter
_options.Converters.Add(new PluginIdJsonConverter());  // ✅ Has converter
// ❌ NO TodoIdJsonConverter!
```

**Result:** System.Text.Json can't properly serialize/deserialize TodoId value objects

---

### **Issue 3: Projection Gets Stuck**

**From Logs:**
```
[ERR] Failed to deserialize event TodoCreatedEvent at position 131
[DBG] Projection TreeView processed batch: 1 events (position 130)
↑ Projection processes position 130 successfully
↓ But then tries position 131 and fails
↓ Gets stuck in loop, never progresses past 131
```

**Result:**
- Projection checkpoint stuck at position 130
- New events at positions 132+ never processed
- New todos never appear in UI

---

## 🔧 **SOLUTIONS**

### **Option A: Clear Old Todo Events (QUICKEST)**

**Pros:**
- ✅ Fast (2 minutes)
- ✅ Simple (just delete old events)
- ✅ No code changes needed

**Cons:**
- ⚠️ Loses history of old todos (if any exist)
- ⚠️ Not suitable if user has important todo data

**Implementation:**
```sql
-- Delete all old todo events from events.db
DELETE FROM events WHERE event_type LIKE 'Todo%';

-- Reset projection checkpoints
UPDATE projection_metadata SET last_processed_position = 0;
```

---

### **Option B: Add TodoId JSON Converter (ROBUST)**

**Pros:**
- ✅ Handles both old and new TodoId formats
- ✅ Preserves event history
- ✅ Production-grade solution

**Cons:**
- ⚠️ Requires code changes (15 minutes)
- ⚠️ More complex

**Implementation:**
1. Create `TodoIdJsonConverter.cs` (copy NoteIdJsonConverter pattern)
2. Register in `JsonEventSerializer.cs`
3. Handle both old and new TodoId structures

---

### **Option C: Skip Failed Events (WORKAROUND)**

**Pros:**
- ✅ Allows projections to continue past errors
- ✅ Doesn't lose other event data

**Cons:**
- ❌ Silent data loss (old todos ignored)
- ❌ Not ideal for production

**Not recommended**

---

## 📋 **RECOMMENDED APPROACH**

**Option A (Clear Old Events) IF:**
- No important todo data exists yet
- App is in development/testing phase
- User can recreate any todos easily

**Option B (Add Converter) IF:**
- There are existing todos user wants to keep
- Production deployment
- Need to preserve event history

---

## 🎯 **INVESTIGATION NEEDED**

**Questions to Answer:**
1. Are there any existing todos the user wants to keep?
2. Can we see what's in position 131 of events.db?
3. Is the user's NEW test (after our fix) creating events successfully?

---

**Investigation In Progress**

