using System;
using System.IO;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Centralized path management to eliminate hard-coded paths
    /// </summary>
    public static class PathService
    {
        private static string _rootPath = string.Empty;
        
        /// <summary>
        /// Gets the root path for NoteNest data
        /// Default: Documents/NoteNest
        /// </summary>
        public static string RootPath
        {
            get
            {
                if (string.IsNullOrEmpty(_rootPath))
                {
                    try
                    {
                        _rootPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                            "NoteNest");
                    }
                    catch
                    {
                        // Fallback to current directory if Documents folder fails
                        _rootPath = Path.Combine(Directory.GetCurrentDirectory(), "NoteNest");
                    }
                }
                return _rootPath;
            }
            set
            {
                _rootPath = value;
                // Try to ensure the directory exists when set
                try
                {
                    Directory.CreateDirectory(_rootPath);
                }
                catch
                {
                    // Ignore creation errors here - will be handled elsewhere
                }
            }
        }

        /// <summary>
        /// Gets the metadata directory path
        /// </summary>
        public static string MetadataPath => Path.Combine(RootPath, ".metadata");

        /// <summary>
        /// Gets the notes directory path
        /// </summary>
        public static string ProjectsPath => Path.Combine(RootPath, "Notes");

        /// <summary>
        /// Gets the templates directory path
        /// </summary>
        public static string TemplatesPath => Path.Combine(RootPath, "Templates");

        /// <summary>
        /// Gets the application data directory (for settings, logs, etc.)
        /// </summary>
        public static string AppDataPath
        {
            get
            {
                try
                {
                    var path = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "NoteNest");
                    Directory.CreateDirectory(path);
                    return path;
                }
                catch
                {
                    // Fallback to user profile if LocalAppData fails
                    try
                    {
                        var path = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            ".notenest");
                        Directory.CreateDirectory(path);
                        return path;
                    }
                    catch
                    {
                        // Last resort - use current directory
                        var path = Path.Combine(Directory.GetCurrentDirectory(), ".notenest");
                        Directory.CreateDirectory(path);
                        return path;
                    }
                }
            }
        }

        /// <summary>
        /// Gets the settings file path
        /// </summary>
        public static string SettingsPath => Path.Combine(AppDataPath, "settings.json");

        /// <summary>
        /// Gets the categories index file path
        /// </summary>
        public static string CategoriesPath => Path.Combine(MetadataPath, "categories.json");

        /// <summary>
        /// Ensures all required directories exist
        /// </summary>
        public static void EnsureDirectoriesExist()
        {
            var errors = new System.Collections.Generic.List<string>();

            // Try to create each directory and collect errors
            TryCreateDirectory(RootPath, "Root", errors);
            TryCreateDirectory(MetadataPath, "Metadata", errors);
            TryCreateDirectory(ProjectsPath, "Notes", errors);
            TryCreateDirectory(TemplatesPath, "Templates", errors);
            TryCreateDirectory(AppDataPath, "AppData", errors);

            if (errors.Count > 0)
            {
                var errorMessage = "Failed to create the following directories:\n" + string.Join("\n", errors);
                throw new InvalidOperationException(errorMessage);
            }
        }

        private static void TryCreateDirectory(string path, string name, System.Collections.Generic.List<string> errors)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"- {name} ({path}): {ex.Message}");
            }
        }

        /// <summary>
        /// Converts an absolute path to a relative path (for storage)
        /// </summary>
        public static string ToRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return absolutePath;

            try
            {
                if (absolutePath.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase))
                {
                    var relativePath = absolutePath.Substring(RootPath.Length);
                    if (relativePath.StartsWith(Path.DirectorySeparatorChar.ToString()))
                        relativePath = relativePath.Substring(1);
                    return relativePath;
                }
            }
            catch
            {
                // If any error, return the original path
            }

            // If not under root path or error occurred, return as-is
            return absolutePath;
        }

        /// <summary>
        /// Converts a relative path to an absolute path (for file operations)
        /// </summary>
        public static string ToAbsolutePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return relativePath;

            try
            {
                // If already absolute, return as-is
                if (Path.IsPathRooted(relativePath))
                    return relativePath;

                return Path.Combine(RootPath, relativePath);
            }
            catch
            {
                // If any error, return the original path
                return relativePath;
            }
        }

        /// <summary>
        /// Validates and sanitizes a file/folder name
        /// </summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Untitled";

            try
            {
                var invalidChars = Path.GetInvalidFileNameChars();
                var sanitized = string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
                
                // Additional safety for Windows reserved names
                string[] reservedNames = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", 
                                           "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", 
                                           "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
                
                foreach (var reserved in reservedNames)
                {
                    if (sanitized.Equals(reserved, StringComparison.OrdinalIgnoreCase))
                    {
                        sanitized = "_" + sanitized;
                        break;
                    }
                }

                return sanitized.Trim();
            }
            catch
            {
                // If sanitization fails, return a safe default
                return "Untitled";
            }
        }

        /// <summary>
        /// Returns true if the provided file name is valid after sanitization.
        /// </summary>
        public static bool IsValidFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var sanitized = SanitizeName(name);
                return !string.IsNullOrWhiteSpace(sanitized);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Normalize an absolute path (resolves '..' and returns full path).
        /// Returns null if invalid.
        /// </summary>
        public static string? NormalizeAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Ensures that a given absolute path lies under the configured RootPath.
        /// Returns false if the path is outside RootPath or invalid.
        /// </summary>
        public static bool IsUnderRoot(string absolutePath)
        {
            try
            {
                var full = NormalizeAbsolutePath(absolutePath);
                var root = NormalizeAbsolutePath(RootPath);
                if (string.IsNullOrEmpty(full) || string.IsNullOrEmpty(root)) return false;
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns true if a relative path does not traverse outside of RootPath when combined.
        /// </summary>
        public static bool IsSafeRelativePath(string relativePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relativePath)) return false;
                var combined = Path.Combine(RootPath, relativePath);
                return IsUnderRoot(combined);
            }
            catch
            {
                return false;
            }
        }
    }
}