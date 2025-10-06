using System;
using System.Windows;
using System.Windows.Controls;
using NoteNest.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace NoteNest.UI.Windows
{
    /// <summary>
    /// Simplified Settings Window
    /// Note: Theme setting moved to More menu in title bar
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
                // Load any saved settings here
                // Theme is now controlled from the More menu in title bar
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsWindow] Failed to load settings: {ex.Message}");
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