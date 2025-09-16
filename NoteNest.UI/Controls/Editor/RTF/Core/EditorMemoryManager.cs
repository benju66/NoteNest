using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Controls;
using NoteNest.Core.Models;

namespace NoteNest.UI.Controls.Editor.RTF.Core
{
    /// <summary>
    /// Centralized memory management for all editor types
    /// Single Responsibility: Editor memory optimization and resource cleanup
    /// Follows SRP principles for maintainable, testable memory management
    /// </summary>
    public class EditorMemoryManager : IDisposable
    {
        private readonly EditorSettings _settings;
        private readonly Dictionary<WeakReference, IDisposable> _managedResources;
        private readonly Timer _cleanupTimer;
        private readonly object _lockObject = new object();
        private bool _disposed = false;
        
        public EditorMemoryManager(EditorSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _managedResources = new Dictionary<WeakReference, IDisposable>();
            
            // Configure cleanup interval based on settings
            var interval = _settings.EnableAggressiveMemoryManagement 
                ? Math.Max(_settings.MemoryCleanupIntervalMs / 4, 30000) // More aggressive: every 30s minimum
                : _settings.MemoryCleanupIntervalMs; // Standard: 2 minutes default
                
            _cleanupTimer = new Timer(PerformCleanup, null, interval, interval);
            
            System.Diagnostics.Debug.WriteLine($"[EditorMemoryManager] Initialized with cleanup interval: {interval}ms");
        }
        
        /// <summary>
        /// Configure memory settings for a RichTextBox editor
        /// </summary>
        public void ConfigureEditor(RichTextBox editor)
        {
            if (editor == null || _disposed) return;
            
            try
            {
                // Apply undo stack limit to prevent unlimited memory growth
                editor.UndoLimit = _settings.UndoStackLimit;
                
                // Register editor for cleanup tracking (weak reference to avoid keeping it alive)
                RegisterManagedResource(new WeakReference(editor), null);
                
                System.Diagnostics.Debug.WriteLine($"[EditorMemoryManager] Configured editor with UndoLimit: {_settings.UndoStackLimit}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditorMemoryManager] Editor configuration failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Create and track a managed TextRange or other disposable resource
        /// Ensures proper cleanup when the resource is no longer needed
        /// </summary>
        public T CreateManagedResource<T>(Func<T> factory) where T : IDisposable
        {
            if (factory == null || _disposed) 
                return default(T);
                
            try
            {
                var resource = factory();
                if (resource != null)
                {
                    RegisterManagedResource(new WeakReference(resource), resource);
                }
                return resource;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditorMemoryManager] Resource creation failed: {ex.Message}");
                return default(T);
            }
        }
        
        /// <summary>
        /// Register a resource for automatic cleanup tracking
        /// </summary>
        private void RegisterManagedResource(WeakReference weakRef, IDisposable disposable)
        {
            if (weakRef == null || _disposed) return;
            
            lock (_lockObject)
            {
                try
                {
                    _managedResources[weakRef] = disposable;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[EditorMemoryManager] Resource registration failed: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Periodic cleanup of dead weak references and their associated resources
        /// </summary>
        private void PerformCleanup(object state)
        {
            if (_disposed) return;
            
            try
            {
                lock (_lockObject)
                {
                    var toRemove = new List<WeakReference>();
                    var disposedCount = 0;
                    
                    foreach (var kvp in _managedResources)
                    {
                        try
                        {
                            // If the weak reference target is no longer alive, clean it up
                            if (!kvp.Key.IsAlive)
                            {
                                kvp.Value?.Dispose();
                                toRemove.Add(kvp.Key);
                                disposedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[EditorMemoryManager] Resource disposal failed: {ex.Message}");
                            toRemove.Add(kvp.Key); // Remove problematic entries
                        }
                    }
                    
                    // Remove cleaned up resources from tracking
                    foreach (var key in toRemove)
                    {
                        _managedResources.Remove(key);
                    }
                    
                    // Log cleanup activity for monitoring
                    if (disposedCount > 0 || toRemove.Count > _managedResources.Count / 4)
                    {
                        System.Diagnostics.Debug.WriteLine($"[EditorMemoryManager] Cleanup completed: {disposedCount} resources disposed, {_managedResources.Count} active");
                    }
                    
                    // If aggressive memory management is enabled, suggest GC for dead objects
                    if (_settings.EnableAggressiveMemoryManagement && disposedCount > 10)
                    {
                        // Only suggest GC collection, don't force it (respects performance guidelines)
                        GC.Collect(0, GCCollectionMode.Optimized);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditorMemoryManager] Cleanup operation failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Force an immediate cleanup cycle
        /// Useful for explicit memory pressure scenarios
        /// </summary>
        public void ForceCleanup()
        {
            if (!_disposed)
            {
                PerformCleanup(null);
            }
        }
        
        /// <summary>
        /// Get current memory management statistics
        /// </summary>
        public MemoryManagerStats GetStats()
        {
            if (_disposed) return new MemoryManagerStats();
            
            lock (_lockObject)
            {
                var activeCount = 0;
                var deadCount = 0;
                
                foreach (var kvp in _managedResources)
                {
                    if (kvp.Key.IsAlive)
                        activeCount++;
                    else
                        deadCount++;
                }
                
                return new MemoryManagerStats
                {
                    ActiveResources = activeCount,
                    DeadReferences = deadCount,
                    TotalTracked = _managedResources.Count,
                    AggressiveMode = _settings.EnableAggressiveMemoryManagement,
                    CleanupIntervalMs = _settings.MemoryCleanupIntervalMs
                };
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            try
            {
                _disposed = true;
                
                // Stop the cleanup timer
                _cleanupTimer?.Dispose();
                
                // Clean up all tracked resources
                lock (_lockObject)
                {
                    var disposedCount = 0;
                    foreach (var resource in _managedResources.Values)
                    {
                        try
                        {
                            resource?.Dispose();
                            disposedCount++;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[EditorMemoryManager] Resource disposal during cleanup failed: {ex.Message}");
                        }
                    }
                    _managedResources.Clear();
                    
                    System.Diagnostics.Debug.WriteLine($"[EditorMemoryManager] Disposed with {disposedCount} resources cleaned up");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditorMemoryManager] Disposal failed: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Statistics for memory manager monitoring
    /// </summary>
    public class MemoryManagerStats
    {
        public int ActiveResources { get; set; }
        public int DeadReferences { get; set; }
        public int TotalTracked { get; set; }
        public bool AggressiveMode { get; set; }
        public int CleanupIntervalMs { get; set; }
    }
}
