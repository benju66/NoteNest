using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using NoteNest.UI.Interfaces;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// 🎨 LUCIDE ICON SERVICE - High-Performance SVG Icon Management
    /// Provides Lucide SVG icons with smart caching, lazy loading, and performance optimization
    /// </summary>
    public class LucideIconService : IIconService
    {
        private readonly IAppLogger _logger;
        private readonly ConcurrentDictionary<IconCacheKey, Geometry> _geometryCache;
        private long _hitCount = 0;
        private long _missCount = 0;
        private readonly object _statsLock = new object();

        // Lucide SVG path data - converted from official Lucide icons
        private static readonly Dictionary<(TreeIconType, TreeIconState), string> IconPaths = new()
        {
            // 📁 FOLDER ICONS
            [(TreeIconType.Folder, TreeIconState.Default)] = 
                "M4 20h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.93a2 2 0 0 1-1.66-.9L8.59 3.2A2 2 0 0 0 6.93 2H4a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2Z",
            
            [(TreeIconType.Folder, TreeIconState.Expanded)] = 
                "M2 7.5V2a2 2 0 0 1 2-2h6.5L14 4h6a2 2 0 0 1 2 2v6.5M4 15v-3a2 2 0 0 1 2-2h14l-2 2H8v1",
            
            // 📄 DOCUMENT ICONS  
            [(TreeIconType.Document, TreeIconState.Default)] =
                "M14,2 L14,8 L20,8 M14,2 L20,8 L20,20 A2,2 0 0,1 18,22 L6,22 A2,2 0 0,1 4,20 L4,4 A2,2 0 0,1 6,2 L14,2 Z M16,13 L8,13 M16,17 L8,17 M10,9 L8,9",
            
            // 📌 PIN ICONS (Future)
            [(TreeIconType.Pin, TreeIconState.Default)] =
                "m9 12 2-2 3 3-3 3-2-2M9 12l6-6 2 2-6 6M9 12l-6 6 2 2 6-6",
            
            [(TreeIconType.Pin, TreeIconState.Pinned)] =
                "m9 12 2-2 3 3-3 3-2-2M9 12l6-6 2 2-6 6M9 12l-6 6 2 2 6-6"
        };

        public LucideIconService(IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _geometryCache = new ConcurrentDictionary<IconCacheKey, Geometry>();
            
            _logger.Info("🎨 LucideIconService initialized with smart caching");
        }

        /// <summary>
        /// Gets icon geometry with smart caching and fallback handling
        /// </summary>
        public Geometry GetTreeIconGeometry(TreeIconType iconType, TreeIconState state = TreeIconState.Default)
        {
            var key = new IconCacheKey(iconType, state);
            
            try
            {
                var geometry = _geometryCache.GetOrAdd(key, LoadIconGeometry);
                
                // Track cache statistics
                if (_geometryCache.ContainsKey(key))
                {
                    Interlocked.Increment(ref _hitCount);
                }
                else
                {
                    Interlocked.Increment(ref _missCount);
                }
                
                return geometry;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to load icon geometry for {iconType}:{state}");
                return GetFallbackGeometry(iconType);
            }
        }

        /// <summary>
        /// Preloads icons for visible tree items
        /// </summary>
        public void PreloadVisibleIcons(IEnumerable<TreeIconType> visibleIcons)
        {
            try
            {
                var iconsToPreload = visibleIcons.Distinct().ToList();
                _logger.Info($"Preloading {iconsToPreload.Count} visible icon types");

                foreach (var iconType in iconsToPreload)
                {
                    // Preload both states for folders to avoid delays during expand/collapse
                    if (iconType == TreeIconType.Folder)
                    {
                        GetTreeIconGeometry(TreeIconType.Folder, TreeIconState.Default);
                        GetTreeIconGeometry(TreeIconType.Folder, TreeIconState.Expanded);
                    }
                    else
                    {
                        GetTreeIconGeometry(iconType, TreeIconState.Default);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to preload visible icons");
            }
        }

        /// <summary>
        /// Clears cache to free memory
        /// </summary>
        public void ClearCache()
        {
            lock (_statsLock)
            {
                var cacheSize = _geometryCache.Count;
                _geometryCache.Clear();
                _hitCount = 0;
                _missCount = 0;
                
                _logger.Info($"Icon cache cleared. Released {cacheSize} cached geometries");
            }
        }

        /// <summary>
        /// Checks if icon is cached
        /// </summary>
        public bool IsIconCached(TreeIconType iconType, TreeIconState state)
        {
            var key = new IconCacheKey(iconType, state);
            return _geometryCache.ContainsKey(key);
        }

        /// <summary>
        /// Gets cache performance statistics
        /// </summary>
        public IconCacheStats GetCacheStats()
        {
            lock (_statsLock)
            {
                var totalRequests = _hitCount + _missCount;
                var hitRatio = totalRequests > 0 ? (double)_hitCount / totalRequests : 0.0;
                
                // Rough memory estimate (each geometry ~1-2KB)
                var estimatedMemory = _geometryCache.Count * 1500L;
                
                return new IconCacheStats(
                    CacheSize: _geometryCache.Count,
                    HitCount: (int)_hitCount,
                    MissCount: (int)_missCount,
                    HitRatio: hitRatio,
                    MemoryUsageBytes: estimatedMemory
                );
            }
        }

        #region Private Implementation

        /// <summary>
        /// Loads geometry from SVG path data
        /// </summary>
        private Geometry LoadIconGeometry(IconCacheKey key)
        {
            try
            {
                if (IconPaths.TryGetValue((key.IconType, key.State), out var pathData))
                {
                    var geometry = Geometry.Parse(pathData);
                    geometry.Freeze(); // Freeze for performance and thread safety
                    
                    _logger.Debug($"Loaded icon geometry: {key.IconType}:{key.State}");
                    return geometry;
                }
                
                _logger.Warning($"No path data found for {key.IconType}:{key.State}, using fallback");
                return GetFallbackGeometry(key.IconType);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to parse geometry for {key.IconType}:{key.State}");
                return GetFallbackGeometry(key.IconType);
            }
        }

        /// <summary>
        /// Provides simple fallback geometries for error cases
        /// </summary>
        private Geometry GetFallbackGeometry(TreeIconType iconType)
        {
            return iconType switch
            {
                TreeIconType.Folder => Geometry.Parse("M2 4h4l2 2h12v12H2V4z"), // Simple folder rectangle
                TreeIconType.Document => Geometry.Parse("M4 2h12v20H4V2z M8 6h8 M8 10h8 M8 14h5"), // Simple document
                TreeIconType.Pin => Geometry.Parse("M12 2l-2 8h4l-2-8z M10 12h4v2h-4v-2z"), // Simple pin
                _ => Geometry.Parse("M2 2h20v20H2V2z") // Simple square
            };
        }

        #endregion

        #region Cache Key Implementation

        /// <summary>
        /// Cache key for icon geometry lookup
        /// </summary>
        private readonly record struct IconCacheKey(TreeIconType IconType, TreeIconState State);

        #endregion
    }
}
