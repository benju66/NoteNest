using System;
using System.Windows;
using System.Windows.Controls;
using NoteNest.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace NoteNest.UI.Windows
{
    /// <summary>
    /// Simplified Settings Window - migrated from ModernWPF
    /// Note: This is a temporary simplified version during the ModernWPF removal
    /// Will be rebuilt with full settings in future updates
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            try
            {
                // Load current theme setting
                var app = (App)System.Windows.Application.Current;
                var themeService = app.ServiceProvider?.GetService<IThemeService>();
                if (themeService != null)
                {
                    var currentTheme = themeService.CurrentTheme.ToString();
                    foreach (ComboBoxItem item in ThemeComboBox.Items)
                    {
                        if (item.Tag?.ToString() == currentTheme)
                        {
                            ThemeComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsWindow] Failed to load settings: {ex.Message}");
            }
        }

        private async void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;
            
            try
            {
                var selectedItem = e.AddedItems[0] as ComboBoxItem;
                var themeTag = selectedItem?.Tag?.ToString();
                
                if (!string.IsNullOrEmpty(themeTag) && Enum.TryParse<ThemeType>(themeTag, out var theme))
                {
                    var app = (App)System.Windows.Application.Current;
                    var themeService = app.ServiceProvider?.GetService<IThemeService>();
                    if (themeService != null)
                    {
                        await themeService.SetThemeAsync(theme);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsWindow] Theme change failed: {ex.Message}");
            }
        }

        // Legacy method for compatibility
        private void NavigationView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // No longer needed - simplified settings window
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}