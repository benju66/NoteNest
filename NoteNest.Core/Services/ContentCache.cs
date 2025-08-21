using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NoteNest.Core.Services
{
    public class ContentCache : IDisposable
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache;
        private readonly Timer? _cleanupTimer;
        private readonly int _maxCacheSize;
        private readonly TimeSpan _defaultExpiration;
        private long _currentSize;
        private bool _disposed;

        private class CacheEntry
        {
            public string Content { get; set; } = string.Empty;
            public DateTime LastAccessed { get; set; }
            public DateTime Created { get; set; }
            public int Size { get; set; }
            public int AccessCount { get; set; }
        }

        public ContentCache(int maxCacheSizeMB = 50, int expirationMinutes = 10, int cleanupMinutes = 5)
        {
            _cache = new ConcurrentDictionary<string, CacheEntry>();
            _maxCacheSize = maxCacheSizeMB * 1024 * 1024; // Convert to bytes
            _defaultExpiration = TimeSpan.FromMinutes(Math.Max(1, expirationMinutes));
            var cleanup = TimeSpan.FromMinutes(Math.Max(1, cleanupMinutes));
            _cleanupTimer = new Timer(CleanupExpired, null, cleanup, cleanup);
        }

        public ContentCache(IEventBus eventBus, int maxCacheSizeMB = 50, int expirationMinutes = 10, int cleanupMinutes = 5) : this(maxCacheSizeMB, expirationMinutes, cleanupMinutes)
        {
            if (eventBus != null)
            {
                eventBus.Subscribe<NoteNest.Core.Events.NoteSavedEvent>(e =>
                {
                    if (e?.FilePath != null)
                    {
                        InvalidateEntry(e.FilePath);
                    }
                });
            }
        }

        public async Task<string> GetContentAsync(string filePath, 
            Func<string, Task<string>> loadFunc)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            // Try to get from cache
            if (_cache.TryGetValue(filePath, out var entry))
            {
                entry.LastAccessed = DateTime.Now;
                entry.AccessCount++;
                return entry.Content;
            }

            // Load content if not in cache
            if (loadFunc == null)
                throw new ArgumentNullException(nameof(loadFunc));

            var content = await loadFunc(filePath);
            
            // Add to cache
            var size = content?.Length * 2 ?? 0; // Approximate size in bytes
            
            // Check if we need to make room
            if (Interlocked.Read(ref _currentSize) + size > _maxCacheSize)
            {
                await Task.Run(() => MakeRoom(size));
            }

            var newEntry = new CacheEntry
            {
                Content = content ?? string.Empty,
                Created = DateTime.Now,
                LastAccessed = DateTime.Now,
                Size = size,
                AccessCount = 1
            };

            if (_cache.TryAdd(filePath, newEntry))
            {
                Interlocked.Add(ref _currentSize, size);
            }

            return content ?? string.Empty;
        }

        public void InvalidateEntry(string filePath)
        {
            if (_cache.TryRemove(filePath, out var removed))
            {
                Interlocked.Add(ref _currentSize, -removed.Size);
            }
        }

        private void MakeRoom(int requiredSpace)
        {
            // Remove least recently used entries
            var sortedEntries = _cache
                .OrderBy(kvp => kvp.Value.LastAccessed)
                .ThenBy(kvp => kvp.Value.AccessCount)
                .ToList();

            int freedSpace = 0;
            foreach (var kvp in sortedEntries)
            {
                if (freedSpace >= requiredSpace) break;

                if (_cache.TryRemove(kvp.Key, out var removed))
                {
                    freedSpace += removed.Size;
                    Interlocked.Add(ref _currentSize, -removed.Size);
                }
            }
        }

        private void CleanupExpired(object? state)
        {
            if (_disposed) return;

            try
            {
                var expiredTime = DateTime.Now - _defaultExpiration;
                var toRemove = _cache
                    .Where(kvp => kvp.Value.LastAccessed < expiredTime)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in toRemove)
                {
                    if (_cache.TryRemove(key, out var removed))
                    {
                        Interlocked.Add(ref _currentSize, -removed.Size);
                    }
                }
            }
            catch
            {
                // Silently handle cleanup errors
            }
        }

        public void Clear()
        {
            _cache.Clear();
            Interlocked.Exchange(ref _currentSize, 0);
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            _cleanupTimer?.Dispose();
            Clear();
        }
    }
}