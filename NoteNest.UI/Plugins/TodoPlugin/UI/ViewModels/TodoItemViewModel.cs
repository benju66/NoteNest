using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MediatR;
using NoteNest.Core.Commands;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Models;
using NoteNest.UI.Plugins.TodoPlugin.Services;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.UI.ViewModels
{
    /// <summary>
    /// View model for individual todo items.
    /// </summary>
    public class TodoItemViewModel : ViewModelBase
    {
        private readonly TodoItem _todoItem;
        private readonly ITodoStore _todoStore;
        private readonly IMediator _mediator;
        private readonly IAppLogger _logger;
        
        private bool _isEditing;
        private string _editingText;
        private bool _isVisible = true;

        public TodoItemViewModel(TodoItem todoItem, ITodoStore todoStore, IMediator mediator, IAppLogger logger)
        {
            _todoItem = todoItem ?? throw new ArgumentNullException(nameof(todoItem));
            _todoStore = todoStore ?? throw new ArgumentNullException(nameof(todoStore));
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _editingText = _todoItem.Text;
            
            InitializeCommands();
        }

        #region Properties

        public Guid Id => _todoItem.Id;
        
        /// <summary>
        /// Category ID of the todo item (null for uncategorized).
        /// Exposed for selection context tracking and parent category identification.
        /// </summary>
        public Guid? CategoryId => _todoItem.CategoryId;
        
        public string Text
        {
            get => _todoItem.Text;
            set
            {
                if (_todoItem.Text != value)
                {
                    _ = UpdateTextAsync(value);
                }
            }
        }

        public string? Description => _todoItem.Description;
        
        public bool IsCompleted
        {
            get => _todoItem.IsCompleted;
            set
            {
                if (_todoItem.IsCompleted != value)
                {
                    _ = ToggleCompletionAsync();
                }
            }
        }

        public DateTime? DueDate => _todoItem.DueDate;
        
        public DateTime? CompletedDate => _todoItem.CompletedDate;
        
        public Priority Priority => _todoItem.Priority;
        
        public bool IsFavorite => _todoItem.IsFavorite;
        
        public IReadOnlyList<string> Tags => _todoItem.Tags;
        
        public bool IsOverdue => _todoItem.IsOverdue();
        
        public bool IsDueToday => _todoItem.IsDueToday();
        
        public bool IsDueTomorrow => _todoItem.IsDueTomorrow();
        
        public System.Windows.Media.Brush PriorityBrush
        {
            get
            {
                var app = System.Windows.Application.Current;
                if (app == null) return System.Windows.Media.Brushes.Gray;
                
                return _todoItem.Priority switch
                {
                    Priority.Low => (System.Windows.Media.Brush)app.Resources["AppTextSecondaryBrush"],
                    Priority.Normal => (System.Windows.Media.Brush)app.Resources["AppTextPrimaryBrush"],
                    Priority.High => (System.Windows.Media.Brush)app.Resources["AppWarningBrush"],
                    Priority.Urgent => (System.Windows.Media.Brush)app.Resources["AppErrorBrush"],
                    _ => (System.Windows.Media.Brush)app.Resources["AppTextPrimaryBrush"]
                };
            }
        }
        
        public string PriorityTooltip => _todoItem.Priority switch
        {
            Priority.Low => "Low Priority",
            Priority.Normal => "Normal Priority",
            Priority.High => "High Priority",
            Priority.Urgent => "Urgent Priority",
            _ => "Normal Priority"
        };

        public bool IsEditing
        {
            get => _isEditing;
            set => SetProperty(ref _isEditing, value);
        }

        public string EditingText
        {
            get => _editingText;
            set => SetProperty(ref _editingText, value);
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        // Computed properties for UI binding
        public string DueDateDisplay
        {
            get
            {
                if (!DueDate.HasValue)
                    return string.Empty;
                    
                var dueDate = DueDate.Value;
                var today = DateTime.Today;
                
                if (dueDate.Date == today)
                    return "Today";
                else if (dueDate.Date == today.AddDays(1))
                    return "Tomorrow";
                else if (dueDate.Date == today.AddDays(-1))
                    return "Yesterday";
                else if (dueDate.Date < today)
                    return $"Overdue ({dueDate:MMM d})";
                else if (dueDate.Date < today.AddDays(7))
                    return dueDate.ToString("dddd");
                else
                    return dueDate.ToString("MMM d");
            }
        }

        public string TagsDisplay => Tags.Any() ? string.Join(", ", Tags) : string.Empty;
        
        // Source tracking for RTF integration
        public bool IsNoteLinked => _todoItem.SourceNoteId.HasValue;
        public string SourceIndicator => IsNoteLinked ? "📄" : string.Empty;
        public string SourceTooltip => IsNoteLinked 
            ? $"Linked to note:\n{System.IO.Path.GetFileName(_todoItem.SourceFilePath ?? "Unknown")}\nLine {_todoItem.SourceLineNumber ?? 0}"
            : string.Empty;
        public bool ShowOrphanedIndicator => _todoItem.IsOrphaned;

        #endregion

        #region Events

        /// <summary>
        /// Events for parent coordination - enables event bubbling pattern.
        /// Matches main app NoteItemViewModel pattern for architectural consistency.
        /// </summary>
        public event Action<TodoItemViewModel>? OpenRequested;
        public event Action<TodoItemViewModel>? SelectionRequested;

        private void OnOpenRequested()
        {
            OpenRequested?.Invoke(this);
        }

        private void OnSelectionRequested()
        {
            SelectionRequested?.Invoke(this);
        }

        #endregion

        #region Commands

        public ICommand ToggleCompletionCommand { get; private set; }
        public ICommand ToggleFavoriteCommand { get; private set; }
        public ICommand StartEditCommand { get; private set; }
        public ICommand SaveEditCommand { get; private set; }
        public ICommand CancelEditCommand { get; private set; }
        public ICommand SetPriorityCommand { get; private set; }
        public ICommand CyclePriorityCommand { get; private set; }
        public ICommand SetDueDateCommand { get; private set; }
        public ICommand DeleteCommand { get; private set; }
        public ICommand OpenCommand { get; private set; }
        public ICommand SelectCommand { get; private set; }

        private void InitializeCommands()
        {
            ToggleCompletionCommand = new AsyncRelayCommand(ToggleCompletionAsync);
            ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync);
            StartEditCommand = new RelayCommand(StartEdit);
            SaveEditCommand = new AsyncRelayCommand(SaveEdit);
            CancelEditCommand = new RelayCommand(CancelEdit);
            SetPriorityCommand = new AsyncRelayCommand<int>(SetPriorityAsync);
            CyclePriorityCommand = new AsyncRelayCommand(CyclePriorityAsync);
            SetDueDateCommand = new RelayCommand(SetDueDate);
            DeleteCommand = new AsyncRelayCommand(DeleteAsync);
            OpenCommand = new RelayCommand(() => OnOpenRequested());
            SelectCommand = new RelayCommand(() => OnSelectionRequested());
        }

        private async Task ToggleCompletionAsync()
        {
            try
            {
                // ✨ CQRS: Use CompleteTodoCommand
                var command = new Application.Commands.CompleteTodo.CompleteTodoCommand
                {
                    TodoId = _todoItem.Id,
                    IsCompleted = !_todoItem.IsCompleted
                };
                
                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    _logger.Error($"[TodoItemViewModel] CompleteTodoCommand failed: {result.Error}");
                    return;
                }
                
                // NOTE: UI updates automatically via TodoCompletedEvent/TodoUncompletedEvent
                // Event handler in TodoStore reloads from DB and updates collection
                _logger.Debug($"[TodoItemViewModel] ✅ Todo completion toggled via command");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error toggling todo completion");
            }
        }

        private async Task ToggleFavoriteAsync()
        {
            try
            {
                // ✨ CQRS: Use ToggleFavoriteCommand
                var command = new Application.Commands.ToggleFavorite.ToggleFavoriteCommand
                {
                    TodoId = _todoItem.Id,
                    IsFavorite = !_todoItem.IsFavorite
                };
                
                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    _logger.Error($"[TodoItemViewModel] ToggleFavoriteCommand failed: {result.Error}");
                    return;
                }
                
                // NOTE: UI updates automatically via TodoFavoritedEvent/TodoUnfavoritedEvent
                _logger.Debug($"[TodoItemViewModel] ✅ Todo favorite toggled via command");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error toggling todo favorite");
            }
        }

        private void StartEdit()
        {
            EditingText = Text;
            IsEditing = true;
        }

        private async Task SaveEdit()
        {
            if (string.IsNullOrWhiteSpace(EditingText))
            {
                CancelEdit();
                return;
            }

            await UpdateTextAsync(EditingText.Trim());
            IsEditing = false;
        }

        private void CancelEdit()
        {
            EditingText = Text;
            IsEditing = false;
        }

        private async Task UpdateTextAsync(string newText)
        {
            if (string.IsNullOrWhiteSpace(newText) || newText == Text)
                return;
            
            try
            {
                // ✨ CQRS: Use UpdateTodoTextCommand
                var command = new Application.Commands.UpdateTodoText.UpdateTodoTextCommand
                {
                    TodoId = _todoItem.Id,
                    NewText = newText.Trim()
                };
                
                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    _logger.Error($"[TodoItemViewModel] UpdateTodoTextCommand failed: {result.Error}");
                    return;
                }
                
                // NOTE: UI updates automatically via TodoTextUpdatedEvent
                _logger.Debug($"[TodoItemViewModel] ✅ Todo text updated via command");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating todo text");
            }
        }
        
        private async Task SetPriorityAsync(int priority)
        {
            try
            {
                // ✨ CQRS: Use SetPriorityCommand
                var command = new Application.Commands.SetPriority.SetPriorityCommand
                {
                    TodoId = _todoItem.Id,
                    Priority = (Priority)priority
                };
                
                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    _logger.Error($"[TodoItemViewModel] SetPriorityCommand failed: {result.Error}");
                    return;
                }
                
                // NOTE: UI updates automatically via TodoPriorityChangedEvent
                _logger.Debug($"[TodoItemViewModel] ✅ Todo priority set via command");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting priority");
            }
        }
        
        private async Task CyclePriorityAsync()
        {
            var nextPriority = (int)_todoItem.Priority + 1;
            if (nextPriority > 3) nextPriority = 0;  // Wrap around
            await SetPriorityAsync(nextPriority);
        }
        
        private void SetDueDate()
        {
            try
            {
                var dialog = new NoteNest.UI.Plugins.TodoPlugin.Dialogs.DatePickerDialog(_todoItem.DueDate)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        // ✨ CQRS: Use SetDueDateCommand
                        var command = new Application.Commands.SetDueDate.SetDueDateCommand
                        {
                            TodoId = _todoItem.Id,
                            DueDate = dialog.SelectedDate
                        };
                        
                        // Synchronous call (command pattern is async, but this is called from sync context)
                        var result = _mediator.Send(command).GetAwaiter().GetResult();
                        
                        if (result.IsFailure)
                        {
                            _logger.Error($"[TodoItemViewModel] SetDueDateCommand failed: {result.Error}");
                            return;
                        }
                        
                        // NOTE: UI updates automatically via TodoDueDateChangedEvent
                        _logger.Debug($"[TodoItemViewModel] ✅ Todo due date set via command");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error setting due date");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error showing date picker dialog");
            }
        }
        
        private async Task DeleteAsync()
        {
            try
            {
                // ✨ CQRS: Use DeleteTodoCommand
                var command = new Application.Commands.DeleteTodo.DeleteTodoCommand
                {
                    TodoId = _todoItem.Id
                };
                
                var result = await _mediator.Send(command);
                
                if (result.IsFailure)
                {
                    _logger.Error($"[TodoItemViewModel] DeleteTodoCommand failed: {result.Error}");
                    return;
                }
                
                // NOTE: UI updates automatically via TodoDeletedEvent
                // TodoStore removes from collection
                _logger.Info($"[TodoItemViewModel] ✅ Todo deleted via command: {_todoItem.Text}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting todo");
            }
        }

        #endregion

        /// <summary>
        /// Refreshes all properties from the underlying todo item.
        /// </summary>
        public void RefreshFromModel(TodoItem updatedTodo)
        {
            if (updatedTodo.Id != Id)
                throw new ArgumentException("Cannot refresh from a different todo item");
                
            // Notify all property changes
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(DueDate));
            OnPropertyChanged(nameof(CompletedDate));
            OnPropertyChanged(nameof(Priority));
            OnPropertyChanged(nameof(IsFavorite));
            OnPropertyChanged(nameof(Tags));
            OnPropertyChanged(nameof(IsOverdue));
            OnPropertyChanged(nameof(IsDueToday));
            OnPropertyChanged(nameof(IsDueTomorrow));
            OnPropertyChanged(nameof(DueDateDisplay));
            OnPropertyChanged(nameof(TagsDisplay));
        }
    }
}
