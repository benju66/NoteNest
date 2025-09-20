using System;
using System.IO;
using NoteNest.Core.Models;

namespace NoteNest.Core.Configuration
{
    /// <summary>
    /// Clean, focused storage configuration implementation
    /// Immutable and validates paths on construction
    /// </summary>
    public class StorageOptions : IStorageOptions
    {
        public string NotesPath { get; init; } = string.Empty;
        public string MetadataPath { get; init; } = string.Empty;
        public string TempPath { get; init; } = string.Empty;
        public string WalPath { get; init; } = string.Empty;
        public StorageMode StorageMode { get; init; } = StorageMode.Local;
        public string? CustomPath { get; init; }

        /// <summary>
        /// Create storage options from base notes path
        /// Automatically calculates all derived paths
        /// </summary>
        public static StorageOptions FromNotesPath(string notesPath, StorageMode mode = StorageMode.Local, string? customPath = null)
        {
            if (string.IsNullOrWhiteSpace(notesPath))
            {
                throw new ArgumentException("Notes path cannot be null or empty", nameof(notesPath));
            }

            var metadataPath = Path.Combine(notesPath, ".notenest");
            var tempPath = Path.Combine(notesPath, ".temp");
            var walPath = Path.Combine(notesPath, ".wal");

            return new StorageOptions
            {
                NotesPath = Path.GetFullPath(notesPath),
                MetadataPath = Path.GetFullPath(metadataPath),
                TempPath = Path.GetFullPath(tempPath),
                WalPath = Path.GetFullPath(walPath),
                StorageMode = mode,
                CustomPath = customPath
            };
        }

        /// <summary>
        /// Validate that all required paths can be accessed
        /// </summary>
        public void ValidatePaths()
        {
            var pathsToValidate = new[] { NotesPath, MetadataPath, TempPath, WalPath };
            
            foreach (var path in pathsToValidate)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new InvalidOperationException($"Path cannot be null or empty: {path}");
                }

                try
                {
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Cannot access or create path '{path}': {ex.Message}", ex);
                }
            }
        }
    }
}
