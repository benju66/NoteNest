# 🧪 PROTOTYPE TEST PLAN - Option A Event-Driven DB Sync

## TEST OBJECTIVE
Verify that DatabaseMetadataUpdateService successfully updates tree_nodes metadata when notes are saved.

---

## VERIFICATION CHECKLIST

### ✅ **Step 1: Service Registration (Check Logs at Startup)**
Look for these log entries at app startup:
```
═══════════════════════════════════════════════════════════════
🔄 DatabaseMetadataUpdateService PROTOTYPE starting...
   Purpose: Test Option A - Event-driven DB metadata sync
   Subscribing to: ISaveManager.NoteSaved event
═══════════════════════════════════════════════════════════════
✅ DatabaseMetadataUpdateService subscribed to NoteSaved events
   Listening for: Manual saves, auto-saves, tab switch, tab close
```

**If you see this:** Service is registered and listening ✓

---

### ✅ **Step 2: Manual Save Test**
1. Open an existing note in the tree
2. Make a small edit (type a word)
3. Press Ctrl+S or click Save button
4. Check logs for:

```
─────────────────────────────────────────────────────────────
📝 SAVE EVENT RECEIVED:
   File: C:\Users\Burness\MyNotes\Notes\...\YourNote.rtf
   NoteId: {some-guid}
   SavedAt: 2025-10-01 XX:XX:XX.XXX
   WasAutoSave: false
   
✅ Node found in DB: YourNote (ID: {guid})
📊 File metadata: Size=XXXX bytes, Modified=2025-10-01 XX:XX:XX
💾 Updating database record...
✅ DATABASE UPDATE SUCCESS:
   Node: YourNote
   New Size: XXXX bytes
   New ModifiedAt: 2025-10-01 XX:XX:XX.XXX
   Update Duration: XX.XXms
─────────────────────────────────────────────────────────────
```

**Expected:** Event fires, DB updates successfully ✓

---

### ✅ **Step 3: Auto-Save Test**
1. Keep the note open
2. Make another edit
3. Wait ~5 seconds (auto-save timer)
4. Check logs for same pattern but with `WasAutoSave: true`

**Expected:** Auto-save also triggers DB update ✓

---

### ✅ **Step 4: Tab Switch Test**
1. Open a second note
2. Edit the first note
3. Click the second note's tab (switch without saving)
4. Check logs for save event

**Expected:** Tab switch triggers save + DB update ✓

---

### ✅ **Step 5: Save All Test**
1. Open 3-4 notes
2. Edit each one
3. Click "Save All" button
4. Check logs for multiple save events

**Expected:** Multiple events fire, all DB updates succeed ✓

---

## WHAT TO LOOK FOR IN LOGS

### ✅ SUCCESS INDICATORS:
- Service starts and subscribes
- NoteSaved events received
- Node found in database
- File metadata retrieved
- Database update succeeds
- Update duration < 50ms

### ⚠️ WARNING INDICATORS (Acceptable):
- "Node not found in DB (may be new external file)"
  → Means file was created outside MediatR flow (rare, OK)
  → FileWatcher will fix later

### ❌ FAILURE INDICATORS (Need Investigation):
- "DATABASE UPDATE FAILED: UpdateNodeAsync() returned false"
  → Row not found or constraint violation
- Exceptions in error handling blocks
- Service not starting

---

## LOG FILE LOCATIONS

Primary logs:
- `C:\Users\Burness\AppData\Local\NoteNest\Logs\notenest-{date}.log`
- `C:\Users\Burness\AppData\Local\NoteNest\debug.log`

---

## PERFORMANCE BENCHMARKS

Target metrics (from logs):
- **Event handling:** < 5ms (async dispatch)
- **DB query:** < 10ms (GetNodeByPathAsync)
- **DB update:** < 30ms (UpdateNodeAsync)
- **Total:** < 50ms per save

If slower:
- Check database file size
- Check connection pooling
- Check disk I/O

---

## EDGE CASE TESTS (If Time Permits)

### Test 1: Rapid Sequential Saves
1. Type continuously for 30 seconds
2. Auto-save fires multiple times
3. Verify: No event queue buildup, no exceptions

### Test 2: Save After Create
1. Create new note via context menu
2. Immediately type and save
3. Verify: Node found in DB (CreateNoteHandler inserted it)

### Test 3: External File Edit
1. Edit .rtf file in Notepad (outside app)
2. Save in Notepad
3. Verify: FileWatcher picks it up (not NoteSaved event)

---

## CONFIDENCE VERIFICATION

### If All Tests Pass:
- ✅ Event subscription works
- ✅ Path matching works
- ✅ TreeNode immutability handled
- ✅ Database updates work
- ✅ Performance acceptable
- ✅ Error handling robust

**Confidence → 98%**

### If Some Tests Fail:
- Document specific failure
- Check logs for root cause
- Adjust implementation
- Retest

**Confidence → Adjust based on findings**

---

## NEXT STEPS AFTER VERIFICATION

If prototype succeeds:
1. Remove verbose logging (keep key events)
2. Add optimized UpdateNodeFileMetadataAsync() for performance
3. Register DatabaseFileWatcherService for backup sync
4. Move to Category CQRS implementation

If prototype fails:
1. Analyze failure mode
2. Fix root cause
3. Retest
4. Document learnings

