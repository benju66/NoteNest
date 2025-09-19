using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Core.Models;

namespace NoteNest.Core.Interfaces.Services
{
    /// <summary>
    /// Service for validating paths, data, and names in NoteNest
    /// </summary>
    public interface IValidationService
    {
        /// <summary>
        /// Validate a complete NoteNest dataset at the specified path
        /// </summary>
        Task<ValidationResult> ValidateNoteNestDatasetAsync(string path);
        
        /// <summary>
        /// Validate a storage location for the specified mode
        /// </summary>
        Task<ValidationResult> ValidateStorageLocationAsync(string path, StorageMode mode);
        
        /// <summary>
        /// Check if a note name is valid
        /// </summary>
        bool IsValidNoteName(string name);
        
        /// <summary>
        /// Sanitize a note name by removing invalid characters
        /// </summary>
        string SanitizeNoteName(string name);
    }

    /// <summary>
    /// Result of a validation operation
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public Dictionary<string, bool> ComponentStatus { get; set; } = new();
        
        public static ValidationResult Success()
        {
            return new ValidationResult { IsValid = true };
        }
        
        public static ValidationResult Failed(string error)
        {
            return new ValidationResult 
            { 
                IsValid = false, 
                Errors = new List<string> { error }
            };
        }
        
        public static ValidationResult Failed(List<string> errors)
        {
            return new ValidationResult 
            { 
                IsValid = false, 
                Errors = errors ?? new List<string>()
            };
        }
    }
}
