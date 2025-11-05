using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MediatR;
using NoteNest.Core.Commands;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Queries;
using NoteNest.UI.Plugins.TodoPlugin.Application.Queries;
using NoteNest.UI.Plugins.TodoPlugin.Models;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.UI.ViewModels
{
    /// <summary>
    /// Main view model for the Todo list view.
    /// Event-sourced version - uses ITodoQueryService.
    /// </summary>
    public class TodoListViewModel : ViewModelBase
    {
        private readonly ITodoQueryService _todoQueryService;
        private readonly ITagQueryService _tagQueryService;
        private readonly IMediator _mediator;
        private readonly IAppLogger _logger;
        
        private ObservableCollection<TodoItemViewModel> _todos;
        private TodoItemViewModel? _selectedTodo;
        private Guid? _selectedCategoryId;
        private Models.SmartListType? _selectedSmartList;
        private string _filterText = string.Empty;
        private string _quickAddText = string.Empty;
        private bool _isLoading;

        public TodoListViewModel(
            ITodoQueryService todoQueryService,
            ITagQueryService tagQueryService,
            IMediator mediator,
            IAppLogger logger)
        {
            _todoQueryService = todoQueryService ?? throw new ArgumentNullException(nameof(todoQueryService));
            _tagQueryService = tagQueryService ?? throw new ArgumentNullException(nameof(tagQueryService));
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
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
        
        #region Helper Methods
        
        // Helper method to convert TodoDto to TodoItem
        private TodoItem ConvertDtoToTodoItem(TodoDto dto)
        {
            if (dto == null) return null;
            
            return new TodoItem
            {
                Id = dto.Id,
                Text = dto.Text,
                Description = dto.Description,
                IsCompleted = dto.IsCompleted,
                CompletedDate = dto.CompletedDate,
                DueDate = dto.DueDate,
                ReminderDate = dto.ReminderDate,
                Priority = (Priority)dto.Priority,
                IsFavorite = dto.IsFavorite,
                Order = dto.Order,
                CreatedDate = dto.CreatedDate,
                ModifiedDate = dto.ModifiedDate,
                CategoryId = dto.CategoryId,
                ParentId = dto.ParentId,
                SourceNoteId = dto.SourceNoteId,
                SourceFilePath = dto.SourceFilePath,
                SourceLineNumber = dto.SourceLineNumber,
                SourceCharOffset = dto.SourceCharOffset,
                IsOrphaned = dto.IsOrphaned,
                Tags = new List<string>(), // Tags loaded separately if needed
                LinkedNoteIds = new List<string>()
            };
        }
        
        private List<TodoItem> ConvertDtosToTodoItems(List<TodoDto> dtos)
        {
            if (dtos == null) return new List<TodoItem>();
            return dtos.Select(ConvertDtoToTodoItem).Where(t => t != null).ToList();
        }
        
        #endregion

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

        public Models.SmartListType? SelectedSmartList
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
                
                // ✨ CQRS: Use CreateTodoCommand instead of direct TodoStore call
                var command = new Application.Commands.CreateTodo.CreateTodoCommand
                {
                    Text = QuickAddText.Trim(),
                    CategoryId = _selectedCategoryId
                };
                
                _logger.Info($"📋 Sending CreateTodoCommand via MediatR...");
                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    _logger.Error($"❌ CreateTodoCommand failed: {result.Error}");
                    // TODO: Show error to user (toast/status bar)
                    return;
                }
                
                _logger.Info($"✅ CreateTodoCommand succeeded: {result.Value.Text}");
                
                // Clear text box
                QuickAddText = string.Empty;
                _logger.Info("📋 Cleared QuickAddText");
                
                // NOTE: UI will update automatically via TodoCreatedEvent subscription in TodoStore
                // No need to manually add to Todos collection - event-driven!
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
                // Query from projection
                var todo = await _todoQueryService.GetTodoByIdAsync(todoVm.Id);
                if (todo != null)
                {
                    var command = new Application.Commands.CompleteTodo.CompleteTodoCommand
                    {
                        TodoId = todo.Id,
                        IsCompleted = !todo.IsCompleted
                    };
                    
                    var result = await _mediator.Send(command);
                    
                    if (result.IsFailure)
                    {
                        _logger.Error($"[TodoListViewModel] CompleteTodoCommand failed: {result.Error}");
                        return;
                    }
                    
                    _logger.Info($"✅ Todo updated via command");
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
                // ✨ CQRS: Use DeleteTodoCommand
                var command = new Application.Commands.DeleteTodo.DeleteTodoCommand
                {
                    TodoId = todoVm.Id
                };
                
                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    _logger.Error($"[TodoListViewModel] DeleteTodoCommand failed: {result.Error}");
                    return;
                }
                
                _logger.Info($"✅ Todo deleted via command");
                
                // NOTE: UI updates automatically via TodoDeletedEvent
                // TodoStore removes from collection, no need to manually remove
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
                _logger.Info("📋 LoadTodosAsync started (event-sourced)");
                IsLoading = true;
                
                // Query from projection
                List<TodoItem> todos;
                
                if (_selectedSmartList.HasValue)
                {
                    _logger.Info($"📋 Loading smart list from projection: {_selectedSmartList.Value}");
                    var dtos = await _todoQueryService.GetSmartListTodosAsync(_selectedSmartList.Value);
                    todos = ConvertDtosToTodoItems(dtos);
                }
                else if (_selectedCategoryId != null)
                {
                    _logger.Info($"📋 Loading by category from projection: {_selectedCategoryId}");
                    var dtos = await _todoQueryService.GetTodosByCategoryAsync(_selectedCategoryId.Value);
                    todos = ConvertDtosToTodoItems(dtos);
                }
                else
                {
                    // Load all todos (default to Today smart list)
                    _logger.Info("📋 Loading default (Today) smart list from projection");
                    _selectedSmartList = Models.SmartListType.Today;
                    OnPropertyChanged(nameof(SelectedSmartList));
                    var dtos = await _todoQueryService.GetSmartListTodosAsync(Models.SmartListType.Today);
                    todos = ConvertDtosToTodoItems(dtos);
                }
                
                _logger.Info($"📋 Retrieved {todos.Count} todos from projection");
                
                // Convert to view models
                Todos.Clear();
                foreach (var todo in todos)
                {
                    // Pass null for ITodoStore - event-driven updates handled differently
                    // Pass tagQueryService instead of todoTagRepository
                    var vm = new TodoItemViewModel(todo, null, null, _mediator, _logger);
                    Todos.Add(vm);
                }
                
                _logger.Info($"📋 Created {Todos.Count} view models");
                
                await ApplyFilterAsync();
                _logger.Info("📋 LoadTodosAsync completed successfully (event-sourced)");
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
