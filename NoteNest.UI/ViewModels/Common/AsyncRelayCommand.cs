using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NoteNest.UI.ViewModels.Common
{
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private readonly Action<Exception>? _onError;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null, Action<Exception>? onError = null)
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

        public bool CanExecute(object parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke() ?? true);
        }

        public async void Execute(object parameter)
        {
            if (!CanExecute(parameter))
                return;

            _isExecuting = true;
            RaiseCanExecuteChanged();

            try
            {
                await _execute();
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
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public class AsyncRelayCommand<T> : ICommand
    {
        private readonly Func<T, Task> _execute;
        private readonly Func<T, bool> _canExecute;
        private readonly Action<Exception>? _onError;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<T, Task> execute, Func<T, bool> canExecute = null, Action<Exception>? onError = null)
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

        public bool CanExecute(object parameter)
        {
            try
            {
                if (_isExecuting) return false;
                
                if (_canExecute == null) return true;
                
                // Safe cast with null handling
                if (parameter is T typedParam)
                {
                    return _canExecute.Invoke(typedParam);
                }
                else if (parameter == null && default(T) == null)
                {
                    return _canExecute.Invoke((T)parameter);
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AsyncRelayCommand] CanExecute exception: {ex.Message}");
                return false;
            }
        }

        public async void Execute(object parameter)
        {
            if (!CanExecute(parameter))
                return;

            _isExecuting = true;
            RaiseCanExecuteChanged();

            try
            {
                T typedParam = parameter is T t ? t : (T)parameter;
                await _execute(typedParam);
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
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
