# 🧪 Comprehensive Testing Strategy for TodoPlugin

**Date:** October 9, 2025  
**Goal:** Add professional-grade testing to TodoPlugin

---

## 📊 CURRENT STATE: ZERO TESTS

### **What Exists:**
- ✅ TodoPlugin code (2,000+ lines)
- ✅ Domain layer (aggregates, value objects)
- ✅ Infrastructure (repository, database, parser)
- ✅ UI (ViewModels, Views)
- ❌ **Tests: NONE!**

### **Risk:**
- No safety net for refactoring
- Can't verify business logic
- Manual testing only
- Regression risk high

---

## 🎯 TESTING LAYERS

### **Test Pyramid:**

```
        ┌──────────────┐
        │  E2E Tests   │  ← 5% (Manual for now)
        │  (Manual)    │
        └──────────────┘
      ┌──────────────────┐
      │ Integration Tests │ ← 20%
      │  (Database+UI)    │
      └──────────────────┘
    ┌──────────────────────┐
    │    Unit Tests         │ ← 75%
    │ (Domain+Logic)        │
    └──────────────────────┘
```

**Priority:** Unit Tests first (biggest ROI)

---

## ✅ LAYER 1: UNIT TESTS (Priority 1)

### **What to Test:**

#### **A. Domain Layer Tests** (High Value)

```csharp
// Location: NoteNest.Tests/Plugins/TodoPlugin/Domain/

public class TodoAggregateTests
{
    [Test]
    public void Create_WithValidText_ShouldSucceed()
    {
        // Arrange
        var text = "Buy milk";
        
        // Act
        var result = TodoAggregate.Create(text);
        
        // Assert
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(text, result.Value.Text.Value);
        Assert.IsFalse(result.Value.IsCompleted);
    }
    
    [Test]
    public void Create_WithEmptyText_ShouldFail()
    {
        // Act
        var result = TodoAggregate.Create("");
        
        // Assert
        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual("Todo text cannot be empty", result.Error);
    }
    
    [Test]
    public void Complete_WhenNotCompleted_ShouldSucceed()
    {
        // Arrange
        var todo = TodoAggregate.Create("Test").Value;
        
        // Act
        var result = todo.Complete();
        
        // Assert
        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(todo.IsCompleted);
        Assert.IsNotNull(todo.CompletedDate);
    }
    
    [Test]
    public void Complete_WhenAlreadyCompleted_ShouldFail()
    {
        // Arrange
        var todo = TodoAggregate.Create("Test").Value;
        todo.Complete();
        
        // Act
        var result = todo.Complete();
        
        // Assert
        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual("Todo is already completed", result.Error);
    }
}

public class TodoTextTests
{
    [Test]
    public void Create_WithValidText_ShouldSucceed()
    {
        var result = TodoText.Create("Valid todo text");
        Assert.IsTrue(result.IsSuccess);
    }
    
    [Test]
    public void Create_WithTextOver1000Chars_ShouldFail()
    {
        var longText = new string('a', 1001);
        var result = TodoText.Create(longText);
        Assert.IsTrue(result.IsFailure);
    }
    
    [Test]
    public void Create_TrimsWhitespace()
    {
        var result = TodoText.Create("  todo  ");
        Assert.AreEqual("todo", result.Value.Value);
    }
}

public class DueDateTests
{
    [Test]
    public void IsOverdue_WithPastDate_ShouldReturnTrue()
    {
        var yesterday = DateTime.UtcNow.AddDays(-1);
        var dueDate = DueDate.Create(yesterday).Value;
        Assert.IsTrue(dueDate.IsOverdue());
    }
    
    [Test]
    public void IsToday_WithTodaysDate_ShouldReturnTrue()
    {
        var today = DateTime.UtcNow;
        var dueDate = DueDate.Create(today).Value;
        Assert.IsTrue(dueDate.IsToday());
    }
}
```

**Coverage:** Domain logic, validation, business rules  
**Time to Implement:** 2-3 hours  
**Value:** ⭐⭐⭐⭐⭐ (High - catches logic bugs)

---

#### **B. Parser Tests** (High Value)

