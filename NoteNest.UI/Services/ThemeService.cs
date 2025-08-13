using System;
using System.Threading.Tasks;
using System.Windows.Media;
using ModernWpf;
using NoteNest.Core.Services;

namespace NoteNest.UI.Services
{
    public enum AppTheme
    {
        Light,
        Dark,
        System
    }

    public static class ThemeService
    {
        private static ConfigurationService _configurationService;

        public static void Initialize()
        {
            _configurationService = new ConfigurationService();

            // Apply quickly using current in-memory settings (defaults on first run)
            ApplyTheme(GetSavedTheme());

            // Load settings in background, then re-apply theme from loaded config on UI context
            _ = _configurationService
                .LoadSettingsAsync()
                .ContinueWith(t =>
                {
                    try { ApplyTheme(GetSavedTheme()); } catch { /* ignore */ }
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        public static AppTheme GetSavedTheme()
        {
            var themeString = _configurationService != null ? _configurationService.Settings?.Theme : null;
            if (!string.IsNullOrWhiteSpace(themeString) && Enum.TryParse<AppTheme>(themeString, true, out var theme))
            {
                return theme;
            }
            return AppTheme.System;
        }

        public static void SetTheme(AppTheme theme)
        {
            ApplyTheme(theme);
            SaveTheme(theme);
        }

        private static void ApplyTheme(AppTheme theme)
        {
            var themeManager = ThemeManager.Current;

            switch (theme)
            {
                case AppTheme.Light:
                    themeManager.ApplicationTheme = ApplicationTheme.Light;
                    break;
                case AppTheme.Dark:
                    themeManager.ApplicationTheme = ApplicationTheme.Dark;
                    break;
                case AppTheme.System:
                    themeManager.ApplicationTheme = null; // Use system theme
                    break;
            }
        }

        private static void SaveTheme(AppTheme theme)
        {
            if (_configurationService == null) return;
            try
            {
                _configurationService.Settings.Theme = theme.ToString();
                _configurationService.SaveSettingsAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore save errors for theme change
            }
        }

        public static Color GetAccentColor()
        {
            return ThemeManager.Current.ActualAccentColor;
        }
    }
}


