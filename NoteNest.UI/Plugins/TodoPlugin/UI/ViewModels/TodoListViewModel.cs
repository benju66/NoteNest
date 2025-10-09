using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using NoteNest.Core.Commands;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Models;
using NoteNest.UI.Plugins.TodoPlugin.Services;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.UI.ViewModels
{
    /// <summary>
    /// Main view model for the Todo list view.
    /// </summary>
    public class TodoListViewModel : ViewModelBase
    {
        private readonly ITodoStore _todoStore;
        private readonly IAppLogger _logger;
        
        private ObservableCollection<TodoItemViewModel> _todos;
        private TodoItemViewModel? _selectedTodo;
        private Guid? _selectedCategoryId;
        private SmartListType? _selectedSmartList;
        private string _filterText = string.Empty;
        private string _quickAddText = string.Empty;
        private bool _isLoading;

        public TodoListViewModel(
            ITodoStore todoStore,
            IAppLogger logger)
        {
            _todoStore = todoStore ?? throw new ArgumentNullException(nameof(todoStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _logger.Info("📋 TodoListViewModel constructor called");
            
            _todos = new ObservableCollection<TodoItemViewModel>();
            
            InitializeCommands();
            _logger.Info("📋 TodoListViewModel initialized, commands ready");
            
            // Load todos after construction completes
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                new Action(async () =>
                {
                    try
                    {
                        await LoadTodosAsync();
                        _logger.Info("📋 Initial todos loaded");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "❌ Failed to load initial todos");
                    }
                }));
        }

        #region Properties

        public ObservableCollection<TodoItemViewModel> Todos
        {
            get => _todos;
            set => SetProperty(ref _todos, value);
        }

        public TodoItemViewModel? SelectedTodo
        {
            get => _selectedTodo;
            set => SetProperty(ref _selectedTodo, value);
        }

        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    _ = ApplyFilterAsync();
                }
            }
        }

        public string QuickAddText
        {
            get => _quickAddText;
            set
            {
                if (SetProperty(ref _quickAddText, value))
                {
                    _logger.Debug($"📋 QuickAddText changed to: '{value}', IsNullOrWhiteSpace={string.IsNullOrWhiteSpace(value)}");
                    // Notify command to re-evaluate CanExecute
                    (QuickAddCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public Guid? SelectedCategoryId
        {
            get => _selectedCategoryId;
            set
            {
                if (SetProperty(ref _selectedCategoryId, value))
                {
                    _selectedSmartList = null;
                    OnPropertyChanged(nameof(SelectedSmartList));
                    _ = LoadTodosAsync();
                }
            }
        }

        public SmartListType? SelectedSmartList
        {
            get => _selectedSmartList;
            set
            {
                if (SetProperty(ref _selectedSmartList, value))
                {
                    _selectedCategoryId = null;
                    OnPropertyChanged(nameof(SelectedCategoryId));
                    _ = LoadTodosAsync();
                }
            }
        }

        #endregion

        #region Commands

        public ICommand QuickAddCommand { get; private set; }
        public ICommand ToggleCompletionCommand { get; private set; }
        public ICommand DeleteTodoCommand { get; private set; }
        public ICommand EditTodoCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand ClearFilterCommand { get; private set; }

        private void InitializeCommands()
        {
            QuickAddCommand = new AsyncRelayCommand(ExecuteQuickAdd, CanExecuteQuickAdd);
            ToggleCompletionCommand = new AsyncRelayCommand<TodoItemViewModel>(ExecuteToggleCompletion);
            DeleteTodoCommand = new AsyncRelayCommand<TodoItemViewModel>(ExecuteDeleteTodo);
            EditTodoCommand = new RelayCommand<TodoItemViewModel>(ExecuteEditTodo);
            RefreshCommand = new AsyncRelayCommand(LoadTodosAsync);
            ClearFilterCommand = new RelayCommand(() => FilterText = string.Empty);
        }

        private bool CanExecuteQuickAdd()
        {
            var canExecute = !string.IsNullOrWhiteSpace(QuickAddText);
            _logger.Debug($"📋 CanExecuteQuickAdd called: {canExecute}, Text='{QuickAddText}'");
            return canExecute;
        }

        private async Task ExecuteQuickAdd()
        {
            _logger.Info($"🚀 ExecuteQuickAdd CALLED! Text='{QuickAddText}'");
            
            if (string.IsNullOrWhiteSpace(QuickAddText))
            {
                _logger.Warning("⚠️ QuickAddText is null/whitespace, aborting");
                return;
            }

            try
            {
                _logger.Info("📋 Setting IsLoading = true");
                IsLoading = true;
                
                var todo = new TodoItem
                {
                    Text = QuickAddText.Trim(),
                    CategoryId = _selectedCategoryId
                };
                
                _logger.Info($"📋 Created TodoItem: {todo.Text}, Id={todo.Id}");
                
                // Actually await the save
                _logger.Info("📋 Calling TodoStore.AddAsync...");
                await _todoStore.AddAsync(todo);
                _logger.Info($"✅ TodoStore.AddAsync completed successfully");
                
                _logger.Info($"✅ Created and saved todo: {todo.Text}");
                
                QuickAddText = string.Empty;
                _logger.Info("📋 Cleared QuickAddText");
                
                // Add to UI directly
                _logger.Info("📋 Creating TodoItemViewModel...");
                var vm = new TodoItemViewModel(todo, _todoStore, _logger);
                _logger.Info($"📋 TodoItemViewModel created, adding to Todos collection (current count={Todos.Count})");
                
                Todos.Add(vm);
                _logger.Info($"✅ Todo added to UI! New count={Todos.Count}");
                
                OnPropertyChanged(nameof(Todos));
                _logger.Info("📋 PropertyChanged raised for Todos");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ EXCEPTION in ExecuteQuickAdd!");
                _logger.Error(ex, $"❌ Exception details: {ex.GetType().Name}: {ex.Message}");
                _logger.Error(ex, $"❌ Stack trace: {ex.StackTrace}");
            }
            finally
            {
                IsLoading = false;
                _logger.Info("📋 IsLoading = false");
            }
        }

        private async Task ExecuteToggleCompletion(TodoItemViewModel? todoVm)
        {
            if (todoVm == null) return;

            try
            {
                var todo = _todoStore.GetById(todoVm.Id);
                if (todo != null)
                {
                    todo.IsCompleted = !todo.IsCompleted;
                    todo.CompletedDate = todo.IsCompleted ? DateTime.UtcNow : null;
                    
                    await _todoStore.UpdateAsync(todo);
                    _logger.Info($"✅ Todo updated: {todo.Text}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error toggling todo completion");
            }
        }

        private async Task ExecuteDeleteTodo(TodoItemViewModel? todoVm)
        {
            if (todoVm == null) return;

            try
            {
                await _todoStore.DeleteAsync(todoVm.Id);
                _logger.Info($"✅ Todo deleted");
                
                // Remove from UI
                Todos.Remove(todoVm);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting todo");
            }
        }

        private void ExecuteEditTodo(TodoItemViewModel? todoVm)
        {
            if (todoVm == null) return;
            
            SelectedTodo = todoVm;
            // This will trigger the details panel to show
        }

        #endregion

        #region Methods

        private async Task LoadTodosAsync()
        {
            try
            {
                _logger.Info("📋 LoadTodosAsync started");
                IsLoading = true;
                
                ObservableCollection<TodoItem> todos;
                
                if (_selectedSmartList.HasValue)
                {
                    _logger.Info($"📋 Loading smart list: {_selectedSmartList.Value}");
                    todos = _todoStore.GetSmartList(_selectedSmartList.Value);
                }
                else if (_selectedCategoryId != null)
                {
                    _logger.Info($"📋 Loading by category: {_selectedCategoryId}");
                    todos = _todoStore.GetByCategory(_selectedCategoryId);
                }
                else
                {
                    // Load all todos (default to Today smart list)
                    _logger.Info("📋 Loading default (Today) smart list");
                    _selectedSmartList = SmartListType.Today;
                    OnPropertyChanged(nameof(SelectedSmartList));
                    todos = _todoStore.GetSmartList(SmartListType.Today);
                }
                
                _logger.Info($"📋 Retrieved {todos.Count} todos");
                
                // Convert to view models
                Todos.Clear();
                foreach (var todo in todos)
                {
                    var vm = new TodoItemViewModel(todo, _todoStore, _logger);
                    Todos.Add(vm);
                }
                
                _logger.Info($"📋 Created {Todos.Count} view models");
                
                await ApplyFilterAsync();
                _logger.Info("📋 LoadTodosAsync completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ Error loading todos");
                // Don't rethrow - just log and continue
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ApplyFilterAsync()
        {
            await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(_filterText))
                {
                    // Show all todos
                    foreach (var todo in Todos)
                    {
                        todo.IsVisible = true;
                    }
                }
                else
                {
                    // Apply filter
                    var filterLower = _filterText.ToLowerInvariant();
                    foreach (var todo in Todos)
                    {
                        todo.IsVisible = todo.Text.ToLowerInvariant().Contains(filterLower) ||
                                       (todo.Description?.ToLowerInvariant().Contains(filterLower) ?? false) ||
                                       todo.Tags.Any(t => t.ToLowerInvariant().Contains(filterLower));
                    }
                }
            });
        }

        #endregion
    }
}
