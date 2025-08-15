using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Implementation
{
    public class StateManager : IStateManager
    {
        private readonly IAppLogger _logger;
        private bool _isLoading;
        private string _statusMessage;
        
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    var oldValue = _isLoading;
                    _isLoading = value;
                    OnPropertyChanged();
                    OnStateChanged(nameof(IsLoading), oldValue, value);
                    
                    _logger?.Debug($"IsLoading changed from {oldValue} to {value}");
                }
            }
        }
        
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    var oldValue = _statusMessage;
                    _statusMessage = value;
                    OnPropertyChanged();
                    OnStateChanged(nameof(StatusMessage), oldValue, value);
                    
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        _logger?.Info($"Status: {value}");
                    }
                }
            }
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<StateChangedEventArgs>? StateChanged;
        
        public StateManager(IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _statusMessage = "Ready";
            _isLoading = false;
            
            _logger.Debug("StateManager initialized");
        }
        
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        protected virtual void OnStateChanged(string propertyName, object oldValue, object newValue)
        {
            StateChanged?.Invoke(this, new StateChangedEventArgs
            {
                PropertyName = propertyName,
                OldValue = oldValue,
                NewValue = newValue
            });
        }
        
        public void BeginOperation(string message = "Loading...")
        {
            IsLoading = true;
            StatusMessage = message;
        }
        
        public void EndOperation(string message = "Ready")
        {
            IsLoading = false;
            StatusMessage = message;
        }
        
        public void ReportProgress(string message)
        {
            StatusMessage = message;
        }
        
        public void ClearStatus()
        {
            StatusMessage = string.Empty;
        }
    }
}