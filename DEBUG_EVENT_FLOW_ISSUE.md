# Debugging Event Flow Issue - Investigation

**Issue:** Added todos don't appear in tree until app restart  
**Status:** Investigation In Progress  
**Expected:** Todos appear immediately after creation

---

## 🔍 Investigation Plan

### **Step 1: Verify Event Publishing**
Check if TodoCreatedEvent is actually being published

### **Step 2: Verify Event Subscription**
Check if TodoStore is receiving events

### **Step 3: Verify Collection Update**
Check if _todos collection is being updated

### **Step 4: Verify Tree Refresh**
Check if CategoryTreeViewModel is rebuilding

### **Step 5: Identify Root Cause**
Find where the chain breaks

---

## 📋 Checking Event Flow...


