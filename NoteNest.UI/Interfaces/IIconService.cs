using System.Collections.Generic;
using System.Windows.Media;

namespace NoteNest.UI.Interfaces
{
    /// <summary>
    /// 🎨 LUCIDE ICON SERVICE - UI Layer Interface
    /// Provides SVG-based vector icons with smart caching and performance optimization
    /// Correctly placed in UI layer since it depends on WPF Geometry types
    /// </summary>
    public interface IIconService
    {
        /// <summary>
        /// Gets icon geometry for tree view items with state-based switching
        /// </summary>
        /// <param name="iconType">Type of icon (Folder, Document, etc.)</param>
        /// <param name="state">Current state (Default, Expanded, Pinned)</param>
        /// <returns>WPF Geometry for rendering in Path elements</returns>
        Geometry GetTreeIconGeometry(TreeIconType iconType, TreeIconState state = TreeIconState.Default);

        /// <summary>
        /// Preloads icons for visible tree items to improve performance
        /// </summary>
        /// <param name="visibleIcons">Icon types currently visible in tree</param>
        void PreloadVisibleIcons(IEnumerable<TreeIconType> visibleIcons);

        /// <summary>
        /// Clears icon cache to free memory (useful for large trees)
        /// </summary>
        void ClearCache();

        /// <summary>
        /// Checks if specific icon state is cached
        /// </summary>
        /// <param name="iconType">Icon type to check</param>
        /// <param name="state">Icon state to check</param>
        /// <returns>True if cached and ready for immediate use</returns>
        bool IsIconCached(TreeIconType iconType, TreeIconState state);

        /// <summary>
        /// Gets cache statistics for performance monitoring
        /// </summary>
        /// <returns>Cache hit rate, size, and performance metrics</returns>
        IconCacheStats GetCacheStats();
    }

    /// <summary>
    /// Tree view icon types supported by the service
    /// </summary>
    public enum TreeIconType
    {
        /// <summary>📁 Folder/Category icon</summary>
        Folder,
        
        /// <summary>📄 Document/Note icon</summary>
        Document,
        
        /// <summary>📌 Pin icon for pinned items</summary>
        Pin
    }

    /// <summary>
    /// Icon state variations for different contexts
    /// </summary>
    public enum TreeIconState
    {
        /// <summary>Default state (folder=closed, document=normal)</summary>
        Default,
        
        /// <summary>Expanded state (folder=open)</summary>
        Expanded,
        
        /// <summary>Pinned state (any item that's pinned)</summary>
        Pinned
    }

    /// <summary>
    /// Icon cache performance statistics
    /// </summary>
    public record IconCacheStats(
        int CacheSize,
        int HitCount,
        int MissCount,
        double HitRatio,
        long MemoryUsageBytes
    );
}
