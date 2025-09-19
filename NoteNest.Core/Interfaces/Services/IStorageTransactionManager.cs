using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Core.Models;

namespace NoteNest.Core.Interfaces.Services
{
    /// <summary>
    /// Interface for managing storage location change transactions
    /// </summary>
    public interface IStorageTransactionManager
    {
        /// <summary>
        /// Execute a complete storage location change transaction
        /// </summary>
        Task<StorageTransactionResult> ChangeStorageLocationAsync(
            string newPath, 
            StorageMode mode, 
            bool keepOriginalData = true,
            IProgress<StorageTransactionProgress> progress = null);

        /// <summary>
        /// Event fired when a transaction starts
        /// </summary>
        event EventHandler<StorageTransactionEventArgs> TransactionStarted;

        /// <summary>
        /// Event fired when a transaction completes (success or failure)
        /// </summary>
        event EventHandler<StorageTransactionEventArgs> TransactionCompleted;

        /// <summary>
        /// Event fired for transaction progress updates
        /// </summary>
        event EventHandler<StorageTransactionProgressEventArgs> ProgressChanged;
    }

    /// <summary>
    /// Result of a storage location change transaction
    /// </summary>
    public class StorageTransactionResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public Exception Exception { get; set; }
        public string TransactionId { get; set; } = string.Empty;
        public string NewPath { get; set; } = string.Empty;
        public string OldPath { get; set; } = string.Empty;
        public string FailedStep { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public bool DataMigrated { get; set; }
        public Dictionary<string, string> StepResults { get; set; } = new();
    }

    /// <summary>
    /// Progress information for storage location transactions
    /// </summary>
    public class StorageTransactionProgress
    {
        public int CurrentStep { get; set; }
        public int TotalSteps { get; set; }
        public string CurrentOperation { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public int PercentComplete { get; set; }
    }

    /// <summary>
    /// Event args for storage transaction events
    /// </summary>
    public class StorageTransactionEventArgs : EventArgs
    {
        public string TransactionId { get; set; } = string.Empty;
        public string NewPath { get; set; } = string.Empty;
        public StorageMode StorageMode { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Event args for storage transaction progress events
    /// </summary>
    public class StorageTransactionProgressEventArgs : EventArgs
    {
        public StorageTransactionProgress Progress { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
