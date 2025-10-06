using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// NEW: Custom theme service implementation
    /// Replaces ModernWPF-based theming with full Solarized support
    /// Clean Architecture - dependency injection ready
    /// </summary>
    public class ThemeService : IThemeService
    {
        private readonly ConfigurationService _configService;
        private readonly IAppLogger _logger;
        private ThemeType _currentTheme;
        
        public ThemeType CurrentTheme => _currentTheme;
        public event EventHandler<ThemeType> ThemeChanged;
        
        public ThemeService(ConfigurationService configService, IAppLogger logger)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Initialize theme system - loads saved theme preference
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                _logger.Info("[ThemeService] Initializing theme system...");
                
                // Load saved theme from settings
                await _configService.LoadSettingsAsync();
                var savedTheme = _configService.Settings?.Theme;
                
                if (!string.IsNullOrEmpty(savedTheme) && Enum.TryParse<ThemeType>(savedTheme, out var theme))
                {
                    _logger.Info($"[ThemeService] Restoring saved theme: {theme}");
                    await SetThemeAsync(theme);
                }
                else
                {
                    _logger.Info("[ThemeService] No saved theme found, using Light theme");
                    await SetThemeAsync(ThemeType.Light);
                }
                
                _logger.Info($"[ThemeService] Theme system initialized successfully - Active: {_currentTheme}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[ThemeService] Failed to initialize theme system");
                // Fallback to Light theme
                await SetThemeAsync(ThemeType.Light);
            }
        }
        
        /// <summary>
        /// Change the active theme dynamically
        /// </summary>
        public Task<bool> SetThemeAsync(ThemeType theme)
        {
            try
            {
                _logger.Info($"[ThemeService] Switching theme to: {theme}");
                
                _currentTheme = theme;
                
                var app = System.Windows.Application.Current;
                if (app == null)
                {
                    _logger.Warning("[ThemeService] Application.Current is null, cannot apply theme");
                    return Task.FromResult(false);
                }
                
                // Remove existing theme dictionaries
                var themeDicts = app.Resources.MergedDictionaries
                    .Where(d => d.Source?.OriginalString?.Contains("/Themes/") == true)
                    .ToList();
                    
                foreach (var dict in themeDicts)
                {
                    app.Resources.MergedDictionaries.Remove(dict);
                }
                
                // Determine theme URI
                var themeUri = GetThemeUri(theme);
                
                // Load new theme
                var themeDict = new ResourceDictionary { Source = themeUri };
                app.Resources.MergedDictionaries.Add(themeDict);
                
                _logger.Info($"[ThemeService] Theme loaded: {themeUri.OriginalString}");
                
                // Save to settings
                _configService.Settings.Theme = theme.ToString();
                _configService.RequestSaveDebounced();
                
                // Raise event
                ThemeChanged?.Invoke(this, theme);
                
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[ThemeService] Failed to set theme: {theme}");
                return Task.FromResult(false);
            }
        }
        
        private Uri GetThemeUri(ThemeType theme)
        {
            // Handle System theme by detecting OS preference
            if (theme == ThemeType.System)
            {
                // TODO: Detect Windows theme preference
                // For now, default to Light
                theme = ThemeType.Light;
                _logger.Info("[ThemeService] System theme detected, using Light (OS detection TODO)");
            }
            
            return theme switch
            {
                ThemeType.Light => new Uri("pack://application:,,,/Resources/Themes/LightTheme.xaml"),
                ThemeType.Dark => new Uri("pack://application:,,,/Resources/Themes/DarkTheme.xaml"),
                ThemeType.SolarizedLight => new Uri("pack://application:,,,/Resources/Themes/SolarizedLightTheme.xaml"),
                ThemeType.SolarizedDark => new Uri("pack://application:,,,/Resources/Themes/SolarizedDarkTheme.xaml"),
                _ => new Uri("pack://application:,,,/Resources/Themes/LightTheme.xaml")
            };
        }
    }
}


