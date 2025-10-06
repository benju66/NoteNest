using System;
using System.Threading.Tasks;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Theme types supported by NoteNest
    /// </summary>
    public enum ThemeType
    {
        Light,
        Dark,
        SolarizedLight,
        SolarizedDark,
        System // Follows OS theme preference
    }
    
    /// <summary>
    /// Service for managing application themes
    /// Clean Architecture - Single Responsibility Principle
    /// </summary>
    public interface IThemeService
    {
        /// <summary>
        /// Event fired when theme changes
        /// Subscribe to this to react to theme changes in real-time
        /// </summary>
        event EventHandler<ThemeType> ThemeChanged;
        
        /// <summary>
        /// Currently active theme
        /// </summary>
        ThemeType CurrentTheme { get; }
        
        /// <summary>
        /// Initialize theme system and load saved theme preference
        /// Call this during app startup
        /// </summary>
        Task InitializeAsync();
        
        /// <summary>
        /// Change the active theme
        /// </summary>
        /// <param name="theme">Theme to switch to</param>
        /// <returns>True if theme was applied successfully</returns>
        Task<bool> SetThemeAsync(ThemeType theme);
    }
}
