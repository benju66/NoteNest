#if DEBUG
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NoteNest.Core.Diagnostics;
using Microsoft.Win32;
using System.Text;

namespace NoteNest.UI.Windows
{
    /// <summary>
    /// Memory Dashboard Window for debugging memory usage
    /// Only available in DEBUG builds
    /// </summary>
    public partial class MemoryDashboardWindow : Window, INotifyPropertyChanged
    {
        private readonly DispatcherTimer _refreshTimer;
        private MemoryDashboardStats _currentStats;
        private bool _disposed = false;
        
        // Filtering state
        private string _serviceFilter = "All Services";
        private double _memoryThresholdKB = 0;

        public MemoryDashboardWindow()
        {
            InitializeComponent();
            
            // Apply modern theme resources
            this.Resources.MergedDictionaries.Add(new ModernWpf.ThemeResources());
            this.Resources.MergedDictionaries.Add(new ModernWpf.Controls.XamlControlsResources());
            
            // Match main window theme
            try
            {
                var mainWindow = System.Windows.Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    var currentTheme = ModernWpf.ThemeManager.GetRequestedTheme(mainWindow);
                    ModernWpf.ThemeManager.SetRequestedTheme(this, currentTheme);
                }
            }
            catch { }

            // Set up auto-refresh timer
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            
            // Initial data load
            RefreshData();
            
            // Start auto-refresh
            _refreshTimer.Start();
            StatusText.Text = "Live Tracking";
            
            // Handle window closing to cleanup timer
            this.Closing += MemoryDashboardWindow_Closing;
        }

        private void MemoryDashboardWindow_Closing(object sender, CancelEventArgs e)
        {
            _refreshTimer?.Stop();
            _disposed = true;
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (!_disposed)
            {
                RefreshData();
            }
        }

