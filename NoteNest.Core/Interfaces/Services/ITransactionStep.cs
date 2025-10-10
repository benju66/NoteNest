using System;
using System.Threading.Tasks;

namespace NoteNest.Core.Interfaces.Services
{
    /// <summary>
    /// Represents a single step in a storage location transaction
    /// Each step can be executed and rolled back independently
    /// </summary>
    public interface ITransactionStep
    {
        /// <summary>
        /// Unique identifier for this transaction step
        /// </summary>
        string StepId { get; }
        
        /// <summary>
        /// Human-readable description of what this step does
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// Current state of this step
        /// </summary>
        TransactionStepState State { get; }
        
        /// <summary>
        /// Execute the transaction step
        /// </summary>
        Task<TransactionStepResult> ExecuteAsync();
        
        /// <summary>
        /// Rollback the transaction step (undo its effects)
        /// </summary>
        Task<TransactionStepResult> RollbackAsync();
        
        /// <summary>
        /// Check if this step can be safely rolled back
        /// </summary>
        bool CanRollback { get; }
        
        /// <summary>
        /// Event fired when step state changes
        /// </summary>
        event EventHandler<TransactionStepStateChangedEventArgs> StateChanged;
    }

    /// <summary>
    /// State of a transaction step
    /// </summary>
    public enum TransactionStepState
    {
        NotStarted,
        Executing,
        Completed,
        Failed,
        RollingBack,
        RolledBack,
        RollbackFailed
    }

    /// <summary>
    /// Result of executing or rolling back a transaction step
    /// </summary>
    public class TransactionStepResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
        public TimeSpan Duration { get; set; }
        public object? ResultData { get; set; }
        
        public static TransactionStepResult Succeeded(object? resultData = null, TimeSpan? duration = null)
        {
            return new TransactionStepResult 
            { 
                Success = true, 
                ResultData = resultData,
                Duration = duration ?? TimeSpan.Zero
            };
        }
        
        public static TransactionStepResult Failed(string errorMessage, Exception? exception = null)
        {
            return new TransactionStepResult 
            { 
                Success = false, 
                ErrorMessage = errorMessage,
                Exception = exception
            };
        }
    }

    /// <summary>
    /// Event args for transaction step state changes
    /// </summary>
    public class TransactionStepStateChangedEventArgs : EventArgs
    {
        public string StepId { get; set; } = string.Empty;
        public TransactionStepState OldState { get; set; }
        public TransactionStepState NewState { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime ChangedAt { get; set; }
    }
}
