# ⚠️ CRITICAL FINDINGS - Read Before Proceeding

**Date:** October 9, 2025  
**Status:** 🔴 **BLOCKING BUG FOUND + RTF PLAN READY**

---

## 🚨 ISSUE #1: YOU WERE RIGHT - TODOS ARE HIDDEN!

### **The Bug:**

**Current behavior:** User adds todo → **Disappears immediately!**

**Why:** Default view is "Today", which requires todos to have a due date. New todos have `DueDate = null`, so they're filtered out.

**Fix:** 5 minutes to update LINQ logic

**Impact:** CRITICAL - Blocks all testing until fixed

---

## ✅ ISSUE #2: RTF INTEGRATION PLAN

### **Your Proposed Features (Analyzed):**

| Feature | Complexity | Timeline | Recommendation |
|---------|-----------|----------|----------------|
| `[bracket]` syntax | LOW | 2-3 days | ✅ **DO FIRST** |
| `TODO:` keyword | MEDIUM | 3-4 days | ⏳ **DEFER** - Add later if requested |
| Toolbar button | MEDIUM-HIGH | 1 week | ⏳ **DEFER** - Probably not worth it |
| Todo→Note update | MEDIUM (visual) | 3-4 days | ✅ **DO AFTER BRACKETS** |
| Todo→Note update | HIGH (modify RTF) | 1-2 weeks | ⏳ **DEFER** - Maybe never |

---

## 🎯 MY RECOMMENDATIONS

### **DECISION #1: Fix UI Bug IMMEDIATELY**

**Options:**
- **A.** Quick LINQ fix (5 min) - Works now, not ideal architecture
- **B.** Refactor to async (30 min) - Proper architecture, slight delay

**Which do you prefer?**
- Fast fix to unblock testing? → Choose A
- Proper architecture from start? → Choose B

---

### **DECISION #2: RTF Parser Scope**

**My Recommendation:**

✅ **START WITH:**
1. Bracket parser `[text]` only
2. One-way sync (note → todo)
3. Visual indicators for completion (non-destructive)

⏳ **ADD LATER (if users want it):**
1. `TODO:` keyword (after brackets proven)
2. Toolbar button (if users request it)
3. RTF file modification (if visual indicators insufficient)

**Rationale:**
- Brackets solve 90% of the use case
- Simple, unambiguous, fast to implement
- Get feedback before adding complexity
- Avoid over-engineering

**Do you agree?**

---

### **DECISION #3: Bidirectional Sync Approach**

**Two Approaches:**

**Approach A: Visual Indicators (Non-Destructive)** ⭐ **RECOMMENDED**
```
Complete todo → Green highlight shows in note editor
                 ✅ File unchanged
                 ✅ Safe
                 ✅ Fast (3-4 days)
```

**Approach B: Modify RTF File**
```
Complete todo → File modified with ✓ or strikethrough
                 ⚠️ File changed
                 ⚠️ Corruption risk
                 ⚠️ Slow (1-2 weeks)
```

**My Recommendation:** 
- Start with Approach A (visual indicators)
- Ask users after 2 weeks: "Want file modification?"
- Only add Approach B if strong demand

**Do you agree?**

---

## 📋 PROPOSED IMPLEMENTATION ORDER

### **TODAY (5 minutes):**
1. ✅ Fix UI visibility bug

### **THIS WEEK (3-4 days):**
2. ✅ Test persistence (verify database works)
3. ✅ Create BracketTodoParser
4. ✅ Subscribe to NoteSavedEvent
5. ✅ Basic one-way sync (note → todo)

### **NEXT WEEK (1 week):**
6. ✅ Reconciliation (handle edited notes)
7. ✅ Orphan management
8. ✅ UI indicator for note-linked todos (📄 icon)

### **WEEK 3 (4-5 days):**
9. ✅ Visual indicators (green highlight overlay)
10. ✅ Tooltip with completion info
11. ✅ End-to-end testing

### **DEFERRED:**
- ⏳ TODO: keyword
- ⏳ Toolbar button
- ⏳ RTF file modification

**Total Timeline: 2-3 weeks to working RTF integration**

---

## ⚠️ IMPORTANT NOTES

### **About "TODO: keyword":**

You asked: *"Should this be added later after brackets?"*

**My Answer: YES!** Here's why:

**Bracket syntax:**
- ✅ Explicit (user MEANS to create a todo)
- ✅ Simple to parse (clear delimiters)
- ✅ No false positives
- ✅ 2-3 days to implement

**TODO: keyword:**
- ⚠️ Ambiguous (might be prose, not a task)
- ⚠️ Needs confidence scoring
- ⚠️ False positives likely
- ⚠️ 3-4 days + tuning

**Better approach:**
1. Ship brackets first
2. See how users actually write notes
3. If they use "TODO:" naturally, add parser
4. If they adapt to brackets, save the work!

---

### **About Toolbar Button:**

You asked: *"Perhaps these features should be added later after the brackets?"*

**My Answer: YES, much later (or never)!** Here's why:

**Typing brackets is already fast:**
```
User workflow: Select text → Type [ → Type ]
Total keystrokes: 2
```

**Toolbar button workflow:**
```
User workflow: Select text → Move mouse → Click button
Total time: 2-3 seconds (slower!)
```

**Implementation cost:** 1 week of work

**Value:** Minimal (keyboard is faster)

**Recommendation:** 
- Skip this entirely unless users specifically request it
- Most todo apps don't have this feature
- Power users prefer keyboard

---

### **About RTF File Modification:**

**You correctly said: "Maybe we work on this later"**

**Absolutely correct!** Here's the risk assessment:

| Risk | Visual Indicators | RTF Modification |
|------|-------------------|------------------|
| File corruption | None | HIGH |
| Lost work | None | Possible |
| Conflict resolution | Not needed | Complex |
| External edits | Handles fine | Can break |
| Reversibility | Always | Hard to undo |
| Implementation time | 3-4 days | 1-2 weeks |

**Recommendation:**
- Phase 2-3: Visual indicators only
- Phase 5+: Add RTF modification if users strongly request it
- **Most users won't need it!** Visual indicators are enough.

---

## ✅ CONFIRMATION CHECKLIST

Before I implement, please confirm:

**UI Bug:**
- [ ] Proceed with fix?
- [ ] Quick fix (Option A) or proper async (Option B)?

**RTF Parser:**
- [ ] Start with brackets only?
- [ ] Defer TODO: keyword?
- [ ] Skip toolbar button entirely?

**Bidirectional Sync:**
- [ ] Visual indicators first (non-destructive)?
- [ ] RTF modification deferred to Phase 5+?

**Timeline:**
- [ ] Allocate 2-3 weeks for RTF integration?
- [ ] Or prefer faster minimal path?

---

## 🎯 MY STRONG RECOMMENDATION

**Fix UI bug now** (5 min) **→** **Test persistence** (15 min) **→** **Build bracket parser** (2-3 days)

**Then see how it feels with real usage before adding more complexity.**

You've correctly identified that:
- TODO: keyword should wait
- Toolbar button should wait
- RTF modification should wait

**Trust your instincts - they're architecturally sound!**

**Start simple, iterate based on real usage.** 🎯

---

**Ready to proceed? Please confirm the approach and I'll implement!**

