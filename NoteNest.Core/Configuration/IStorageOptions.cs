using System;

namespace NoteNest.Core.Configuration
{
    /// <summary>
    /// Clean, focused interface for storage-related configuration
    /// Follows Single Responsibility Principle
    /// </summary>
    public interface IStorageOptions
    {
        /// <summary>
        /// Root path for notes storage
        /// </summary>
        string NotesPath { get; }

        /// <summary>
        /// Path for metadata storage (.notenest folder)
        /// </summary>
        string MetadataPath { get; }

        /// <summary>
        /// Temporary files path
        /// </summary>
        string TempPath { get; }

        /// <summary>
        /// Write-ahead log path for save operations
        /// </summary>
        string WalPath { get; }

        /// <summary>
        /// Storage mode (Local, OneDrive, Custom)
        /// </summary>
        Models.StorageMode StorageMode { get; }

        /// <summary>
        /// Custom storage path when StorageMode is Custom
        /// </summary>
        string? CustomPath { get; }
    }
}
