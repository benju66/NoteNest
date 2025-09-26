using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NoteNest.Core.Services;
using NoteNest.UI.Services;

namespace NoteNest.UI.Windows
{
    public partial class MigrationWindow : Window
    {
        private readonly MigrationService _migrationService;
        private readonly string _sourcePath;
        private readonly string _destinationPath;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _cleanupAvailable;

        public bool MigrationSuccessful { get; private set; }

        public MigrationWindow(string sourcePath, string destinationPath)
        {
            InitializeComponent();
            
            _migrationService = new MigrationService();
            _sourcePath = sourcePath;
            _destinationPath = destinationPath;
            
            // Display paths
            SourcePath.Text = sourcePath;
            DestinationPath.Text = destinationPath;
            
            // Wire up events
            _migrationService.ProgressChanged += OnProgressChanged;
            _migrationService.LogMessage += OnLogMessage;

            // Initial state
            ProgressBar.Value = 0;
            StartButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            CleanupButton.IsEnabled = false;

            // Add initial log message
            AddLogMessage("Click 'Start Migration' to begin moving your notes.");
        }

        private void OnProgressChanged(object sender, MigrationProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = e.Progress;
                StatusText.Text = $"Processing {e.CurrentFile} ({e.ProcessedFiles}/{e.TotalFiles})";
            });
        }

        private void OnLogMessage(object sender, string message)
        {
            Dispatcher.Invoke(() => AddLogMessage(message));
        }

        private void AddLogMessage(string message)
        {
            var timestampedMessage = $"{DateTime.Now:HH:mm:ss} - {message}";
            LogList.Items.Add(timestampedMessage);
            
            // Auto-scroll to latest message
            if (LogList.Items.Count > 0)
            {
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            }
        }

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            var dlg = (System.Windows.Application.Current as App)?.ServiceProvider?.GetService(typeof(IDialogService)) as IDialogService;
            // Validate paths first
            if (!Directory.Exists(_sourcePath))
            {
                dlg?.ShowError($"Source directory does not exist:\n{_sourcePath}", "Error");
                return;
            }

            if (_sourcePath.Equals(_destinationPath, StringComparison.OrdinalIgnoreCase))
            {
                dlg?.ShowInfo("Source and destination paths are the same.", "Error");
                return;
            }

            // Update UI state
            StartButton.IsEnabled = false;
            CancelButton.Content = "Stop";
            ProgressBar.Value = 0;
            
            _cancellationTokenSource = new CancellationTokenSource();
            
            try
            {
                AddLogMessage("Starting migration...");
                
                MigrationSuccessful = await _migrationService.MigrateNotesAsync(
                    _sourcePath, 
                    _destinationPath,
                    _cancellationTokenSource.Token);
                
                if (MigrationSuccessful)
                {
                    AddLogMessage("Migration completed successfully!");
                    
                    await Task.Delay(1000); // Let user see the completion
                    
                    dlg?.ShowInfo("Migration completed successfully!\n\nYour notes have been moved to the new location.", "Success");
                    
                    _cleanupAvailable = true;
                    CleanupButton.IsEnabled = true;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    AddLogMessage("Migration was cancelled or failed.");
                    StartButton.IsEnabled = true;
                    CancelButton.Content = "Cancel";
                }
            }
            catch (Exception ex)
            {
                AddLogMessage($"ERROR: {ex.Message}");
                dlg?.ShowError($"Migration failed:\n{ex.Message}", "Error");
                StartButton.IsEnabled = true;
                CancelButton.Content = "Cancel";
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                StatusText.Text = "Cancelling...";
                AddLogMessage("Migration cancelled by user.");
            }
            else
            {
                DialogResult = false;
                Close();
            }
        }

        private async void Cleanup_Click(object sender, RoutedEventArgs e)
        {
            if (!_cleanupAvailable)
                return;

            var dlg = (System.Windows.Application.Current as App)?.ServiceProvider?.GetService(typeof(IDialogService)) as IDialogService;
            var ok = await dlg!.ShowConfirmationDialogAsync(
                $"This will delete the old location and all its contents:\n\n{_sourcePath}\n\nAre you sure?",
                "Confirm Cleanup");
            if (!ok)
                return;

            try
            {
                await Task.Run(() =>
                {
                    if (Directory.Exists(_sourcePath))
                    {
                        Directory.Delete(_sourcePath, recursive: true);
                    }
                });
                AddLogMessage("Old location deleted successfully.");
                CleanupButton.IsEnabled = false;
                _cleanupAvailable = false;
                dlg?.ShowInfo("Old location cleaned up.", "Cleanup Complete");
            }
            catch (Exception ex)
            {
                AddLogMessage($"Cleanup failed: {ex.Message}");
                dlg?.ShowError($"Cleanup failed:\n{ex.Message}", "Error");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            
            // Clean up
            _migrationService.ProgressChanged -= OnProgressChanged;
            _migrationService.LogMessage -= OnLogMessage;
            _cancellationTokenSource?.Dispose();
        }
    }
}


