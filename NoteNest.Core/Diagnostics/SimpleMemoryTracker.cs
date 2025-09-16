using System;
using System.Diagnostics;

namespace NoteNest.Core.Diagnostics
{
    /// <summary>
    /// Simple memory tracking for production diagnostics
    /// High-impact, low-risk memory monitoring
    /// </summary>
    public static class SimpleMemoryTracker
    {
        private static long _baselineMemory = 0;
        private static int _tabCount = 0;
        private static long _totalContentSize = 0;
        private static readonly object _trackingLock = new object();
        
        /// <summary>
        /// Set memory baseline when app starts
        /// </summary>
        public static void SetBaseline()
        {
            _baselineMemory = GC.GetTotalMemory(true);
            DebugLogger.Log($"Memory baseline set: {_baselineMemory / 1024 / 1024}MB");
        }
        
        /// <summary>
        /// Track memory when tab is created
        /// </summary>
        public static void TrackTabCreation(string tabTitle = "")
        {
            lock (_trackingLock)
            {
                _tabCount++;
                
                // Log every 5 tabs or significant memory changes
                if (_tabCount % 5 == 0 || ShouldLogMemoryUpdate())
                {
                    LogCurrentMemoryUsage($"Tab created: {tabTitle}");
                }
            }
        }
        
        /// <summary>
        /// Track memory when tab is disposed
        /// </summary>
        public static void TrackTabDisposal(string tabTitle = "")
        {
            lock (_trackingLock)
            {
                _tabCount = Math.Max(0, _tabCount - 1);
                
                if (_tabCount % 5 == 0 || ShouldLogMemoryUpdate())
                {
                    LogCurrentMemoryUsage($"Tab disposed: {tabTitle}");
                }
            }
        }
        
        /// <summary>
        /// Track content loading for size analysis
        /// </summary>
        public static void TrackContentLoad(long contentSize, string tabTitle = "")
        {
            lock (_trackingLock)
            {
                _totalContentSize += contentSize;
                
                if (contentSize > 100_000)  // Log large documents (100KB+)
                {
                    DebugLogger.Log($"Large content loaded: {contentSize / 1024}KB for {tabTitle}");
                }
            }
        }
        
        /// <summary>
        /// Force memory logging (for debugging)
        /// </summary>
        public static void LogMemoryStatus(string context = "")
        {
            LogCurrentMemoryUsage(context);
        }
        
        private static void LogCurrentMemoryUsage(string context)
        {
            try
            {
                var currentMemory = GC.GetTotalMemory(false);
                var deltaMemory = currentMemory - _baselineMemory;
                var memoryPerTab = _tabCount > 0 ? deltaMemory / _tabCount : 0;
                var avgContentSize = _tabCount > 0 ? _totalContentSize / _tabCount : 0;
                
                DebugLogger.Log($"[MEMORY] {context} | " +
                              $"Total: {currentMemory / 1024 / 1024}MB | " +
                              $"Delta: {deltaMemory / 1024 / 1024}MB | " +
                              $"Per Tab: {memoryPerTab / 1024}KB | " +
                              $"Tabs: {_tabCount} | " +
                              $"Avg Content: {avgContentSize / 1024}KB");
                
                // Check for concerning patterns
                if (memoryPerTab > 10 * 1024 * 1024)  // >10MB per tab
                {
                    DebugLogger.Log($"WARNING: High memory per tab detected: {memoryPerTab / 1024 / 1024}MB");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SimpleMemoryTracker] Logging failed: {ex.Message}");
            }
        }
        
        private static bool ShouldLogMemoryUpdate()
        {
            // Log if memory has grown significantly since baseline
            var currentMemory = GC.GetTotalMemory(false);
            var deltaMemory = currentMemory - _baselineMemory;
            
            // Log if we've grown by more than 50MB
            return deltaMemory > 50 * 1024 * 1024;
        }
        
        /// <summary>
        /// Get memory statistics for diagnostics
        /// </summary>
        public static (long totalMB, long deltaMB, long perTabKB, int tabCount) GetStats()
        {
            lock (_trackingLock)
            {
                var currentMemory = GC.GetTotalMemory(false);
                var deltaMemory = currentMemory - _baselineMemory;
                var memoryPerTab = _tabCount > 0 ? deltaMemory / _tabCount : 0;
                
                return (
                    totalMB: currentMemory / 1024 / 1024,
                    deltaMB: deltaMemory / 1024 / 1024, 
                    perTabKB: memoryPerTab / 1024,
                    tabCount: _tabCount
                );
            }
        }
    }
    
    /// <summary>
    /// Enhanced debug logger with memory awareness
    /// </summary>
    public static class DebugLogger
    {
        [Conditional("DEBUG")]
        public static void Log(string message)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }
        
        [Conditional("DEBUG")]
        public static void LogMemory(string context)
        {
            SimpleMemoryTracker.LogMemoryStatus(context);
        }
        
        [Conditional("DEBUG")] 
        public static void LogPerformance(string operation, TimeSpan duration)
        {
            if (duration.TotalMilliseconds > 10)
            {
                Log($"PERF: {operation} took {duration.TotalMilliseconds:F1}ms");
            }
        }
    }
}
