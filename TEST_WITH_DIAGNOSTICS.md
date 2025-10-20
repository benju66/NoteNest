# 🔍 Test with Enhanced Diagnostics

**Build:** 3:30 PM (with detailed CategoryId logging)  
**Status:** Ready to test with full visibility into what's happening

---

## 🧪 **Test Steps**

1. **Close app** if running

2. **Clear logs:**
   ```powershell
   Clear-Content "C:\Users\Burness\AppData\Local\NoteNest\Logs\notenest-20251020.log"
   ```

3. **Launch app**

4. **Add category:**
   - Right-click "25-117 - OP III" → "Add to todos"

5. **Create note-linked todo:**
   - Open: `25-117 - OP III\Daily Notes\Note 2025.10.20 - 10.24.rtf`
   - Add: `[diagnostic test]`
   - Save

6. **Check logs for:**
   ```
   [TodoView] Event CategoryId: b9d84b31...
   [TodoView] Looking up category in tree_view: b9d84b31...
   [TodoView] ✅ Category found: 25-117 - OP III   OR
   [TodoView] ⚠️ Category NOT found in tree_view
   [TodoView] ✅ WROTE to todo_view: 'diagnostic test' | CategoryId: b9d84b31... | CategoryName: ...
   ```

---

## 📊 **What This Will Tell Us**

### **Scenario A: Category Lookup Succeeds**
```
[TodoView] Event CategoryId: b9d84b31...
[TodoView] ✅ Category found: 25-117 - OP III
[TodoView] ✅ WROTE to todo_view: ... | CategoryId: b9d84b31... | CategoryName: 25-117 - OP III
```

**Then later:**
```
[TodoStore] ✅ Todo loaded from database: ... CategoryId:  ← Still empty!
```

**Conclusion:** CategoryId IS being written but NOT being read! Query/mapping issue.

---

### **Scenario B: Category Lookup Fails**
```
[TodoView] Event CategoryId: b9d84b31...
[TodoView] ⚠️ Category NOT found in tree_view: b9d84b31...
[TodoView] ✅ WROTE to todo_view: ... | CategoryId: b9d84b31... | CategoryName: NULL
```

**Conclusion:** Category not in tree_view, but CategoryId should still be stored.  
**Action:** Verify if CategoryId alone (without name) is sufficient.

---

### **Scenario C: Event Has No CategoryId**
```
[TodoView] Event CategoryId: NULL
[TodoView] ✅ WROTE to todo_view: ... | CategoryId: NULL
```

**Conclusion:** CategoryId is being lost BEFORE the projection!  
**Action:** Check CreateTodoHandler and TodoAggregate.CreateFromNote().

---

## 🎯 **Run This Test**

**After testing, send me the logs with these key lines:**

```powershell
$log = Get-Content "C:\Users\Burness\AppData\Local\NoteNest\Logs\notenest-20251020.log"
$log | Select-String -Pattern "TodoView.*Event CategoryId|TodoView.*Category found|TodoView.*WROTE to todo_view|TodoStore.*loaded from database.*diagnostic"
```

This will show the complete data flow and pinpoint exactly where CategoryId is lost.

---

**Ready to test!** This diagnostic logging will give us the answer.

