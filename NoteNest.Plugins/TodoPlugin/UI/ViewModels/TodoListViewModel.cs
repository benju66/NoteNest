using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MediatR;
using NoteNest.Core.Commands;
using NoteNest.Core.Services.Logging;
using NoteNest.Plugins.TodoPlugin.Application.Commands.Todos;
using NoteNest.Plugins.TodoPlugin.Application.Common.Interfaces;
using NoteNest.Plugins.TodoPlugin.Application.Queries.Todos;
using NoteNest.Plugins.TodoPlugin.Domain.Entities;
using NoteNest.Plugins.TodoPlugin.Domain.ValueObjects;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.Plugins.TodoPlugin.UI.ViewModels
{
    /// <summary>
    /// Main view model for the Todo list view.
    /// </summary>
    public class TodoListViewModel : ViewModelBase
    {
        private readonly IMediator _mediator;
        private readonly ITodoStore _todoStore;
        private readonly IAppLogger _logger;
        
        private ObservableCollection<TodoItemViewModel> _todos;
        private TodoItemViewModel? _selectedTodo;
        private CategoryId? _selectedCategoryId;
        private SmartListType? _selectedSmartList;
        private string _filterText = string.Empty;
        private string _quickAddText = string.Empty;
        private bool _isLoading;

        public TodoListViewModel(
            IMediator mediator,
            ITodoStore todoStore,
            IAppLogger logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _todoStore = todoStore ?? throw new ArgumentNullException(nameof(todoStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _todos = new ObservableCollection<TodoItemViewModel>();
            
            InitializeCommands();
            _ = LoadTodosAsync();
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
            set => SetProperty(ref _quickAddText, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public CategoryId? SelectedCategoryId
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

        private void InitializeCommands()
        {
            QuickAddCommand = new AsyncRelayCommand(ExecuteQuickAdd, CanExecuteQuickAdd);
            ToggleCompletionCommand = new AsyncRelayCommand<TodoItemViewModel>(ExecuteToggleCompletion);
            DeleteTodoCommand = new AsyncRelayCommand<TodoItemViewModel>(ExecuteDeleteTodo);
            EditTodoCommand = new RelayCommand<TodoItemViewModel>(ExecuteEditTodo);
            RefreshCommand = new AsyncRelayCommand(LoadTodosAsync);
        }

        private bool CanExecuteQuickAdd()
        {
            return !string.IsNullOrWhiteSpace(QuickAddText);
        }

        private async Task ExecuteQuickAdd()
        {
            if (string.IsNullOrWhiteSpace(QuickAddText))
                return;

            try
            {
                IsLoading = true;
                
                var command = new CreateTodoCommand
                {
                    Text = QuickAddText.Trim(),
                    CategoryId = _selectedCategoryId
                };
                
                var result = await _mediator.Send(command);
                if (result.IsSuccess)
                {
                    QuickAddText = string.Empty;
                    _logger.Info($"Created todo: {command.Text}");
                    
                    // The store will handle adding to the collection via events
                    await LoadTodosAsync(); // Refresh to ensure proper ordering
                }
                else
                {
                    _logger.Warning($"Failed to create todo: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating todo");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ExecuteToggleCompletion(TodoItemViewModel? todoVm)
        {
            if (todoVm == null) return;

            try
            {
                var command = new ToggleTodoCompletionCommand
                {
                    TodoId = todoVm.Id
                };
                
                var result = await _mediator.Send(command);
                if (!result.IsSuccess)
                {
                    _logger.Warning($"Failed to toggle todo completion: {result.Error}");
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
                var command = new DeleteTodoCommand
                {
                    TodoId = todoVm.Id
                };
                
                var result = await _mediator.Send(command);
                if (!result.IsSuccess)
                {
                    _logger.Warning($"Failed to delete todo: {result.Error}");
                }
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
                IsLoading = true;
                
                ObservableCollection<TodoItem> todos;
                
                if (_selectedSmartList.HasValue)
                {
                    // Load smart list
                    todos = _todoStore.GetSmartList(_selectedSmartList.Value);
                }
                else if (_selectedCategoryId != null)
                {
                    // Load by category
                    todos = _todoStore.GetByCategory(_selectedCategoryId);
                }
                else
                {
                    // Load all todos (default to Today smart list)
                    _selectedSmartList = SmartListType.Today;
                    OnPropertyChanged(nameof(SelectedSmartList));
                    todos = _todoStore.GetSmartList(SmartListType.Today);
                }
                
                // Convert to view models
                Todos.Clear();
                foreach (var todo in todos)
                {
                    var vm = new TodoItemViewModel(todo, _mediator, _logger);
                    Todos.Add(vm);
                }
                
                await ApplyFilterAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading todos");
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
