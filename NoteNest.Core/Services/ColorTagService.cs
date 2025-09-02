using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NoteNest.Core.Interfaces;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Stores per-item colors for categories (by Id) and notes (by relative path) in .metadata/colors.json
    /// </summary>
    public class ColorTagService
    {
        private readonly IFileSystemProvider _fileSystem;
        private readonly object _lock = new object();
        private volatile bool _loaded;
        private Dictionary<string, string> _categoryColors = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _noteColors = new(StringComparer.OrdinalIgnoreCase);

        private string ColorsFilePath => Path.Combine(PathService.MetadataPath, "colors.json");

        public ColorTagService(IFileSystemProvider fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                try
                {
                    // Use direct synchronous IO to avoid deadlocks at startup
                    if (File.Exists(ColorsFilePath))
                    {
                        var json = File.ReadAllText(ColorsFilePath);
                        var data = JsonSerializer.Deserialize<ColorData>(json) ?? new ColorData();
                        _categoryColors = data.CategoryColors ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        _noteColors = data.NoteColors ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch
                {
                    _categoryColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _noteColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                finally
                {
                    _loaded = true;
                }
            }
        }

        public string GetCategoryColor(string categoryId)
        {
            EnsureLoaded();
            lock (_lock)
            {
                return _categoryColors.TryGetValue(categoryId ?? string.Empty, out var hex) ? hex : null;
            }
        }

        public void SetCategoryColor(string categoryId, string hex)
        {
            EnsureLoaded();
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(hex))
                    _categoryColors.Remove(categoryId ?? string.Empty);
                else
                    _categoryColors[categoryId ?? string.Empty] = hex;
                SaveUnsafe();
            }
        }

        public string GetNoteColor(string absolutePath)
        {
            EnsureLoaded();
            var rel = PathService.ToRelativePath(PathService.NormalizeAbsolutePath(absolutePath) ?? absolutePath);
            lock (_lock)
            {
                return _noteColors.TryGetValue(rel ?? string.Empty, out var hex) ? hex : null;
            }
        }

        public void SetNoteColor(string absolutePath, string hex)
        {
            EnsureLoaded();
            var rel = PathService.ToRelativePath(PathService.NormalizeAbsolutePath(absolutePath) ?? absolutePath);
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(hex))
                    _noteColors.Remove(rel ?? string.Empty);
                else
                    _noteColors[rel ?? string.Empty] = hex;
                SaveUnsafe();
            }
        }

        private void SaveUnsafe()
        {
            try
            {
                var data = new ColorData { CategoryColors = _categoryColors, NoteColors = _noteColors };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                var dir = Path.GetDirectoryName(ColorsFilePath) ?? string.Empty;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(ColorsFilePath, json);
            }
            catch { }
        }

        private class ColorData
        {
            public Dictionary<string, string> CategoryColors { get; set; }
            public Dictionary<string, string> NoteColors { get; set; }
        }
    }
}


