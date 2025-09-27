using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Infrastructure.Database
{
    /// <summary>
    /// Performance monitoring service for database operations.
    /// Tracks execution times, memory usage, and provides diagnostics.
    /// </summary>
    public interface ITreePerformanceMonitor
    {
        Task<PerformanceMetrics> MeasureOperationAsync(string operation, Func<Task> action);
        Task<PerformanceMetrics> MeasureOperationAsync<T>(string operation, Func<Task<T>> action);
        Task<List<PerformanceMetrics>> GetRecentMetricsAsync(int count = 100);
        Task<PerformanceReport> GenerateReportAsync();
    }
    
    public class TreePerformanceMonitor : ITreePerformanceMonitor
    {
        private readonly IAppLogger _logger;
        private readonly List<PerformanceMetrics> _recentMetrics = new();
        private readonly object _metricsLock = new();
        
        public TreePerformanceMonitor(IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task<PerformanceMetrics> MeasureOperationAsync(string operation, Func<Task> action)
        {
            var stopwatch = Stopwatch.StartNew();
            var startMemory = GC.GetTotalMemory(false);
            Exception exception = null;
            
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                exception = ex;
                throw;
            }
            finally
            {
                stopwatch.Stop();
                var endMemory = GC.GetTotalMemory(false);
                
                var metrics = new PerformanceMetrics
                {
                    Operation = operation,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    MemoryUsedBytes = endMemory - startMemory,
                    Timestamp = DateTime.UtcNow,
                    Success = exception == null,
                    ErrorMessage = exception?.Message
                };
                
                RecordMetrics(metrics);
            }
            
            return _recentMetrics.LastOrDefault();
        }
        
        public async Task<PerformanceMetrics> MeasureOperationAsync<T>(string operation, Func<Task<T>> action)
        {
            var stopwatch = Stopwatch.StartNew();
            var startMemory = GC.GetTotalMemory(false);
            Exception exception = null;
            T result = default(T);
            
            try
            {
                result = await action();
            }
            catch (Exception ex)
            {
                exception = ex;
                throw;
            }
            finally
            {
                stopwatch.Stop();
                var endMemory = GC.GetTotalMemory(false);
                
                var metrics = new PerformanceMetrics
                {
                    Operation = operation,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    MemoryUsedBytes = endMemory - startMemory,
                    Timestamp = DateTime.UtcNow,
                    Success = exception == null,
                    ErrorMessage = exception?.Message,
                    ResultType = typeof(T).Name,
                    HasResult = result != null
                };
                
                RecordMetrics(metrics);
            }
            
            return _recentMetrics.LastOrDefault();
        }
        
        public async Task<List<PerformanceMetrics>> GetRecentMetricsAsync(int count = 100)
        {
            await Task.CompletedTask; // Make async for consistency
            
            lock (_metricsLock)
            {
                return _recentMetrics.TakeLast(count).ToList();
            }
        }
        
        public async Task<PerformanceReport> GenerateReportAsync()
        {
            await Task.CompletedTask; // Make async for consistency
            
            lock (_metricsLock)
            {
                if (!_recentMetrics.Any())
                {
                    return new PerformanceReport
                    {
                        GeneratedAt = DateTime.UtcNow,
                        Message = "No performance data available"
                    };
                }
                
                var report = new PerformanceReport
                {
                    GeneratedAt = DateTime.UtcNow,
                    TotalOperations = _recentMetrics.Count,
                    SuccessfulOperations = _recentMetrics.Count(m => m.Success),
                    FailedOperations = _recentMetrics.Count(m => !m.Success),
                    AverageOperationTimeMs = _recentMetrics.Average(m => m.DurationMs),
                    TotalMemoryUsedBytes = _recentMetrics.Sum(m => m.MemoryUsedBytes),
                    FastestOperationMs = _recentMetrics.Min(m => m.DurationMs),
                    SlowestOperationMs = _recentMetrics.Max(m => m.DurationMs)
                };
                
                // Operation breakdown
                report.OperationBreakdown = _recentMetrics
                    .GroupBy(m => m.Operation)
                    .Select(g => new OperationSummary
                    {
                        Operation = g.Key,
                        Count = g.Count(),
                        AverageTimeMs = g.Average(m => m.DurationMs),
                        TotalTimeMs = g.Sum(m => m.DurationMs),
                        SuccessRate = (double)g.Count(m => m.Success) / g.Count() * 100
                    })
                    .OrderByDescending(s => s.TotalTimeMs)
                    .ToList();
                
                return report;
            }
        }
        
        private void RecordMetrics(PerformanceMetrics metrics)
        {
            lock (_metricsLock)
            {
                _recentMetrics.Add(metrics);
                
                // Keep only last 1000 metrics to prevent memory growth
                if (_recentMetrics.Count > 1000)
                {
                    _recentMetrics.RemoveAt(0);
                }
            }
            
            // Log slow operations
            if (metrics.DurationMs > 1000) // Slower than 1 second
            {
                _logger.Warning($"Slow operation detected: {metrics.Operation} took {metrics.DurationMs}ms");
            }
            else if (metrics.DurationMs > 100) // Slower than 100ms
            {
                _logger.Debug($"Operation timing: {metrics.Operation} took {metrics.DurationMs}ms");
            }
        }
    }
    
    // =============================================================================
    // PERFORMANCE TRACKING TYPES
    // =============================================================================
    
    public class PerformanceMetrics
    {
        public string Operation { get; set; }
        public long DurationMs { get; set; }
        public long MemoryUsedBytes { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string ResultType { get; set; }
        public bool HasResult { get; set; }
        
        public string MemoryDisplay => MemoryUsedBytes > 1024 * 1024 
            ? $"{MemoryUsedBytes / (1024.0 * 1024.0):F1} MB"
            : $"{MemoryUsedBytes / 1024.0:F1} KB";
    }
    
    public class PerformanceReport
    {
        public DateTime GeneratedAt { get; set; }
        public string Message { get; set; }
        
        // Summary statistics
        public int TotalOperations { get; set; }
        public int SuccessfulOperations { get; set; }
        public int FailedOperations { get; set; }
        public double SuccessRate => TotalOperations > 0 ? (double)SuccessfulOperations / TotalOperations * 100 : 0;
        
        // Timing statistics
        public double AverageOperationTimeMs { get; set; }
        public long FastestOperationMs { get; set; }
        public long SlowestOperationMs { get; set; }
        
        // Memory statistics
        public long TotalMemoryUsedBytes { get; set; }
        public string TotalMemoryDisplay => TotalMemoryUsedBytes > 1024 * 1024 
            ? $"{TotalMemoryUsedBytes / (1024.0 * 1024.0):F1} MB"
            : $"{TotalMemoryUsedBytes / 1024.0:F1} KB";
        
        // Operation breakdown
        public List<OperationSummary> OperationBreakdown { get; set; } = new();
    }
    
    public class OperationSummary
    {
        public string Operation { get; set; }
        public int Count { get; set; }
        public double AverageTimeMs { get; set; }
        public double TotalTimeMs { get; set; }
        public double SuccessRate { get; set; }
    }
}
