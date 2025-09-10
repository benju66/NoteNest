using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Search
{
    public class SearchDebouncer : IDisposable
    {
        private readonly ConcurrentDictionary<string, Timer> _timers = new();
        private readonly int _delayMs;
        private readonly IAppLogger _logger;
        private bool _disposed;

        public SearchDebouncer(int delayMs, IAppLogger logger)
        {
            _delayMs = delayMs;
            _logger = logger;
        }

        public void Debounce(string key, Func<Task> action)
        {
            if (_disposed) return;

            // Cancel existing timer for this key
            if (_timers.TryRemove(key, out var existingTimer))
            {
                existingTimer?.Dispose();
            }

            // Create new timer
            var timer = new Timer(async _ =>
            {
                try
                {
                    await action();
                    _timers.TryRemove(key, out var t);
                    t?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, $"Debounced action failed for key: {key}");
                }
            }, null, _delayMs, Timeout.Infinite);

            _timers[key] = timer;
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var timer in _timers.Values)
            {
                timer?.Dispose();
            }
            _timers.Clear();
        }
    }
}
