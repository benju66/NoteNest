using System;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Implementation
{
    public class ServiceErrorHandler : IServiceErrorHandler
    {
        private readonly IAppLogger _logger;
        private readonly IStateManager? _stateManager;
        
        public event EventHandler<NoteNest.Core.Interfaces.Services.ErrorEventArgs>? ErrorOccurred;
        
        public ServiceErrorHandler(IAppLogger logger, IStateManager? stateManager = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _stateManager = stateManager; // Optional dependency
            
            _logger.Debug("ServiceErrorHandler initialized");
        }
        
        public async Task<bool> SafeExecuteAsync(Func<Task> action, string operationName)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            
            try
            {
                _logger.Debug($"Starting operation: {operationName}");
                
                if (_stateManager != null)
                {
                    _stateManager.BeginOperation($"Processing {operationName}...");
                }
                
                await action();
                
                _logger.Debug($"Completed operation: {operationName}");
                
                if (_stateManager != null)
                {
                    _stateManager.EndOperation($"{operationName} completed");
                }
                
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.Info($"Operation cancelled: {operationName}");
                
                if (_stateManager != null)
                {
                    _stateManager.EndOperation($"{operationName} cancelled");
                }
                
                return false;
            }
            catch (Exception ex)
            {
                LogError(ex, operationName);
                
                var errorArgs = new NoteNest.Core.Interfaces.Services.ErrorEventArgs
                {
                    Exception = ex,
                    Context = operationName,
                    IsHandled = false
                };
                
                ErrorOccurred?.Invoke(this, errorArgs);
                
                if (_stateManager != null)
                {
                    _stateManager.EndOperation($"Error in {operationName}");
                    _stateManager.StatusMessage = $"Error: {ex.Message}";
                }
                
                return false;
            }
        }
        
        public async Task<T?> SafeExecuteAsync<T>(Func<Task<T>> action, string operationName)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            
            try
            {
                _logger.Debug($"Starting operation: {operationName}");
                
                if (_stateManager != null)
                {
                    _stateManager.BeginOperation($"Processing {operationName}...");
                }
                
                var result = await action();
                
                _logger.Debug($"Completed operation: {operationName}");
                
                if (_stateManager != null)
                {
                    _stateManager.EndOperation($"{operationName} completed");
                }
                
                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.Info($"Operation cancelled: {operationName}");
                
                if (_stateManager != null)
                {
                    _stateManager.EndOperation($"{operationName} cancelled");
                }
                
                return default(T);
            }
            catch (Exception ex)
            {
                LogError(ex, operationName);
                
                var errorArgs = new NoteNest.Core.Interfaces.Services.ErrorEventArgs
                {
                    Exception = ex,
                    Context = operationName,
                    IsHandled = false
                };
                
                ErrorOccurred?.Invoke(this, errorArgs);
                
                if (_stateManager != null)
                {
                    _stateManager.EndOperation($"Error in {operationName}");
                    _stateManager.StatusMessage = $"Error: {ex.Message}";
                }
                
                return default(T);
            }
        }
        
        public void LogError(Exception ex, string context)
        {
            if (ex == null) return;
            
            _logger.Error(ex, $"Error in {context}: {ex.Message}");
            
            // Log inner exceptions
            var innerEx = ex.InnerException;
            int depth = 1;
            while (innerEx != null && depth < 5)
            {
                _logger.Error(innerEx, $"Inner exception {depth}: {innerEx.Message}");
                innerEx = innerEx.InnerException;
                depth++;
            }
        }
    }
}