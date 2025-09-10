using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Search
{
    public class SearchIndexPersistence
    {
        private readonly IAppLogger _logger;
        private readonly string _indexPath;
        private readonly JsonSerializerOptions _jsonOptions;

        public SearchIndexPersistence(string rootPath, IAppLogger logger)
        {
            _logger = logger;
            _indexPath = Path.Combine(rootPath, ".notenest", "search-index.json");
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNameCaseInsensitive = true
            };
        }

        public async Task<PersistedIndex?> LoadIndexAsync()
        {
            try
            {
                if (!File.Exists(_indexPath))
                    return null;

                var json = await File.ReadAllTextAsync(_indexPath);
                var index = JsonSerializer.Deserialize<PersistedIndex>(json, _jsonOptions);
                
                // Validate index version
                if (index?.Version != PersistedIndex.CurrentVersion)
                {
                    _logger?.Info("Index version mismatch, will rebuild");
                    return null;
                }

                _logger?.Info($"Loaded search index with {index.Entries.Count} entries");
                return index;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to load search index: {ex.Message}");
                return null;
            }
        }

        public async Task SaveIndexAsync(PersistedIndex index)
        {
            try
            {
                var dir = Path.GetDirectoryName(_indexPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(index, _jsonOptions);
                
                // Write to temp file first for atomicity
                var tempFile = _indexPath + ".tmp";
                await File.WriteAllTextAsync(tempFile, json);
                
                // Atomic replace
                if (File.Exists(_indexPath))
                    File.Replace(tempFile, _indexPath, null);
                else
                    File.Move(tempFile, _indexPath);

                _logger?.Debug($"Saved search index with {index.Entries.Count} entries");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to save search index");
            }
        }

        public async Task<bool> ValidateIndexAsync(PersistedIndex index, string rootPath)
        {
            try
            {
                // Quick sample validation - check 10 random files
                var sample = index.Entries.Take(10).ToList();
                foreach (var entry in sample)
                {
                    var fullPath = Path.Combine(rootPath, entry.RelativePath);
                    if (!File.Exists(fullPath))
                        return false;
                    
                    var fileInfo = new FileInfo(fullPath);
                    if (Math.Abs((fileInfo.LastWriteTimeUtc - entry.LastModified).TotalSeconds) > 1)
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public class PersistedIndex
    {
        public const string CurrentVersion = "2.0";
        public string Version { get; set; } = CurrentVersion;
        public DateTime CreatedAt { get; set; }
        public List<IndexEntry> Entries { get; set; } = new();
    }

    public class IndexEntry
    {
        public string Id { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ContentPreview { get; set; } = string.Empty; // First 500 words
        public List<string> Tags { get; set; } = new();
        public DateTime LastModified { get; set; }
        public long FileSize { get; set; }
    }
}