```csharp
// Location: NoteNest.Tests/Plugins/TodoPlugin/Parsing/

public class BracketTodoParserTests
{
    private BracketTodoParser _parser;
    
    [SetUp]
    public void Setup()
    {
        var logger = new MockLogger();
        _parser = new BracketTodoParser(logger);
    }
    
    [Test]
    public void ExtractFromPlainText_WithValidBrackets_ShouldExtract()
    {
        // Arrange
        var text = "Meeting notes [call John] and [send email]";
        
        // Act
        var todos = _parser.ExtractFromPlainText(text);
        
        // Assert
        Assert.AreEqual(2, todos.Count);
        Assert.AreEqual("call John", todos[0].Text);
        Assert.AreEqual("send email", todos[1].Text);
    }
    
    [Test]
    public void ExtractFromPlainText_WithNestedBrackets_ShouldIgnore()
    {
        var text = "[outer [nested] text]";
        var todos = _parser.ExtractFromPlainText(text);
        Assert.AreEqual(0, todos.Count); // Should not extract nested
    }
    
    [Test]
    public void ExtractFromPlainText_WithMetadata_ShouldFilter()
    {
        var text = "[date: 2024-10-09] [TODO] [call John]";
        var todos = _parser.ExtractFromPlainText(text);
        Assert.AreEqual(1, todos.Count); // Only "call John"
        Assert.AreEqual("call John", todos[0].Text);
    }
    
    [Test]
    public void ExtractFromPlainText_TracksLineNumbers()
    {
        var text = "Line 1\n[todo 1]\nLine 3\n[todo 2]";
        var todos = _parser.ExtractFromPlainText(text);
        Assert.AreEqual(1, todos[0].LineNumber);
        Assert.AreEqual(3, todos[1].LineNumber);
    }
}
```

**Coverage:** Parsing logic, edge cases  
**Time:** 1-2 hours  
**Value:** ⭐⭐⭐⭐⭐

---

#### **C. Mapper Tests** (Medium Value)

```csharp
public class TodoMapperTests
{
    [Test]
    public void ToUiModel_PreservesAllProperties()
    {
        // Arrange
        var aggregate = TodoAggregate.Create("Test").Value;
        
        // Act
        var uiModel = TodoMapper.ToUiModel(aggregate);
        
        // Assert
        Assert.AreEqual(aggregate.Id.Value, uiModel.Id);
        Assert.AreEqual(aggregate.Text.Value, uiModel.Text);
        Assert.AreEqual(aggregate.IsCompleted, uiModel.IsCompleted);
    }
    
    [Test]
    public void ToAggregate_AndBack_RoundTrip()
    {
        // Arrange
        var original = new TodoItem { Text = "Test", Priority = Priority.High };
        
        // Act
        var aggregate = TodoMapper.ToAggregate(original);
        var result = TodoMapper.ToUiModel(aggregate);
        
        // Assert
        Assert.AreEqual(original.Text, result.Text);
        Assert.AreEqual(original.Priority, result.Priority);
    }
}
```

**Time:** 1 hour  
**Value:** ⭐⭐⭐⭐

---

### **✅ LAYER 2: INTEGRATION TESTS (Priority 2)**

#### **Database Persistence Tests:**

