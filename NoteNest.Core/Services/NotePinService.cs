using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Persists and serves note pin state. Stores data under .metadata/pins.json.
    /// Notes are identified by relative file paths under RootPath for portability.
    /// </summary>
    public class NotePinService
    {
        private readonly IFileSystemProvider _fileSystem;
        private readonly object _lock = new object();
        private volatile bool _loaded;
        private HashSet<string> _pinnedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private string PinsFilePath => Path.Combine(PathService.MetadataPath, "pins.json");

        public NotePinService(IFileSystemProvider fileSystem)
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
                    if (!_fileSystem.ExistsAsync(PinsFilePath).GetAwaiter().GetResult())
                    {
                        _loaded = true;
                        return;
                    }
                    var json = _fileSystem.ReadTextAsync(PinsFilePath).GetAwaiter().GetResult();
                    var data = JsonSerializer.Deserialize<PinsData>(json) ?? new PinsData();
                    _pinnedRelativePaths = new HashSet<string>(data.PinnedNotePaths ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                }
                catch
                {
                    _pinnedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                finally
                {
                    _loaded = true;
                }
            }
        }

        public bool IsPinned(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath)) return false;
            EnsureLoaded();
            var rel = PathService.ToRelativePath(PathService.NormalizeAbsolutePath(absolutePath) ?? absolutePath);
            lock (_lock)
            {
                return _pinnedRelativePaths.Contains(rel);
            }
        }

        public void SetPinned(string absolutePath, bool pinned)
        {
            if (string.IsNullOrWhiteSpace(absolutePath)) return;
            EnsureLoaded();
            var rel = PathService.ToRelativePath(PathService.NormalizeAbsolutePath(absolutePath) ?? absolutePath);
            lock (_lock)
            {
                if (pinned)
                    _pinnedRelativePaths.Add(rel);
                else
                    _pinnedRelativePaths.Remove(rel);
                SaveUnsafe();
            }
        }

        public bool Toggle(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath)) return false;
            EnsureLoaded();
            var rel = PathService.ToRelativePath(PathService.NormalizeAbsolutePath(absolutePath) ?? absolutePath);
            lock (_lock)
            {
                bool nowPinned;
                if (_pinnedRelativePaths.Contains(rel))
                {
                    _pinnedRelativePaths.Remove(rel);
                    nowPinned = false;
                }
                else
                {
                    _pinnedRelativePaths.Add(rel);
                    nowPinned = true;
                }
                SaveUnsafe();
                return nowPinned;
            }
        }

        public IReadOnlyList<string> GetAllPinnedRelativePaths()
        {
            EnsureLoaded();
            lock (_lock)
            {
                return _pinnedRelativePaths.ToList();
            }
        }

        private void SaveUnsafe()
        {
            try
            {
                var data = new PinsData { PinnedNotePaths = _pinnedRelativePaths.ToList() };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                var dir = Path.GetDirectoryName(PinsFilePath) ?? string.Empty;
                if (!_fileSystem.ExistsAsync(dir).GetAwaiter().GetResult())
                {
                    _fileSystem.CreateDirectoryAsync(dir).GetAwaiter().GetResult();
                }
                _fileSystem.WriteTextAsync(PinsFilePath, json).GetAwaiter().GetResult();
            }
            catch
            {
                // Swallow to avoid UI disruption; best-effort persistence
            }
        }

        private class PinsData
        {
            public List<string> PinnedNotePaths { get; set; } = new List<string>();
        }
    }
}


