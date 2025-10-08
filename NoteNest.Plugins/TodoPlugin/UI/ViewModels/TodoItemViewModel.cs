using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MediatR;
using NoteNest.Core.Commands;
using NoteNest.Core.Services.Logging;
using NoteNest.Plugins.TodoPlugin.Application.Commands.Todos;
using NoteNest.Plugins.TodoPlugin.Domain.Entities;
using NoteNest.Plugins.TodoPlugin.Domain.ValueObjects;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.Plugins.TodoPlugin.UI.ViewModels
{
    /// <summary>
    /// View model for individual todo items.
    /// </summary>
    public class TodoItemViewModel : ViewModelBase
    {
        private readonly TodoItem _todoItem;
        private readonly IMediator _mediator;
        private readonly IAppLogger _logger;
        
        private bool _isEditing;
        private string _editingText;
        private bool _isVisible = true;

        public TodoItemViewModel(TodoItem todoItem, IMediator mediator, IAppLogger logger)
        {
            _todoItem = todoItem ?? throw new ArgumentNullException(nameof(todoItem));
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _editingText = _todoItem.Text;
            
            InitializeCommands();
        }

        #region Properties

        public TodoItemId Id => _todoItem.Id;
        
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

        #endregion

        #region Commands

        public ICommand ToggleCompletionCommand { get; private set; }
        public ICommand ToggleFavoriteCommand { get; private set; }
        public ICommand StartEditCommand { get; private set; }
        public ICommand SaveEditCommand { get; private set; }
        public ICommand CancelEditCommand { get; private set; }

        private void InitializeCommands()
        {
            ToggleCompletionCommand = new AsyncRelayCommand(ToggleCompletionAsync);
            ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync);
            StartEditCommand = new RelayCommand(StartEdit);
            SaveEditCommand = new AsyncRelayCommand(SaveEdit);
            CancelEditCommand = new RelayCommand(CancelEdit);
        }

        private async Task ToggleCompletionAsync()
        {
            try
            {
                var command = new ToggleTodoCompletionCommand { TodoId = Id };
                var result = await _mediator.Send(command);
                
                if (result.IsSuccess)
                {
                    OnPropertyChanged(nameof(IsCompleted));
                    OnPropertyChanged(nameof(CompletedDate));
                }
                else
                {
                    _logger.Warning($"Failed to toggle completion: {result.Error}");
                }
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
                var command = new ToggleTodoFavoriteCommand { TodoId = Id };
                var result = await _mediator.Send(command);
                
                if (result.IsSuccess)
                {
                    OnPropertyChanged(nameof(IsFavorite));
                }
                else
                {
                    _logger.Warning($"Failed to toggle favorite: {result.Error}");
                }
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
                var command = new UpdateTodoCommand
                {
                    TodoId = Id,
                    Text = newText.Trim()
                };
                
                var result = await _mediator.Send(command);
                if (result.IsSuccess)
                {
                    OnPropertyChanged(nameof(Text));
                }
                else
                {
                    _logger.Warning($"Failed to update todo text: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating todo text");
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