```csharp
// Location: NoteNest.Tests/Plugins/TodoPlugin/Integration/

public class TodoRepositoryIntegrationTests
{
    private string _testDbPath;
    private string _connectionString;
    private TodoRepository _repository;
    
    [SetUp]
    public async Task Setup()
    {
        // Use in-memory or temp file database
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
        _connectionString = $"Data Source={_testDbPath}";
        
        var logger = new MockLogger();
        var initializer = new TodoDatabaseInitializer(_connectionString, logger);
        await initializer.InitializeAsync();
        
        _repository = new TodoRepository(_connectionString, logger);
    }
    
    [TearDown]
    public void Cleanup()
    {
        if (File.Exists(_testDbPath))
            File.Delete(_testDbPath);
    }
    
    [Test]
    public async Task InsertAndRetrieve_ShouldPersist()
    {
        // Arrange
        var todo = new TodoItem { Text = "Test todo" };
        
        // Act
        var inserted = await _repository.InsertAsync(todo);
        var retrieved = await _repository.GetByIdAsync(todo.Id);
        
        // Assert
        Assert.IsTrue(inserted);
        Assert.IsNotNull(retrieved);
        Assert.AreEqual(todo.Text, retrieved.Text);
    }
    
    [Test]
    public async Task InsertMultiple_GetAll_ShouldReturnAll()
    {
        // Arrange
        var todos = new[]
        {
            new TodoItem { Text = "Todo 1" },
            new TodoItem { Text = "Todo 2" },
            new TodoItem { Text = "Todo 3" }
        };
        
        // Act
        foreach (var todo in todos)
            await _repository.InsertAsync(todo);
            
        var all = await _repository.GetAllAsync();
        
        // Assert
        Assert.AreEqual(3, all.Count);
    }
    
    [Test]
    public async Task Update_ShouldPersistChanges()
    {
        // Arrange
        var todo = new TodoItem { Text = "Original" };
        await _repository.InsertAsync(todo);
        
        // Act
        todo.Text = "Updated";
        todo.IsCompleted = true;
        await _repository.UpdateAsync(todo);
        
        var retrieved = await _repository.GetByIdAsync(todo.Id);
        
        // Assert
        Assert.AreEqual("Updated", retrieved.Text);
        Assert.IsTrue(retrieved.IsCompleted);
    }
}
```

**Time:** 2-3 hours  
**Value:** ⭐⭐⭐⭐⭐ (Critical - verifies persistence works)

---

### **✅ LAYER 3: E2E TESTS (Priority 3)**

#### **Manual Test Scripts** (For Now)

```markdown
# Test Script 1: Basic CRUD
1. Launch app
2. Add todo "Test 1"
3. Complete todo
4. Uncomplete todo
5. Delete todo
✅ All operations work

# Test Script 2: Persistence
1. Add 3 todos
2. Restart app
3. ✅ Todos persist

# Test Script 3: RTF Extraction
1. Open note
2. Type "[call John]"
3. Save
4. ✅ Todo appears in panel

# Test Script 4: Orphaned Todos
1. Add [todo] in note
2. Save note (todo created)
3. Remove [todo] from note
4. Save note
5. ✅ Todo marked orphaned (not deleted)
```

**Time:** 30 min per script  
**Value:** ⭐⭐⭐⭐ (Validates user workflows)

**Future:** Automated UI tests with WPF test frameworks

---

## 🎯 RECOMMENDED TESTING PRIORITY

### **Phase 1: Critical Tests (4-5 hours)**
```
1. Domain unit tests          (2 hrs) - Business logic
2. Repository integration     (2 hrs) - Persistence
3. Manual test scripts        (1 hr)  - User workflows
```

**Value:** Covers 80% of bugs with 20% of effort

---

### **Phase 2: Comprehensive Tests (8-10 hours)**
```
4. Parser tests               (2 hrs) - Bracket extraction
5. Sync service tests         (2 hrs) - RTF integration
6. Mapper tests               (1 hr)  - Conversions
7. ViewModel tests            (3 hrs) - UI logic
```

**Value:** Full coverage

---

### **Phase 3: Advanced Tests (Future)**
```
8. Performance tests          - Large datasets
9. Concurrency tests          - Multi-threading
10. UI automation             - WPF testing framework
```

---

## 🚀 QUICK START: Add Tests NOW

### **Create Test Project Structure:**

```bash
NoteNest.Tests/
└── Plugins/
    └── TodoPlugin/
        ├── Domain/
        │   ├── TodoAggregateTests.cs
        │   ├── TodoTextTests.cs
        │   └── DueDateTests.cs
        ├── Infrastructure/
        │   ├── TodoRepositoryTests.cs
        │   ├── TodoMapperTests.cs
        │   └── BracketParserTests.cs
        └── Integration/
            ├── PersistenceTests.cs
            └── SyncServiceTests.cs
```

**Time to Set Up:** 30 minutes  
**Time to Write Tests:** 4-5 hours  
**ROI:** Massive (catches bugs, enables refactoring)

---

## ✅ RECOMMENDATION

**Add tests AFTER verifying current implementation works!**

**Timeline:**
```
Week 1: Test manually, verify it works
Week 2: Add unit tests (domain + parser)
Week 3: Add integration tests (persistence)
Week 4: Continuous testing as features added
```

**Don't block shipping on tests, but add them soon!**