        private void RefreshData()
        {
            try
            {
                _currentStats = EnhancedMemoryTracker.GetDashboardStats();
                UpdateUI();
                
                FooterText.Text = $"Last updated: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void UpdateUI()
        {
            if (_currentStats == null) return;

            // Update stats cards
            TotalMemoryText.Text = $"{_currentStats.TotalMemoryMB} MB";
            
            DeltaMemoryText.Text = _currentStats.DeltaFromBaselineMB >= 0 
                ? $"+{_currentStats.DeltaFromBaselineMB} MB"
                : $"{_currentStats.DeltaFromBaselineMB} MB";
            
            // Color code the delta
            DeltaMemoryText.Foreground = _currentStats.DeltaFromBaselineMB > 50 
                ? System.Windows.Media.Brushes.Red
                : _currentStats.DeltaFromBaselineMB > 20 
                    ? System.Windows.Media.Brushes.Orange
                    : System.Windows.Media.Brushes.Green;

            MemoryPerTabText.Text = _currentStats.ActiveTabCount > 0 
                ? $"{_currentStats.MemoryPerTabKB:F1} KB"
                : "0 KB";

            LeakCountText.Text = _currentStats.PotentialLeaks?.Count.ToString() ?? "0";
            
            // Color code leak count
            LeakCountText.Foreground = _currentStats.PotentialLeaks?.Count > 0 
                ? System.Windows.Media.Brushes.Red
                : System.Windows.Media.Brushes.Green;

            // Update service breakdown with filtering
            var filteredServices = _currentStats.ServiceBreakdown?
                .Where(s => Math.Abs(s.TotalMemoryDeltaKB) >= _memoryThresholdKB);
                
            if (_serviceFilter != "All Services")
            {
                filteredServices = filteredServices?.Where(s => s.ServiceName.Contains(_serviceFilter));
            }
                
            ServiceBreakdownGrid.ItemsSource = filteredServices?.ToList();

            // Update leak suspects
            if (_currentStats.PotentialLeaks?.Any() == true)
            {
                LeakSuspectsGrid.ItemsSource = _currentStats.PotentialLeaks;
                LeakSuspectsGrid.Visibility = Visibility.Visible;
                NoLeaksText.Visibility = Visibility.Collapsed;
            }
            else
            {
                LeakSuspectsGrid.Visibility = Visibility.Collapsed;
                NoLeaksText.Visibility = Visibility.Visible;
            }

            // Update recent operations
            RecentOperationsGrid.ItemsSource = _currentStats.RecentOperations?
                .Where(op => Math.Abs(op.MemoryDeltaKB) > 0.5) // Only show ops with > 0.5KB delta
                .ToList();

            // Update trend information
            if (_currentStats.MemoryTrend != null)
            {
                TrendStatusText.Text = _currentStats.MemoryTrend.Trend;
                TrendStatusText.Foreground = _currentStats.MemoryTrend.Trend switch
                {
                    "Growing" => System.Windows.Media.Brushes.Red,
                    "Shrinking" => System.Windows.Media.Brushes.Blue,
                    _ => System.Windows.Media.Brushes.Green
                };

                var changeText = Math.Abs(_currentStats.MemoryTrend.ChangeRateMBPerMinute) < 0.1 
                    ? "< 0.1 MB/min" 
                    : $"{_currentStats.MemoryTrend.ChangeRateMBPerMinute:+0.0;-0.0} MB/min";
                    
                TrendDetailsText.Text = $"Change: {changeText} • Data points: {_currentStats.MemoryTrend.DataPoints}";
            }

            // Update tracking info
            TrackingDurationText.Text = $"Duration: {FormatTimeSpan(_currentStats.TrackingDuration)}";
            SnapshotCountText.Text = $"Snapshots: {_currentStats.SnapshotCount}";
        }

        private string FormatTimeSpan(TimeSpan duration)
        {
            if (duration.TotalSeconds < 60)
                return $"{duration.TotalSeconds:F0}s";
            if (duration.TotalMinutes < 60)
                return $"{duration.TotalMinutes:F0}m {duration.Seconds}s";
            return $"{duration.TotalHours:F0}h {duration.Minutes}m";
        }

        #region Event Handlers

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshData();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = System.Windows.MessageBox.Show(
                    "Are you sure you want to clear all memory tracking data?\n\nThis will reset all statistics and cannot be undone.",
                    "Clear Memory Data",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    EnhancedMemoryTracker.ClearTrackingData();
                    RefreshData();
                    StatusText.Text = "Data Cleared";
                    
                    // Reset status after a delay
                    Task.Delay(2000).ContinueWith(_ => 
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (!_disposed)
                                StatusText.Text = "Live Tracking";
                        }));
                    });
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error clearing data: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "Text Files (*.txt)|*.txt|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                    FileName = $"memory-report-{DateTime.Now:yyyy-MM-dd-HHmm}.txt",
                    DefaultExt = "txt"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    ExportReport(saveDialog.FileName);
                    System.Windows.MessageBox.Show($"Memory report exported to:\n{saveDialog.FileName}", 
                        "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error exporting report: {ex.Message}", "Export Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ServiceFilter_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ServiceFilterCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            {
                _serviceFilter = item.Content?.ToString() ?? "All Services";
                UpdateUI(); // Refresh with new filter
            }
        }

        private void MemoryThreshold_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (MemoryThresholdCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            {
                var thresholdText = item.Content?.ToString() ?? "0 KB";
                _memoryThresholdKB = thresholdText switch
                {
                    "1 KB" => 1.0,
                    "10 KB" => 10.0,
                    "100 KB" => 100.0,
                    "1 MB" => 1024.0,
                    _ => 0.0
                };
                UpdateUI(); // Refresh with new threshold
            }
        }

        private void ResetFilters_Click(object sender, RoutedEventArgs e)
        {
            ServiceFilterCombo.SelectedIndex = 0; // "All Services"
            MemoryThresholdCombo.SelectedIndex = 0; // "0 KB"
            _serviceFilter = "All Services";
            _memoryThresholdKB = 0;
            UpdateUI();
        }

        #endregion

        #region Export Functionality

        private void ExportReport(string filePath)
        {
            var report = new StringBuilder();
            
            report.AppendLine("=== NOTENEST MEMORY DASHBOARD REPORT ===");
            report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"Tracking Duration: {FormatTimeSpan(_currentStats.TrackingDuration)}");
            report.AppendLine();

            // Overall statistics
            report.AppendLine("=== OVERALL STATISTICS ===");
            report.AppendLine($"Total Memory: {_currentStats.TotalMemoryMB} MB");
            report.AppendLine($"Delta from Baseline: {_currentStats.DeltaFromBaselineMB:+0;-0} MB");
            report.AppendLine($"Active Tabs: {_currentStats.ActiveTabCount}");
            report.AppendLine($"Memory per Tab: {_currentStats.MemoryPerTabKB:F1} KB");
            report.AppendLine($"Potential Leaks: {_currentStats.PotentialLeaks?.Count ?? 0}");
            report.AppendLine();

            // Memory trend
            report.AppendLine("=== MEMORY TREND ===");
            report.AppendLine($"Trend: {_currentStats.MemoryTrend?.Trend ?? "Unknown"}");
            report.AppendLine($"Change Rate: {_currentStats.MemoryTrend?.ChangeRateMBPerMinute:+0.00;-0.00} MB/min");
            report.AppendLine($"Data Points: {_currentStats.MemoryTrend?.DataPoints ?? 0}");
            report.AppendLine();

            // Service breakdown
            report.AppendLine("=== SERVICE MEMORY BREAKDOWN ===");
            report.AppendLine("Service Name                 | Total (KB) | Avg/Op (KB) | Operations | Last Activity");
            report.AppendLine(new string('-', 90));
            
            if (_currentStats.ServiceBreakdown?.Any() == true)
            {
                foreach (var service in _currentStats.ServiceBreakdown)
                {
                    report.AppendLine($"{service.ServiceName,-28} | {service.TotalMemoryDeltaKB,10:F1} | {service.AverageMemoryPerOperationKB,11:F1} | {service.OperationCount,10} | {service.LastOperationTime:HH:mm:ss}");
                }
            }
            else
            {
                report.AppendLine("No service data available");
            }
            report.AppendLine();

            // Potential leaks
            report.AppendLine("=== POTENTIAL MEMORY LEAKS ===");
            if (_currentStats.PotentialLeaks?.Any() == true)
            {
                report.AppendLine("Service Name                 | Est. Leak (KB) | Avg Leak/Op (KB) | Description");
                report.AppendLine(new string('-', 100));
                
                foreach (var leak in _currentStats.PotentialLeaks)
                {
                    report.AppendLine($"{leak.ServiceName,-28} | {leak.TotalLeakEstimateKB,14:F1} | {leak.AverageLeakPerOperationKB,16:F1} | {leak.Description}");
                }
            }
            else
            {
                report.AppendLine("✅ No potential memory leaks detected");
            }
            report.AppendLine();

            // Recent operations
            report.AppendLine("=== RECENT OPERATIONS (Last 20) ===");
            report.AppendLine("Time      | Operation                    | Memory (KB) | Duration (ms)");
            report.AppendLine(new string('-', 75));
            
            if (_currentStats.RecentOperations?.Any() == true)
            {
                foreach (var op in _currentStats.RecentOperations.Take(20))
                {
                    report.AppendLine($"{op.Timestamp:HH:mm:ss} | {op.OperationName,-28} | {op.MemoryDeltaKB,11:+0.0;-0.0;0.0} | {op.Duration.TotalMilliseconds,13:F1}");
                }
            }
            else
            {
                report.AppendLine("No recent operations available");
            }

            File.WriteAllText(filePath, report.ToString());
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
#endif
