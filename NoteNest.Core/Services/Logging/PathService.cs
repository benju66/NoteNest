using System;
using System.IO;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Centralized path management to eliminate hard-coded paths
    /// </summary>
    public static class PathService
    {
        private static string _rootPath;
        
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
                    _rootPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "NoteNest");
                }
                return _rootPath;
            }
            set
            {
                _rootPath = value;
                // Ensure the directory exists when set
                Directory.CreateDirectory(_rootPath);
            }
        }

        /// <summary>
        /// Gets the metadata directory path
        /// </summary>
        public static string MetadataPath => Path.Combine(RootPath, ".metadata");

        /// <summary>
        /// Gets the projects directory path
        /// </summary>
        public static string ProjectsPath => Path.Combine(RootPath, "Projects");

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
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NoteNest");
                Directory.CreateDirectory(path);
                return path;
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
            Directory.CreateDirectory(RootPath);
            Directory.CreateDirectory(MetadataPath);
            Directory.CreateDirectory(ProjectsPath);
            Directory.CreateDirectory(TemplatesPath);
            Directory.CreateDirectory(AppDataPath);
        }

        /// <summary>
        /// Converts an absolute path to a relative path (for storage)
        /// </summary>
        public static string ToRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return absolutePath;

            if (absolutePath.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = absolutePath.Substring(RootPath.Length);
                if (relativePath.StartsWith(Path.DirectorySeparatorChar.ToString()))
                    relativePath = relativePath.Substring(1);
                return relativePath;
            }

            // If not under root path, return as-is
            return absolutePath;
        }

        /// <summary>
        /// Converts a relative path to an absolute path (for file operations)
        /// </summary>
        public static string ToAbsolutePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return relativePath;

            // If already absolute, return as-is
            if (Path.IsPathRooted(relativePath))
                return relativePath;

            return Path.Combine(RootPath, relativePath);
        }

        /// <summary>
        /// Validates and sanitizes a file/folder name
        /// </summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Untitled";

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
    }
}