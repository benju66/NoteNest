using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Basic validation service for NoteNest paths, data, and names
    /// </summary>
    public class ValidationService : IValidationService
    {
        private readonly IAppLogger _logger;

        public ValidationService(IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Validate a complete NoteNest dataset at the specified path
        /// </summary>
        public async Task<ValidationResult> ValidateNoteNestDatasetAsync(string path)
        {
            try
            {
                var result = new ValidationResult();
                
                if (string.IsNullOrWhiteSpace(path))
                {
                    result.Errors.Add("Path cannot be null or empty");
                    return result;
                }

                if (!Directory.Exists(path))
                {
                    result.Errors.Add($"Directory does not exist: {path}");
                    return result;
                }

                // Check for required NoteNest directories
                var requiredDirs = new[]
                {
                    ".metadata",
                    "Notes"
                };

                foreach (var dir in requiredDirs)
                {
                    var dirPath = Path.Combine(path, dir);
                    if (Directory.Exists(dirPath))
                    {
                        result.ComponentStatus[dir] = true;
                    }
                    else
                    {
                        result.ComponentStatus[dir] = false;
                        result.Warnings.Add($"Directory missing: {dir} (will be created if needed)");
                    }
                }

                // Check for categories.json
                var categoriesPath = Path.Combine(path, ".metadata", "categories.json");
                if (File.Exists(categoriesPath))
                {
                    result.ComponentStatus["categories.json"] = true;
                    
                    // Try to read categories file to verify it's valid
                    try
                    {
                        var content = await File.ReadAllTextAsync(categoriesPath);
                        if (string.IsNullOrEmpty(content))
                        {
                            result.Warnings.Add("categories.json is empty");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Cannot read categories.json: {ex.Message}");
                    }
                }
                else
                {
                    result.ComponentStatus["categories.json"] = false;
                    result.Warnings.Add("categories.json not found (normal for new installations)");
                }

                // If we have no critical errors, the dataset is valid
                result.IsValid = result.Errors.Count == 0;
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Exception during dataset validation: {path}");
                return ValidationResult.Failed($"Exception during validation: {ex.Message}");
            }
        }

        /// <summary>
        /// Validate a storage location for the specified mode
        /// </summary>
        public async Task<ValidationResult> ValidateStorageLocationAsync(string path, StorageMode mode)
        {
            try
            {
                var result = new ValidationResult();
                
                if (string.IsNullOrWhiteSpace(path))
                {
                    result.Errors.Add("Storage path cannot be null or empty");
                    return result;
                }

                // Validate path format
                if (!IsValidPath(path))
                {
                    result.Errors.Add($"Invalid path format: {path}");
                    return result;
                }

                // Mode-specific validation
                switch (mode)
                {
                    case StorageMode.Local:
                        await ValidateLocalStorageAsync(path, result);
                        break;
                        
                    case StorageMode.OneDrive:
                        await ValidateOneDriveStorageAsync(path, result);
                        break;
                        
                    case StorageMode.Custom:
                        await ValidateCustomStorageAsync(path, result);
                        break;
                }

                // Common validations
                await ValidateCommonStorageRequirementsAsync(path, result);

                result.IsValid = result.Errors.Count == 0;
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Exception during storage location validation: {path}");
                return ValidationResult.Failed($"Exception during validation: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a note name is valid
        /// </summary>
        public bool IsValidNoteName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Check for invalid file name characters
            var invalidChars = Path.GetInvalidFileNameChars();
            if (name.Any(c => invalidChars.Contains(c)))
                return false;

            // Check for reserved names on Windows
            var reservedNames = new[] { "CON", "PRN", "AUX", "NUL" };
            if (reservedNames.Any(reserved => 
                string.Equals(name, reserved, StringComparison.OrdinalIgnoreCase)))
                return false;

            return true;
        }

        /// <summary>
        /// Sanitize a note name by removing invalid characters
        /// </summary>
        public string SanitizeNoteName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Untitled";

            try
            {
                var invalidChars = Path.GetInvalidFileNameChars();
                var sanitized = string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
                
                // Handle reserved names
                var reservedNames = new[] { "CON", "PRN", "AUX", "NUL" };
                if (reservedNames.Any(reserved => 
                    string.Equals(sanitized, reserved, StringComparison.OrdinalIgnoreCase)))
                {
                    sanitized = "_" + sanitized;
                }

                return string.IsNullOrWhiteSpace(sanitized) ? "Untitled" : sanitized.Trim();
            }
            catch
            {
                return "Untitled";
            }
        }

        private bool IsValidPath(string path)
        {
            try
            {
                Path.GetFullPath(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task ValidateLocalStorageAsync(string path, ValidationResult result)
        {
            // For local storage, just check that path is accessible
            try
            {
                var documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var fullPath = Path.GetFullPath(path);
                
                if (!fullPath.StartsWith(documentsFolder, StringComparison.OrdinalIgnoreCase))
                {
                    result.Warnings.Add("Local storage path is outside Documents folder");
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Could not validate local storage path: {ex.Message}");
            }
            
            await Task.CompletedTask;
        }

        private async Task ValidateOneDriveStorageAsync(string path, ValidationResult result)
        {
            // For OneDrive storage, check if path is in OneDrive folder
            try
            {
                var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
                if (string.IsNullOrEmpty(oneDrive))
                {
                    result.Warnings.Add("OneDrive environment variable not found");
                }
                else
                {
                    var fullPath = Path.GetFullPath(path);
                    var oneDrivePath = Path.GetFullPath(oneDrive);
                    
                    if (!fullPath.StartsWith(oneDrivePath, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Warnings.Add("Path is not in OneDrive folder - sync may not work");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Could not validate OneDrive storage path: {ex.Message}");
            }
            
            await Task.CompletedTask;
        }

        private async Task ValidateCustomStorageAsync(string path, ValidationResult result)
        {
            // For custom storage, do basic accessibility check
            try
            {
                if (!Directory.Exists(path))
                {
                    result.Warnings.Add("Custom storage directory does not exist - will be created");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Cannot access custom storage path: {ex.Message}");
            }
            
            await Task.CompletedTask;
        }

        private async Task ValidateCommonStorageRequirementsAsync(string path, ValidationResult result)
        {
            try
            {
                // Test write permissions
                var testFile = Path.Combine(path, $"write_test_{Guid.NewGuid():N}.tmp");
                try
                {
                    // Ensure directory exists for test
                    Directory.CreateDirectory(path);
                    
                    await File.WriteAllTextAsync(testFile, "write test");
                    File.Delete(testFile);
                    
                    result.ComponentStatus["WriteAccess"] = true;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"No write access to storage location: {ex.Message}");
                    result.ComponentStatus["WriteAccess"] = false;
                }

                // Check available space
                try
                {
                    var drive = new DriveInfo(Path.GetPathRoot(path));
                    var availableSpace = drive.AvailableFreeSpace;
                    var requiredSpace = 100 * 1024 * 1024; // 100 MB

                    if (availableSpace < requiredSpace)
                    {
                        result.Errors.Add($"Insufficient disk space. Available: {availableSpace / 1024 / 1024} MB, Required: {requiredSpace / 1024 / 1024} MB");
                        result.ComponentStatus["DiskSpace"] = false;
                    }
                    else
                    {
                        result.ComponentStatus["DiskSpace"] = true;
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Could not check disk space: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Common validation failed: {ex.Message}");
            }
        }
    }
}
