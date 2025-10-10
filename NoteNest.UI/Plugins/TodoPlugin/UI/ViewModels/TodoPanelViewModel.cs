using System;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Models;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.UI.ViewModels
{
    /// <summary>
    /// Composite ViewModel for the Todo panel that combines:
    /// - TodoListViewModel (todo list and operations)
    /// - CategoryTreeViewModel (category navigation and smart lists)
    /// This follows the composition pattern used in MainShellViewModel.
    /// </summary>
    public class TodoPanelViewModel : ViewModelBase
    {
        private readonly IAppLogger _logger;

        public TodoPanelViewModel(
            TodoListViewModel todoList,
            CategoryTreeViewModel categoryTree,
            IAppLogger logger)
        {
            TodoList = todoList ?? throw new ArgumentNullException(nameof(todoList));
            CategoryTree = categoryTree ?? throw new ArgumentNullException(nameof(categoryTree));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Wire category selection to todo filtering
            CategoryTree.CategorySelected += OnCategorySelected;
            CategoryTree.SmartListSelected += OnSmartListSelected;
            
            _logger.Info("[TodoPanel] Composite ViewModel initialized with TodoList + CategoryTree");
        }

        /// <summary>
        /// Todo list view model - manages todos and operations
        /// </summary>
        public TodoListViewModel TodoList { get; }

        /// <summary>
        /// Category tree view model - manages navigation and filtering
        /// </summary>
        public CategoryTreeViewModel CategoryTree { get; }

        private void OnCategorySelected(object sender, Guid categoryId)
        {
            _logger.Debug($"[TodoPanel] Category selected: {categoryId}");
            
            // Update TodoList to filter by selected category
            TodoList.SelectedCategoryId = categoryId;
            TodoList.SelectedSmartList = null;
        }

        private void OnSmartListSelected(object sender, SmartListType listType)
        {
            _logger.Debug($"[TodoPanel] Smart list selected: {listType}");
            
            // Update TodoList to show smart list
            TodoList.SelectedSmartList = listType;
            TodoList.SelectedCategoryId = null;
        }
    }
}

