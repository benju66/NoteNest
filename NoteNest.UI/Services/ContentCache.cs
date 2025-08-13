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
        private readonly Timer _cleanupTimer;
        private readonly int _maxCacheSize;
        private readonly TimeSpan _defaultExpiration;
        private long _currentSize;
        private bool _disposed;

        private class CacheEntry
        {
            public string Content { get; set; }
            public DateTime LastAccessed { get; set; }
            public DateTime Created { get; set; }
            public int Size { get; set; }
            public int AccessCount { get; set; }
        }

        public ContentCache(int maxCacheSizeMB = 50)
        {
            _cache = new ConcurrentDictionary<string, CacheEntry>();
            _maxCacheSize = maxCacheSizeMB * 1024 * 1024; // Convert to bytes
            _defaultExpiration = TimeSpan.FromMinutes(10);
            _cleanupTimer = new Timer(CleanupExpired, null, 
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
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

            // Load content
            var content = await loadFunc(filePath);

            // Add to cache
            await AddToCacheAsync(filePath, content);

            return content;
        }

        private async Task AddToCacheAsync(string filePath, string content)
        {
            if (string.IsNullOrEmpty(content)) return;

            var size = content.Length * sizeof(char);

            // Check if we need to make room
            if (Interlocked.Read(ref _currentSize) + size > _maxCacheSize)
            {
                await EvictLeastRecentlyUsedAsync(size);
            }

            var entry = new CacheEntry
            {
                Content = content,
                Created = DateTime.Now,
                LastAccessed = DateTime.Now,
                Size = size,
                AccessCount = 1
            };

            if (_cache.TryAdd(filePath, entry))
            {
                Interlocked.Add(ref _currentSize, size);
            }
        }

        private async Task EvictLeastRecentlyUsedAsync(int requiredSpace)
        {
            await Task.Run(() =>
            {
                var sortedEntries = _cache
                    .OrderBy(kvp => kvp.Value.LastAccessed)
                    .ThenBy(kvp => kvp.Value.AccessCount)
                    .ToList();

                var freedSpace = 0;
                foreach (var kvp in sortedEntries)
                {
                    if (freedSpace >= requiredSpace) break;

                    if (_cache.TryRemove(kvp.Key, out var removed))
                    {
                        freedSpace += removed.Size;
                        Interlocked.Add(ref _currentSize, -removed.Size);
                    }
                }
            });
        }

        private void CleanupExpired(object state)
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