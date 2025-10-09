using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using NoteNest.Core.Commands;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Models;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.UI.Plugins.TodoPlugin.UI.ViewModels
{
    /// <summary>
    /// MINIMAL DEBUG VERSION - No database, just in-memory
    /// Use this to verify UI binding works
    /// </summary>
    public class TodoListViewModel_Minimal : ViewModelBase
    {
        private readonly IAppLogger _logger;
        private ObservableCollection<string> _simpleTodos;
        private string _quickAddText = string.Empty;

        public TodoListViewModel_Minimal(IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _simpleTodos = new ObservableCollection<string>();
            _logger.Info("🧪 MINIMAL TodoListViewModel created");
            
            QuickAddCommand = new RelayCommand(ExecuteQuickAdd, CanExecuteQuickAdd);
            
            // Add test data
            _simpleTodos.Add("Test todo 1");
            _simpleTodos.Add("Test todo 2");
            _logger.Info($"🧪 Added 2 test todos, Count={_simpleTodos.Count}");
        }

        public ObservableCollection<string> SimpleTodos
        {
            get => _simpleTodos;
            set => SetProperty(ref _simpleTodos, value);
        }

        public string QuickAddText
        {
            get => _quickAddText;
            set => SetProperty(ref _quickAddText, value);
        }

        public ICommand QuickAddCommand { get; private set; }

        private bool CanExecuteQuickAdd()
        {
            return !string.IsNullOrWhiteSpace(QuickAddText);
        }

        private void ExecuteQuickAdd()
        {
            if (string.IsNullOrWhiteSpace(QuickAddText))
                return;

            _logger.Info($"🧪 EXECUTING QuickAdd: '{QuickAddText}'");
            
            try
            {
                _simpleTodos.Add(QuickAddText.Trim());
                _logger.Info($"🧪 Added to collection, Count={_simpleTodos.Count}");
                
                QuickAddText = string.Empty;
                _logger.Info("🧪 Cleared textbox");
                
                OnPropertyChanged(nameof(SimpleTodos));
                _logger.Info("🧪 Raised PropertyChanged");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "🧪 ERROR in QuickAdd!");
            }
        }
    }
}

