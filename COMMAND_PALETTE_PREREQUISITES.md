# 🚧 Command Palette Integration - Prerequisites Checklist

**What must be completed in NoteNest before implementing Command Palette extension**

---

## 🎯 **EXECUTIVE SUMMARY**

**Critical Prerequisites:** 3 items  
**Important Prerequisites:** 4 items  
**Nice-to-Have:** 2 items  
**Total Estimated Time:** 1-2 weeks

**Status:** Most core functionality is ready ✅  
**Blockers:** Search system initialization, IPC infrastructure, error handling

---

## 🔴 **CRITICAL PREREQUISITES (MUST COMPLETE)**

### **1. Search System Initialization** 🚨 **BLOCKER**

**Current Status:** ⚠️ **INCOMPLETE** - Search service may not initialize properly

**Issue:**
- Search service needs explicit initialization at startup
- Event wiring for search index updates may be broken
- No user feedback when index is building

**Evidence:**
- `SEARCH_FIX_IMPLEMENTATION_PLAN.md` documents missing initialization
- `SEARCH_FIX_QUICK_SUMMARY.md` shows search never initializes at startup

**Required Fixes:**
1. ✅ Initialize `FTS5SearchService` at app startup (`App.xaml.cs`)
2. ✅ Create `SearchIndexSyncService` to wire save events to search updates
3. ✅ Add status feedback in `SearchViewModel` for index building
4. ✅ Thread-safe initialization with proper error handling

**Files to Modify:**
- `NoteNest.UI/App.xaml.cs` - Add initialization call
- `NoteNest.Core/Services/Search/SearchIndexSyncService.cs` - NEW FILE
- `NoteNest.UI/Composition/CleanServiceConfiguration.cs` - Register service
- `NoteNest.UI/ViewModels/SearchViewModel.cs` - Add status feedback

**Estimated Time:** 45-60 minutes  
**Confidence:** 96% (well-documented fix)

**Why Critical:**
- Command Palette "Search Notes" command depends on working search
- Users expect instant search results
- Broken search = broken Command Palette integration

---

### **2. IPC Server Infrastructure** 🚨 **BLOCKER**

**Current Status:** ❌ **DOES NOT EXIST** - Must be built from scratch

**What's Needed:**
- Named Pipe server for IPC communication
- Command handler registry
- Request/response protocol
- Error handling and timeout management
- Service provider access for DI resolution

**Required Components:**

#### **2.1 IPC Server Service**
```csharp
// NoteNest.Core/Services/Ipc/NoteNestIpcServer.cs
public class NoteNestIpcServer : IHostedService
{
    // Named pipe server
    // Command handler registry
    // Request deserialization
    // Service resolution
    // Response serialization
}
```

#### **2.2 IPC Protocol Contract**
```csharp
// NoteNest.CommandPalette.Contracts/IpcRequest.cs
// NoteNest.CommandPalette.Contracts/IpcResponse.cs
// Shared types for communication
```

#### **2.3 Command Handlers**
```csharp
// NoteNest.Core/Services/Ipc/Handlers/
// - CreateNoteHandler.cs
// - SearchNotesHandler.cs
// - OpenNoteHandler.cs
// - ListRecentNotesHandler.cs
// etc.
```

**Estimated Time:** 2-3 days  
**Confidence:** 90% (standard IPC patterns)

**Why Critical:**
- Without IPC server, Command Palette cannot communicate with NoteNest
- This is the foundation of the entire integration

---

### **3. Robust Error Handling** 🚨 **BLOCKER**

**Current Status:** ⚠️ **PARTIAL** - Commands have validation, but IPC needs error handling

**What's Needed:**

#### **3.1 IPC Error Handling**
- Connection failures (NoteNest not running)
- Timeout handling (commands taking too long)
- Invalid request handling
- Service unavailable errors
- Graceful degradation

#### **3.2 Command-Level Error Handling**
- Validation errors (already exists via FluentValidation)
- Business logic errors (category not found, etc.)
- File system errors (permissions, disk full)
- Database errors (connection lost)

#### **3.3 User-Friendly Error Messages**
- Clear error messages for Command Palette
- Actionable suggestions (e.g., "NoteNest is not running. Start it?")
- Error codes for extension to handle appropriately

**Required Implementation:**
```csharp
// Error response structure
public class IpcErrorResponse
{
    public string ErrorCode { get; set; } // "NOTENEST_NOT_RUNNING", "CATEGORY_NOT_FOUND", etc.
    public string Message { get; set; }
    public string UserMessage { get; set; } // Human-readable
    public bool CanRetry { get; set; }
    public Dictionary<string, object> Context { get; set; }
}
```

