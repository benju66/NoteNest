using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Transaction
{
    /// <summary>
    /// Base class for transaction steps providing common functionality
    /// </summary>
    public abstract class TransactionStepBase : ITransactionStep
    {
        protected readonly IAppLogger _logger;
        private TransactionStepState _state = TransactionStepState.NotStarted;
        
        public string StepId { get; }
        public abstract string Description { get; }
        public TransactionStepState State 
        { 
            get => _state;
            protected set
            {
                var oldState = _state;
                _state = value;
                OnStateChanged(oldState, value);
            }
        }
        
        public abstract bool CanRollback { get; }
        
        public event EventHandler<TransactionStepStateChangedEventArgs> StateChanged;

        protected TransactionStepBase(IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            StepId = Guid.NewGuid().ToString("N")[..8]; // Short unique ID
        }

        /// <summary>
        /// Execute the transaction step with state management and error handling
        /// </summary>
        public async Task<TransactionStepResult> ExecuteAsync()
        {
            if (State != TransactionStepState.NotStarted)
                return TransactionStepResult.Failed($"Step {StepId} is in invalid state {State} for execution");

            State = TransactionStepState.Executing;
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                _logger.Debug($"Executing transaction step: {Description} [{StepId}]");
                
                var result = await ExecuteStepAsync();
                
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
                
                if (result.Success)
                {
                    State = TransactionStepState.Completed;
                    _logger.Debug($"Transaction step completed: {Description} [{StepId}] in {stopwatch.ElapsedMilliseconds}ms");
                }
                else
                {
                    State = TransactionStepState.Failed;
                    _logger.Error($"Transaction step failed: {Description} [{StepId}] - {result.ErrorMessage}");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                State = TransactionStepState.Failed;
                _logger.Error(ex, $"Transaction step threw exception: {Description} [{StepId}]");
                
                return TransactionStepResult.Failed($"Exception in step {StepId}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Rollback the transaction step with state management and error handling
        /// </summary>
        public async Task<TransactionStepResult> RollbackAsync()
        {
            if (!CanRollback)
                return TransactionStepResult.Failed($"Step {StepId} cannot be rolled back");

            if (State != TransactionStepState.Completed && State != TransactionStepState.Failed)
                return TransactionStepResult.Failed($"Step {StepId} is in invalid state {State} for rollback");

            State = TransactionStepState.RollingBack;
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                _logger.Debug($"Rolling back transaction step: {Description} [{StepId}]");
                
                var result = await RollbackStepAsync();
                
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
                
                if (result.Success)
                {
                    State = TransactionStepState.RolledBack;
                    _logger.Debug($"Transaction step rolled back: {Description} [{StepId}] in {stopwatch.ElapsedMilliseconds}ms");
                }
                else
                {
                    State = TransactionStepState.RollbackFailed;
                    _logger.Error($"Transaction step rollback failed: {Description} [{StepId}] - {result.ErrorMessage}");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                State = TransactionStepState.RollbackFailed;
                _logger.Error(ex, $"Transaction step rollback threw exception: {Description} [{StepId}]");
                
                return TransactionStepResult.Failed($"Exception during rollback of step {StepId}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Derived classes implement the actual step execution logic
        /// </summary>
        protected abstract Task<TransactionStepResult> ExecuteStepAsync();

        /// <summary>
        /// Derived classes implement the actual step rollback logic
        /// </summary>
        protected abstract Task<TransactionStepResult> RollbackStepAsync();

        /// <summary>
        /// Fire state changed event
        /// </summary>
        protected virtual void OnStateChanged(TransactionStepState oldState, TransactionStepState newState)
        {
            StateChanged?.Invoke(this, new TransactionStepStateChangedEventArgs
            {
                StepId = StepId,
                OldState = oldState,
                NewState = newState,
                Description = Description,
                ChangedAt = DateTime.UtcNow
            });
        }
    }
}
