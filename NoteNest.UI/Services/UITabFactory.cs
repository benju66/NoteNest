using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Diagnostics;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Services
{
    public class UITabFactory : ITabFactory, IDisposable
    {
        private readonly ISaveManager _saveManager;
        
        // HIGH-IMPACT MEMORY FIX: Simple tab caching with cleanup
        private readonly ConcurrentDictionary<string, WeakReference> _tabCache = new();
        private readonly int _maxTabs = 50;  // User constraint
        private DateTime _lastCleanup = DateTime.Now;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(2);
        
        public UITabFactory(ISaveManager saveManager)
        {
            _saveManager = saveManager;
            
            DebugLogger.Log("UITabFactory initialized with memory management");
        }
        
        public ITabItem CreateTab(NoteModel note, string noteId)
        {
            DebugLogger.Log($"CreateTab called: noteId={noteId}, note.Title={note.Title}");
            
            // Ensure note has the correct ID
            note.Id = noteId;
            
            // HIGH-IMPACT MEMORY FIX: Check cache for existing tab
            if (_tabCache.TryGetValue(noteId, out var weakRef) && weakRef.IsAlive)
            {
                if (weakRef.Target is NoteTabItem existingTab)
                {
                    DebugLogger.Log($"Reusing cached tab for {note.Title}");
                    #if DEBUG
                    EnhancedMemoryTracker.TrackServiceOperation<UITabFactory>("CreateTab-CacheHit", () => { });
                    #endif
                    return existingTab;
                }
            }
            
            // Periodic cleanup to prevent memory growth
            PerformCleanupIfNeeded();
            
            // Create new tab with memory tracking
            #if DEBUG
            ITabItem result = null;
            EnhancedMemoryTracker.TrackServiceOperation<UITabFactory>("CreateTab", () =>
            {
                var tabItem = new NoteTabItem(note, _saveManager);
                _tabCache[noteId] = new WeakReference(tabItem);
                result = tabItem;
            });
            
            DebugLogger.Log($"Created new tab for {note.Title}. Cache size: {_tabCache.Count}");
            return result;
            #else
            var tabItem = new NoteTabItem(note, _saveManager, _taskRunner);
            _tabCache[noteId] = new WeakReference(tabItem);
            DebugLogger.Log($"Created new tab for {note.Title}. Cache size: {_tabCache.Count}");
            return tabItem;
            #endif
        }
        
        // HIGH-IMPACT MEMORY FIX: Simple cleanup without warnings
        private void PerformCleanupIfNeeded()
        {
            #if DEBUG
            EnhancedMemoryTracker.TrackServiceOperation<UITabFactory>("CacheCleanup", () =>
            {
            #endif
                var now = DateTime.Now;
                if (now - _lastCleanup < _cleanupInterval)
                    return;
                
                _lastCleanup = now;
                
                var deadKeys = new List<string>();
                
                // Find dead references
                foreach (var kvp in _tabCache)
                {
                    if (!kvp.Value.IsAlive)
                    {
                        deadKeys.Add(kvp.Key);
                    }
                }
                
                // Clean up dead references
                foreach (var key in deadKeys)
                {
                    _tabCache.TryRemove(key, out _);
                }
                
                if (deadKeys.Count > 0)
                {
                    DebugLogger.Log($"Cleaned up {deadKeys.Count} dead tab references. Active: {_tabCache.Count}");
                    DebugLogger.LogMemory("After tab cleanup");
                }
                
                // If we're still over the reasonable limit, log for diagnostics
                if (_tabCache.Count > _maxTabs * 0.8)
                {
                    DebugLogger.Log($"High tab count: {_tabCache.Count} tabs cached (limit: {_maxTabs})");
                }
            #if DEBUG
            });
            #endif
        }
        
        public void RemoveTab(string noteId)
        {
            #if DEBUG
            EnhancedMemoryTracker.TrackServiceOperation<UITabFactory>("RemoveTab", () =>
            {
            #endif
                if (_tabCache.TryRemove(noteId, out var weakRef))
                {
                    if (weakRef.IsAlive && weakRef.Target is NoteTabItem tab)
                    {
                        tab.Dispose();
                        DebugLogger.Log($"Removed and disposed tab: {tab.Note.Title}");
                    }
                }
            #if DEBUG
            });
            #endif
        }
        
        public void Dispose()
        {
            DebugLogger.Log($"Disposing UITabFactory. Active tabs: {_tabCache.Count}");
            
            // Dispose all cached tabs
            foreach (var kvp in _tabCache.ToArray())
            {
                if (kvp.Value.IsAlive && kvp.Value.Target is NoteTabItem tab)
                {
                    try
                    {
                        tab.Dispose();
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"Tab disposal error during factory cleanup: {ex.Message}");
                    }
                }
            }
            
            _tabCache.Clear();
            DebugLogger.Log("UITabFactory disposed");
        }
    }
}
