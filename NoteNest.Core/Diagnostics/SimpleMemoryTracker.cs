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
        
        // Enhanced tracking for detached windows (Phase 6)
        private static int _detachedWindowCount = 0;
        private static int _totalWindowCount = 1; // Start with 1 for main window
        
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
        /// Track memory when detached window is created (Phase 6)
        /// </summary>
        public static void TrackDetachedWindowCreation(string windowId = "")
        {
            lock (_trackingLock)
            {
                _detachedWindowCount++;
                _totalWindowCount++;
                
                // Always log detached window creation (significant event)
                LogCurrentMemoryUsage($"Detached window created: {windowId}");
                
                DebugLogger.Log($"[WINDOWS] Created detached window {windowId}. " +
                              $"Total windows: {_totalWindowCount} (Main + {_detachedWindowCount} detached)");
            }
        }
        
        /// <summary>
        /// Track memory when detached window is disposed (Phase 6)
        /// </summary>
        public static void TrackDetachedWindowDisposal(string windowId = "")
        {
            lock (_trackingLock)
            {
                _detachedWindowCount = Math.Max(0, _detachedWindowCount - 1);
                _totalWindowCount = Math.Max(1, _totalWindowCount - 1); // Always keep at least main window
                
                // Always log detached window disposal (significant event)
                LogCurrentMemoryUsage($"Detached window disposed: {windowId}");
                
                DebugLogger.Log($"[WINDOWS] Disposed detached window {windowId}. " +
                              $"Remaining windows: {_totalWindowCount} (Main + {_detachedWindowCount} detached)");
                
                // Force garbage collection after window disposal to reclaim memory
                if (_detachedWindowCount == 0)
                {
                    DebugLogger.Log("[MEMORY] All detached windows closed, forcing garbage collection");
                    GC.Collect(2, GCCollectionMode.Default, true);
                    GC.WaitForPendingFinalizers();
                }
            }
        }
        
        /// <summary>
        /// Get current window count for diagnostics
        /// </summary>
        public static (int TotalWindows, int DetachedWindows) GetWindowCounts()
        {
            lock (_trackingLock)
            {
                return (_totalWindowCount, _detachedWindowCount);
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
                var memoryPerWindow = _totalWindowCount > 0 ? deltaMemory / _totalWindowCount : 0;
                var avgContentSize = _tabCount > 0 ? _totalContentSize / _tabCount : 0;
                
                var detachedWindowInfo = _detachedWindowCount > 0 
                    ? $" | Detached Windows: {_detachedWindowCount}"
                    : "";
                
                DebugLogger.Log($"[MEMORY] {context} | " +
                              $"Total: {currentMemory / 1024 / 1024}MB | " +
                              $"Delta: {deltaMemory / 1024 / 1024}MB | " +
                              $"Per Tab: {memoryPerTab / 1024}KB | " +
                              $"Per Window: {memoryPerWindow / 1024}KB | " +
                              $"Tabs: {_tabCount} | Windows: {_totalWindowCount}{detachedWindowInfo} | " +
                              $"Avg Content: {avgContentSize / 1024}KB");
                
                // Enhanced warnings for multi-window scenarios
                if (memoryPerTab > 10 * 1024 * 1024)  // >10MB per tab
                {
                    DebugLogger.Log($"WARNING: High memory per tab detected: {memoryPerTab / 1024 / 1024}MB");
                }
                
                if (_detachedWindowCount > 0 && memoryPerWindow > 50 * 1024 * 1024) // >50MB per window
                {
                    DebugLogger.Log($"WARNING: High memory per window detected: {memoryPerWindow / 1024 / 1024}MB " +
                                  $"({_detachedWindowCount} detached windows)");
                }
                
                if (_detachedWindowCount >= 3) // Alert when many windows are open
                {
                    DebugLogger.Log($"INFO: {_detachedWindowCount} detached windows active, " +
                                  $"consider consolidating tabs for better performance");
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

#if DEBUG
    /// <summary>
    /// Enhanced memory tracking for service-level diagnostics
    /// Extends SimpleMemoryTracker for detailed analysis
    /// </summary>
    public static class EnhancedMemoryTracker
    {
        private static readonly Dictionary<string, ServiceMemoryInfo> _serviceStats = new();
        private static readonly List<MemorySnapshot> _snapshots = new();
        private static readonly object _enhancedLock = new object();
        private static DateTime _lastSnapshotTime = DateTime.MinValue;
        private static readonly TimeSpan _snapshotInterval = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Track memory usage for a specific service operation
        /// </summary>
        public static void TrackServiceOperation<T>(string operation, Action action)
        {
            TrackServiceOperation(typeof(T).Name, operation, action);
        }

        /// <summary>
        /// Track memory usage for a named service operation
        /// </summary>
        public static void TrackServiceOperation(string serviceName, string operation, Action action)
        {
            var before = GC.GetTotalMemory(false);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                action();
            }
            finally
            {
                stopwatch.Stop();
                var after = GC.GetTotalMemory(false);
                var delta = after - before;

                RecordServiceOperation(serviceName, operation, delta, stopwatch.Elapsed);
            }
        }

        /// <summary>
        /// Track memory usage for a specific service operation (async)
        /// </summary>
        public static async Task TrackServiceOperationAsync<T>(string operation, Func<Task> action)
        {
            await TrackServiceOperationAsync(typeof(T).Name, operation, action);
        }

        /// <summary>
        /// Track memory usage for a named service operation (async)
        /// </summary>
        public static async Task TrackServiceOperationAsync(string serviceName, string operation, Func<Task> action)
        {
            var before = GC.GetTotalMemory(false);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                await action();
            }
            finally
            {
                stopwatch.Stop();
                var after = GC.GetTotalMemory(false);
                var delta = after - before;

                RecordServiceOperation(serviceName, operation, delta, stopwatch.Elapsed);
            }
        }

        /// <summary>
        /// Record a service operation's memory impact
        /// </summary>
        private static void RecordServiceOperation(string serviceName, string operation, long memoryDeltaBytes, TimeSpan duration)
        {
            lock (_enhancedLock)
            {
                if (!_serviceStats.ContainsKey(serviceName))
                {
                    _serviceStats[serviceName] = new ServiceMemoryInfo
                    {
                        ServiceName = serviceName,
                        Operations = new List<OperationMemoryInfo>()
                    };
                }

                var serviceInfo = _serviceStats[serviceName];
                serviceInfo.TotalMemoryDeltaBytes += memoryDeltaBytes;
                serviceInfo.OperationCount++;
                serviceInfo.LastOperationTime = DateTime.Now;

                // Track individual operations for analysis
                serviceInfo.Operations.Add(new OperationMemoryInfo
                {
                    OperationName = operation,
                    MemoryDeltaBytes = memoryDeltaBytes,
                    Duration = duration,
                    Timestamp = DateTime.Now
                });

                // Keep only recent operations (last 100 per service)
                if (serviceInfo.Operations.Count > 100)
                {
                    serviceInfo.Operations.RemoveRange(0, serviceInfo.Operations.Count - 100);
                }

                // Log significant memory changes
                if (Math.Abs(memoryDeltaBytes) > 4096) // > 4KB
                {
                    DebugLogger.Log($"[ENHANCED-MEMORY] {serviceName}.{operation}: {memoryDeltaBytes / 1024.0:+0.0;-0.0;0.0}KB ({duration.TotalMilliseconds:F1}ms)");
                }

                // Create snapshots periodically
                TakeSnapshotIfNeeded();
            }
        }

        /// <summary>
        /// Take a memory snapshot if enough time has passed
        /// </summary>
        private static void TakeSnapshotIfNeeded()
        {
            var now = DateTime.Now;
            if (now - _lastSnapshotTime >= _snapshotInterval)
            {
                _lastSnapshotTime = now;

                var snapshot = new MemorySnapshot
                {
                    Timestamp = now,
                    TotalMemoryBytes = GC.GetTotalMemory(false),
                    ServiceStats = _serviceStats.Values.Select(s => new ServiceMemoryInfo
                    {
                        ServiceName = s.ServiceName,
                        TotalMemoryDeltaBytes = s.TotalMemoryDeltaBytes,
                        OperationCount = s.OperationCount,
                        LastOperationTime = s.LastOperationTime,
                        Operations = new List<OperationMemoryInfo>() // Don't duplicate operations in snapshots
                    }).ToList()
                };

                _snapshots.Add(snapshot);

                // Keep only recent snapshots (last 100)
                if (_snapshots.Count > 100)
                {
                    _snapshots.RemoveRange(0, _snapshots.Count - 100);
                }
            }
        }

        /// <summary>
        /// Get comprehensive memory statistics for dashboard
        /// </summary>
        public static MemoryDashboardStats GetDashboardStats()
        {
            lock (_enhancedLock)
            {
                var baseStats = SimpleMemoryTracker.GetStats();
                var currentMemory = GC.GetTotalMemory(false);

                return new MemoryDashboardStats
                {
                    // Base statistics
                    TotalMemoryMB = baseStats.totalMB,
                    DeltaFromBaselineMB = baseStats.deltaMB,
                    MemoryPerTabKB = baseStats.perTabKB,
                    ActiveTabCount = baseStats.tabCount,

                    // Service-level statistics
                    ServiceBreakdown = _serviceStats.Values
                        .OrderByDescending(s => Math.Abs(s.TotalMemoryDeltaBytes))
                        .Take(10)
                        .ToList(),

                    // Leak detection
                    PotentialLeaks = DetectPotentialLeaks(),

                    // Recent activity
                    RecentOperations = GetRecentOperations(20),

                    // Memory trends
                    MemoryTrend = CalculateMemoryTrend(),

                    // General info
                    SnapshotCount = _snapshots.Count,
                    TrackingDuration = DateTime.Now - (_snapshots.FirstOrDefault()?.Timestamp ?? DateTime.Now)
                };
            }
        }

        /// <summary>
        /// Detect services that might be leaking memory
        /// </summary>
        private static List<LeakSuspectInfo> DetectPotentialLeaks()
        {
            var suspects = new List<LeakSuspectInfo>();

            foreach (var service in _serviceStats.Values)
            {
                // Look for services with consistently growing memory usage
                if (service.TotalMemoryDeltaBytes > 1024 * 1024 && // > 1MB total delta
                    service.OperationCount > 10) // With reasonable operation count
                {
                    var avgMemoryPerOp = service.TotalMemoryDeltaBytes / (double)service.OperationCount;
                    if (avgMemoryPerOp > 10240) // > 10KB average per operation
                    {
                        suspects.Add(new LeakSuspectInfo
                        {
                            ServiceName = service.ServiceName,
                            TotalLeakEstimateKB = service.TotalMemoryDeltaBytes / 1024.0,
                            AverageLeakPerOperationKB = avgMemoryPerOp / 1024.0,
                            OperationCount = service.OperationCount,
                            Description = $"Averaging {avgMemoryPerOp / 1024.0:F1}KB per operation"
                        });
                    }
                }
            }

            return suspects.OrderByDescending(s => s.TotalLeakEstimateKB).ToList();
        }

        /// <summary>
        /// Get recent operations across all services
        /// </summary>
        private static List<OperationMemoryInfo> GetRecentOperations(int count)
        {
            return _serviceStats.Values
                .SelectMany(s => s.Operations)
                .OrderByDescending(o => o.Timestamp)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Calculate memory usage trend
        /// </summary>
        private static MemoryTrendInfo CalculateMemoryTrend()
        {
            if (_snapshots.Count < 2)
            {
                return new MemoryTrendInfo { Trend = "Insufficient data" };
            }

            var recent = _snapshots.TakeLast(10).ToList();
            var first = recent.First();
            var last = recent.Last();

            var memoryChange = last.TotalMemoryBytes - first.TotalMemoryBytes;
            var timeSpan = last.Timestamp - first.Timestamp;
            var changeRateMBPerMin = (memoryChange / 1024.0 / 1024.0) / timeSpan.TotalMinutes;

            return new MemoryTrendInfo
            {
                Trend = changeRateMBPerMin > 1 ? "Growing" :
                        changeRateMBPerMin < -1 ? "Shrinking" : "Stable",
                ChangeRateMBPerMinute = changeRateMBPerMin,
                DataPoints = recent.Count
            };
        }

        /// <summary>
        /// Clear all enhanced tracking data
        /// </summary>
        public static void ClearTrackingData()
        {
            lock (_enhancedLock)
            {
                _serviceStats.Clear();
                _snapshots.Clear();
                _lastSnapshotTime = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Get detailed statistics for a specific service
        /// </summary>
        public static ServiceMemoryInfo GetServiceDetails(string serviceName)
        {
            lock (_enhancedLock)
            {
                return _serviceStats.TryGetValue(serviceName, out var info) ? info : null;
            }
        }
    }

    /// <summary>
    /// Comprehensive memory dashboard statistics
    /// </summary>
    public class MemoryDashboardStats
    {
        public long TotalMemoryMB { get; set; }
        public long DeltaFromBaselineMB { get; set; }
        public long MemoryPerTabKB { get; set; }
        public int ActiveTabCount { get; set; }
        public List<ServiceMemoryInfo> ServiceBreakdown { get; set; } = new();
        public List<LeakSuspectInfo> PotentialLeaks { get; set; } = new();
        public List<OperationMemoryInfo> RecentOperations { get; set; } = new();
        public MemoryTrendInfo MemoryTrend { get; set; } = new();
        public int SnapshotCount { get; set; }
        public TimeSpan TrackingDuration { get; set; }
    }

    /// <summary>
    /// Information about a service's memory usage
    /// </summary>
    public class ServiceMemoryInfo
    {
        public string ServiceName { get; set; }
        public long TotalMemoryDeltaBytes { get; set; }
        public int OperationCount { get; set; }
        public DateTime LastOperationTime { get; set; }
        public List<OperationMemoryInfo> Operations { get; set; } = new();

        public double TotalMemoryDeltaKB => TotalMemoryDeltaBytes / 1024.0;
        public double TotalMemoryDeltaMB => TotalMemoryDeltaBytes / 1024.0 / 1024.0;
        public double AverageMemoryPerOperationKB => OperationCount > 0 ? TotalMemoryDeltaKB / OperationCount : 0;
    }

    /// <summary>
    /// Information about a specific operation's memory usage
    /// </summary>
    public class OperationMemoryInfo
    {
        public string OperationName { get; set; }
        public long MemoryDeltaBytes { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime Timestamp { get; set; }

        public double MemoryDeltaKB => MemoryDeltaBytes / 1024.0;
    }

    /// <summary>
    /// Information about potential memory leaks
    /// </summary>
    public class LeakSuspectInfo
    {
        public string ServiceName { get; set; }
        public double TotalLeakEstimateKB { get; set; }
        public double AverageLeakPerOperationKB { get; set; }
        public int OperationCount { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Memory trend information
    /// </summary>
    public class MemoryTrendInfo
    {
        public string Trend { get; set; }
        public double ChangeRateMBPerMinute { get; set; }
        public int DataPoints { get; set; }
    }

    /// <summary>
    /// Memory snapshot at a point in time
    /// </summary>
    public class MemorySnapshot
    {
        public DateTime Timestamp { get; set; }
        public long TotalMemoryBytes { get; set; }
        public List<ServiceMemoryInfo> ServiceStats { get; set; } = new();
    }
#endif
}
