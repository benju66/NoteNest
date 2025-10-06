using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NoteNest.UI.Commands
{
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<object, Task> _execute;
        private readonly Predicate<object> _canExecute;
        private readonly Action<Exception>? _onError;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<object, Task> execute, Predicate<object> canExecute = null, Action<Exception>? onError = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
            _onError = onError;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool IsExecuting
        {
            get => _isExecuting;
            private set
            {
                _isExecuting = value;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool CanExecute(object parameter)
        {
            return !IsExecuting && (_canExecute?.Invoke(parameter) ?? true);
        }

        public async void Execute(object parameter)
        {
            if (!CanExecute(parameter)) return;

            try
            {
                IsExecuting = true;
                await _execute(parameter);
            }
            catch (Exception ex)
            {
                // Log to debug output for diagnostics
                System.Diagnostics.Debug.WriteLine($"[AsyncRelayCommand] Execute exception: {ex.Message}\n{ex.StackTrace}");
                
                // Invoke optional error handler (allows ViewModel to show UI feedback)
                _onError?.Invoke(ex);
                
                // DO NOT RETHROW - prevents application crash from async void
                // ViewModels should handle errors in their command methods
            }
            finally
            {
                IsExecuting = false;
            }
        }
    }

    public class AsyncRelayCommand<T> : ICommand
    {
        private readonly Func<T, Task> _execute;
        private readonly Predicate<T> _canExecute;
        private readonly Action<Exception>? _onError;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<T, Task> execute, Predicate<T> canExecute = null, Action<Exception>? onError = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
            _onError = onError;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool IsExecuting
        {
            get => _isExecuting;
            private set
            {
                _isExecuting = value;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool CanExecute(object parameter)
        {
            return !IsExecuting && (_canExecute?.Invoke((T)parameter) ?? true);
        }

        public async void Execute(object parameter)
        {
            if (!CanExecute(parameter)) return;

            try
            {
                IsExecuting = true;
                await _execute((T)parameter);
            }
            catch (Exception ex)
            {
                // Log to debug output for diagnostics
                System.Diagnostics.Debug.WriteLine($"[AsyncRelayCommand] Execute exception: {ex.Message}\n{ex.StackTrace}");
                
                // Invoke optional error handler (allows ViewModel to show UI feedback)
                _onError?.Invoke(ex);
                
                // DO NOT RETHROW - prevents application crash from async void
                // ViewModels should handle errors in their command methods
            }
            finally
            {
                IsExecuting = false;
            }
        }
    }
}