**Estimated Time:** 1 day  
**Confidence:** 95% (standard error handling patterns)

**Why Critical:**
- External access must handle errors gracefully
- Users need clear feedback when things go wrong
- Prevents cryptic failures that confuse users

---

## 🟠 **IMPORTANT PREREQUISITES (SHOULD COMPLETE)**

### **4. Service Stability & Testing** ⚠️

**Current Status:** ✅ **MOSTLY STABLE** - Core services work, but need validation

**What's Needed:**
- Test all CQRS commands that will be exposed
- Verify error handling in handlers
- Test edge cases (empty categories, invalid paths, etc.)
- Performance testing (ensure commands complete in <500ms)

**Commands to Test:**
- ✅ `CreateNoteCommand` - Already has validation
- ✅ `DeleteNoteCommand` - Has confirmation logic
- ✅ `RenameNoteCommand` - Has validation
- ⚠️ `SearchNotes` - Needs testing (see #1)
- ⚠️ Category operations - Need validation

**Estimated Time:** 1-2 days  
**Confidence:** 85% (most commands already tested in UI)

**Why Important:**
- External access exposes services to more edge cases
- Need confidence that services won't crash
- Performance matters for Command Palette UX

---

### **5. Category Query Service** ⚠️

**Current Status:** ✅ **EXISTS** - `ITreeQueryService` available

**What's Needed:**
- Verify category listing works correctly
- Test hierarchical category queries
- Ensure category path resolution works
- Test category creation via Command Palette

**Verification:**
- Can query all categories
- Can get category by ID
- Can get category by path
- Can create categories programmatically

**Estimated Time:** 4-6 hours  
**Confidence:** 90% (service exists, just needs verification)

**Why Important:**
- "Browse Categories" command needs this
- "Create Category" command needs this
- Category selection in forms needs this

---

### **6. Recent Notes Query** ⚠️

**Current Status:** ⚠️ **NEEDS IMPLEMENTATION** - No dedicated recent notes query

**What's Needed:**
- Query service to get recently modified notes
- Sort by modification date
- Limit results (e.g., last 10)
- Include metadata (title, category, modified date)

**Implementation:**
```csharp
// NoteNest.Application/Queries/GetRecentNotes/GetRecentNotesQuery.cs
public class GetRecentNotesQuery : IRequest<Result<List<RecentNoteDto>>>
{
    public int Limit { get; set; } = 10;
    public DateTime? Since { get; set; } // Optional filter
}

// Returns: List of notes with title, category path, modified date
```

**Estimated Time:** 4-6 hours  
**Confidence:** 95% (straightforward query)

**Why Important:**
- "Recent Notes" command is a key feature
- Users expect quick access to recent work
- High-value feature for productivity

---

### **7. Auto-Start NoteNest Capability** ⚠️

**Current Status:** ❌ **DOES NOT EXIST** - Extension needs to start NoteNest if not running

**What's Needed:**
- IPC server should detect if NoteNest is running
- If not running, extension can start it
- Wait for NoteNest to be ready before sending commands
- Handle startup failures gracefully

**Implementation Options:**

**Option A: Extension Starts NoteNest**
```csharp
// In Command Palette extension
if (!IsNoteNestRunning())
{
    Process.Start("NoteNest.exe", "--ipc-server");
    await WaitForNoteNestReadyAsync(timeout: 10_000);
}
```

**Option B: IPC Server Auto-Starts**
```csharp
// In IPC server
if (!IsMainWindowOpen())
{
    // Show window or start background mode
    _application.ShowMainWindow();
}
```

**Recommendation:** Option A (extension starts NoteNest)

**Estimated Time:** 4-6 hours  
**Confidence:** 90% (standard process management)

**Why Important:**
- Users expect Command Palette to "just work"
- Don't want to manually start NoteNest first
- Seamless experience is key

---

## 🟢 **NICE-TO-HAVE (CAN DEFER)**

### **8. Performance Optimization** 💡

**Current Status:** ✅ **ADEQUATE** - Most operations are fast

**What Could Be Improved:**
- Search index warm-up (pre-load at startup)
- Category tree caching
- Recent notes caching
- Connection pooling for IPC

**Estimated Time:** 1-2 days  
**Priority:** LOW (can optimize after MVP)

**Why Nice-to-Have:**
- Current performance is acceptable
- Can optimize based on real-world usage
- Not blocking for MVP

---

### **9. Comprehensive Logging** 💡

**Current Status:** ✅ **EXISTS** - Logging infrastructure present

**What Could Be Enhanced:**
- IPC request/response logging
- Command execution timing
- Error context logging
- Performance metrics

**Estimated Time:** 4-6 hours  
**Priority:** LOW (logging exists, just needs enhancement)

**Why Nice-to-Have:**
- Helps with debugging
- Useful for performance analysis
- Not required for MVP

---

## 📊 **PREREQUISITES SUMMARY**

### **Critical Path (Must Complete):**

| # | Prerequisite | Status | Time | Priority |
|---|--------------|--------|------|----------|
| 1 | Search System Initialization | ⚠️ Incomplete | 1 hour | 🔴 CRITICAL |
| 2 | IPC Server Infrastructure | ❌ Missing | 2-3 days | 🔴 CRITICAL |
| 3 | Robust Error Handling | ⚠️ Partial | 1 day | 🔴 CRITICAL |

**Total Critical Time:** 3-4 days

---

### **Important Path (Should Complete):**

| # | Prerequisite | Status | Time | Priority |
|---|--------------|--------|------|----------|
| 4 | Service Stability & Testing | ✅ Mostly Ready | 1-2 days | 🟠 IMPORTANT |
| 5 | Category Query Service | ✅ Exists | 4-6 hours | 🟠 IMPORTANT |
| 6 | Recent Notes Query | ⚠️ Needs Implementation | 4-6 hours | 🟠 IMPORTANT |
| 7 | Auto-Start Capability | ❌ Missing | 4-6 hours | 🟠 IMPORTANT |

**Total Important Time:** 2-3 days

---

### **Total Estimated Time:**

**Minimum (Critical Only):** 3-4 days  
**Recommended (Critical + Important):** 5-7 days  
**Complete (All):** 7-10 days

---

## ✅ **WHAT'S ALREADY READY**

### **Core Services (Ready):**
- ✅ `CreateNoteCommand` - Fully implemented with validation
- ✅ `DeleteNoteCommand` - Fully implemented
- ✅ `RenameNoteCommand` - Fully implemented
- ✅ `ISearchService` - Exists (needs initialization fix)
- ✅ `ITreeQueryService` - Exists and works
- ✅ `IMediator` - CQRS infrastructure ready
- ✅ FluentValidation - Validation pipeline ready
- ✅ Error handling in commands - `Result<T>` pattern

### **Architecture (Ready):**
- ✅ CQRS pattern established
- ✅ Dependency Injection configured
- ✅ Event sourcing infrastructure
- ✅ Database access patterns
- ✅ Service interfaces defined

---

## 🎯 **RECOMMENDED IMPLEMENTATION ORDER**

### **Phase 1: Foundation (Week 1)**
1. ✅ Fix Search System Initialization (1 hour)
2. ✅ Implement IPC Server Infrastructure (2-3 days)
3. ✅ Add Robust Error Handling (1 day)

**Result:** Basic IPC communication working

---

### **Phase 2: Core Features (Week 2)**
4. ✅ Implement Recent Notes Query (4-6 hours)
5. ✅ Verify Category Query Service (4-6 hours)
6. ✅ Test Service Stability (1-2 days)
7. ✅ Add Auto-Start Capability (4-6 hours)

**Result:** All MVP commands ready

---

### **Phase 3: Polish (Week 3)**
8. ⏳ Performance Optimization (if needed)
9. ⏳ Enhanced Logging (if needed)

**Result:** Production-ready integration

---

## 🚦 **GO/NO-GO CRITERIA**

### **✅ GO (Can Start Command Palette Extension):**
- ✅ Search system initializes correctly
- ✅ IPC server accepts connections
- ✅ At least 3 commands working (Create, Search, Open)
- ✅ Error handling returns user-friendly messages
- ✅ Basic testing completed

### **❌ NO-GO (Wait Before Starting):**
- ❌ Search doesn't work
- ❌ IPC server crashes on connection
- ❌ Commands fail silently
- ❌ No error handling
- ❌ Services unstable

---

## 📝 **ACTION ITEMS**

### **Immediate (This Week):**
- [ ] Fix search system initialization
- [ ] Design IPC protocol contract
- [ ] Create IPC server skeleton

### **Next Week:**
- [ ] Implement IPC command handlers
- [ ] Add error handling
- [ ] Implement recent notes query
- [ ] Test all commands

### **Following Week:**
- [ ] Add auto-start capability
- [ ] Performance testing
- [ ] Documentation

---

## ✅ **CONCLUSION**

**Most of NoteNest is ready for Command Palette integration!** ✅

**Main Work Required:**
1. Fix search initialization (quick fix)
2. Build IPC infrastructure (new work, but straightforward)
3. Add error handling (standard patterns)

**Estimated Time to Ready:** 5-7 days of focused work

**Confidence:** 90% - All prerequisites are well-understood and achievable

**Recommendation:** Start with Phase 1 (Foundation) to unblock Command Palette development.

