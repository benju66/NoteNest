using System;
using System.IO;
using System.Threading.Tasks;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Service that handles first-time application setup before DI container initialization
    /// Ensures storage path is configured before any path-dependent services are created
    /// </summary>
    public static class FirstTimeSetupService
    {
        private static readonly IAppLogger _logger = AppLogger.Instance;
        private static string _configuredNotesPath = null;

        /// <summary>
        /// Get the configured notes path (set by EnsureStorageConfigurationAsync)
        /// This is used by DI container registration to avoid singleton path binding issues
        /// </summary>
        public static string ConfiguredNotesPath => _configuredNotesPath;

        /// <summary>
        /// Ensure storage location is configured before DI container initialization
        /// Returns true if setup completed successfully, false if user cancelled
        /// </summary>
        public static async Task<bool> EnsureStorageConfigurationAsync()
        {
            try
            {
                _logger.Info("FirstTimeSetupService: Starting storage configuration check");

                // Try to load existing settings
                var settingsPath = GetSettingsFilePath();
                AppSettings existingSettings = null;

                if (File.Exists(settingsPath))
                {
                    try
                    {
                        existingSettings = await LoadSettingsFromFileAsync(settingsPath);
                        _logger.Info($"FirstTimeSetupService: Loaded existing settings with path: {existingSettings.DefaultNotePath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"FirstTimeSetupService: Failed to load existing settings: {ex.Message}");
                    }
                }

                // Determine the notes path to use
                string notesPath = null;

                if (existingSettings != null && !string.IsNullOrEmpty(existingSettings.DefaultNotePath))
                {
                    notesPath = existingSettings.DefaultNotePath;
                }
                else
                {
                    // No existing settings or invalid path - use default
                    notesPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "NoteNest");
                    _logger.Info($"FirstTimeSetupService: Using default notes path: {notesPath}");
                }

                // Validate the path and check if it looks like a valid NoteNest installation
                var validationResult = await ValidateNotesPathAsync(notesPath);

                if (!validationResult.IsValid)
                {
                    _logger.Warning($"FirstTimeSetupService: Path validation failed: {validationResult.ErrorMessage}");
                    
                    // Show dialog to let user select their notes folder
                    notesPath = await PromptUserForNotesPathAsync(notesPath);
                    
                    if (string.IsNullOrEmpty(notesPath))
                    {
                        _logger.Warning("FirstTimeSetupService: User cancelled folder selection");
                        return false; // User cancelled
                    }

                    // Validate the selected path
                    validationResult = await ValidateNotesPathAsync(notesPath);
                    if (!validationResult.IsValid)
                    {
                        _logger.Error($"FirstTimeSetupService: Selected path is also invalid: {validationResult.ErrorMessage}");
                        return false;
                    }
                }

                // Ensure required directory structure exists
                await CreateRequiredDirectoriesAsync(notesPath);

                // Set the configured path for DI container to use
                _configuredNotesPath = notesPath;

                // Create/update settings file with the confirmed path
                var settings = existingSettings ?? new AppSettings();
                settings.DefaultNotePath = notesPath;
                settings.MetadataPath = Path.Combine(notesPath, ".metadata");
                await SaveSettingsAsync(settings, settingsPath);

                // Update PathService static property
                PathService.RootPath = notesPath;

                _logger.Info($"FirstTimeSetupService: Storage configuration completed successfully: {notesPath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "FirstTimeSetupService: Unexpected error during storage configuration");
                return false;
            }
        }

        /// <summary>
        /// Validate that a notes path is suitable for NoteNest
        /// </summary>
        private static async Task<ValidationResult> ValidateNotesPathAsync(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return ValidationResult.Failed("Path cannot be empty");
                }

                // Check if path is valid format
                if (!IsValidPathFormat(path))
                {
                    return ValidationResult.Failed("Invalid path format");
                }

                // Check if we can access the directory
                try
                {
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }

                    // Test write access
                    var testFile = Path.Combine(path, $"write_test_{Guid.NewGuid():N}.tmp");
                    await File.WriteAllTextAsync(testFile, "test");
                    File.Delete(testFile);
                }
                catch (Exception ex)
                {
                    return ValidationResult.Failed($"Cannot access directory: {ex.Message}");
                }

                // Check if it looks like an existing NoteNest installation
                var categoriesFile = Path.Combine(path, ".metadata", "categories.json");
                var hasCategories = File.Exists(categoriesFile);
                var notesFolder = Path.Combine(path, "Notes");
                var hasNotesFolder = Directory.Exists(notesFolder);

                _logger.Debug($"FirstTimeSetupService: Path validation - hasCategories: {hasCategories}, hasNotesFolder: {hasNotesFolder}");

                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                return ValidationResult.Failed($"Validation error: {ex.Message}");
            }
        }

        /// <summary>
        /// Create all required NoteNest directory structure
        /// </summary>
        private static async Task CreateRequiredDirectoriesAsync(string basePath)
        {
            try
            {
                _logger.Info($"FirstTimeSetupService: Creating required directories in {basePath}");

                var directories = new[]
                {
                    basePath,
                    Path.Combine(basePath, ".metadata"),
                    Path.Combine(basePath, ".temp"),
                    Path.Combine(basePath, ".wal"),
                    Path.Combine(basePath, "Notes")
                };

                foreach (var dir in directories)
                {
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                        _logger.Debug($"FirstTimeSetupService: Created directory: {dir}");
                    }
                }

                // Create an initial empty categories file if it doesn't exist
                var categoriesFile = Path.Combine(basePath, ".metadata", "categories.json");
                if (!File.Exists(categoriesFile))
                {
                    var initialCategories = new { Categories = new object[0] };
                    var json = System.Text.Json.JsonSerializer.Serialize(initialCategories, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(categoriesFile, json);
                    _logger.Debug($"FirstTimeSetupService: Created initial categories file: {categoriesFile}");
                }

                _logger.Info("FirstTimeSetupService: Required directories created successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "FirstTimeSetupService: Failed to create required directories");
                throw;
            }
        }

        /// <summary>
        /// Prompt user to select their notes folder using a dialog
        /// Uses the UserSelectionCallback provided by the UI layer
        /// </summary>
        private static async Task<string> PromptUserForNotesPathAsync(string currentPath)
        {
            if (UserSelectionCallback != null)
            {
                try
                {
                    _logger.Debug("FirstTimeSetupService: Prompting user for notes folder selection");
                    return await UserSelectionCallback(currentPath);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "FirstTimeSetupService: Error in UserSelectionCallback");
                    return null;
                }
            }
            
            // No callback available - this should not happen in normal operation
            _logger.Warning("FirstTimeSetupService: No UserSelectionCallback available");
            return null;
        }

        /// <summary>
        /// Set the user selection callback for the UI layer
        /// </summary>
        public static Func<string, Task<string>> UserSelectionCallback { get; set; }

        /// <summary>
        /// Load settings from file
        /// </summary>
        private static async Task<AppSettings> LoadSettingsFromFileAsync(string settingsPath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(settingsPath);
                return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                _logger.Warning($"FirstTimeSetupService: Failed to parse settings file: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Save settings to file
        /// </summary>
        private static async Task SaveSettingsAsync(AppSettings settings, string settingsPath)
        {
            try
            {
                // Ensure settings directory exists
                var settingsDir = Path.GetDirectoryName(settingsPath);
                if (!Directory.Exists(settingsDir))
                {
                    Directory.CreateDirectory(settingsDir);
                }

                var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(settingsPath, json);
                _logger.Debug($"FirstTimeSetupService: Settings saved to {settingsPath}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "FirstTimeSetupService: Failed to save settings");
                throw;
            }
        }

        /// <summary>
        /// Get the path to the settings file
        /// </summary>
        private static string GetSettingsFilePath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var noteNestPath = Path.Combine(appDataPath, "NoteNest");
            return Path.Combine(noteNestPath, "settings.json");
        }

        /// <summary>
        /// Check if a path format is valid
        /// </summary>
        private static bool IsValidPathFormat(string path)
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

        /// <summary>
        /// Simple validation result class
        /// </summary>
        private class ValidationResult
        {
            public bool IsValid { get; set; }
            public string ErrorMessage { get; set; } = string.Empty;

            public static ValidationResult Success() => new ValidationResult { IsValid = true };
            public static ValidationResult Failed(string error) => new ValidationResult { IsValid = false, ErrorMessage = error };
        }
    }
}
