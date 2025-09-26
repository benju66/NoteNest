using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Services;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Coordinates emergency save detection and recovery operations during application startup.
    /// Extracted from MainViewModel to separate recovery and emergency handling concerns.
    /// </summary>
    public class EmergencyRecoveryCoordinator : IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IDialogService _dialogService;
        private readonly IStateManager _stateManager;
        private readonly ISaveManager _saveManager;
        private readonly IAppLogger _logger;
        private readonly Func<IWorkspaceService> _getWorkspaceService;
        private bool _disposed;

        public EmergencyRecoveryCoordinator(
            IServiceProvider serviceProvider,
            IDialogService dialogService,
            IStateManager stateManager,
            ISaveManager saveManager,
            IAppLogger logger,
            Func<IWorkspaceService> getWorkspaceService)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _getWorkspaceService = getWorkspaceService ?? throw new ArgumentNullException(nameof(getWorkspaceService));

            // Subscribe to save manager events for emergency and external change handling
            _saveManager.SaveCompleted += OnSaveCompleted;
            _saveManager.ExternalChangeDetected += OnExternalChangeDetected;
        }

        /// <summary>
        /// Checks for WAL recovery data from previous sessions
        /// </summary>
        public async Task CheckForRecoveryAsync()
        {
            try
            {
                var wal = _serviceProvider.GetService<IWriteAheadLog>();
                if (wal == null) return;
                
                var recovered = await wal.RecoverAllAsync();
                if (recovered.Count > 0)
                {
                    _logger.Info($"Found {recovered.Count} unsaved notes from previous session");
                    
                    // Optional: Show notification to user
                    var message = recovered.Count == 1 
                        ? "Recovered 1 unsaved note from previous session" 
                        : $"Recovered {recovered.Count} unsaved notes from previous session";
                        
                    _stateManager.StatusMessage = message;
                    
                    // Note: The recovered content will be loaded when notes are opened
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to check for recovery");
            }
        }

        /// <summary>
        /// Checks for emergency backup files from previous sessions
        /// </summary>
        public async Task CheckForEmergencySavesAsync()
        {
            try
            {
                var emergencyDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NoteNest_Emergency");
                
                if (Directory.Exists(emergencyDir))
                {
                    var emergencyFiles = Directory.GetFiles(emergencyDir, "EMERGENCY_*.txt");
                    if (emergencyFiles.Length > 0)
                    {
                        var message = $"Found {emergencyFiles.Length} emergency backup(s) from previous sessions.\n" +
                                     $"Check {emergencyDir} to recover your content.";
                        
                        _dialogService?.ShowInfo(message, "Emergency Backups Found");
                        
                        _stateManager.StatusMessage = $"Found {emergencyFiles.Length} emergency backup(s) - check Documents folder";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to check for emergency saves");
            }
        }

        #region Event Handlers

        /// <summary>
        /// Handles save completion events to detect emergency saves
        /// </summary>
        private void OnSaveCompleted(object? sender, SaveProgressEventArgs e)
        {
            if (e != null && e.FilePath.Contains("EMERGENCY"))
            {
                // Show notification to user
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var message = $"File could not be saved to original location.\n" +
                                 $"Emergency backup saved to:\n{e.FilePath}";
                    
                    _dialogService?.ShowError(message, "Emergency Save");
                    
                    // Update status bar
                    _stateManager.StatusMessage = "Emergency save completed - check Documents/NoteNest_Emergency";
                });
            }
        }

        /// <summary>
        /// Handles external file change detection with user conflict resolution
        /// </summary>
        private async void OnExternalChangeDetected(object sender, ExternalChangeEventArgs e)
        {
            // Run on UI thread
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var result = MessageBox.Show(
                    $"The file '{Path.GetFileName(e.FilePath)}' has been modified externally.\n\n" +
                    "Do you want to reload it?\n\n" +
                    "Yes = Reload from disk (lose local changes)\n" +
                    "No = Keep local version (overwrite on next save)",
                    "External Change Detected",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    var saveManager = _serviceProvider.GetService<ISaveManager>();
                    await saveManager.ResolveExternalChangeAsync(e.NoteId, ConflictResolution.KeepExternal);
                    
                    // Refresh UI
                    var workspace = _getWorkspaceService();
                    var tab = workspace.FindTabByPath(e.FilePath);
                    if (tab != null)
                    {
                        tab.Content = saveManager.GetContent(e.NoteId);
                    }
                }
                else
                {
                    var saveManager = _serviceProvider.GetService<ISaveManager>();
                    await saveManager.ResolveExternalChangeAsync(e.NoteId, ConflictResolution.KeepLocal);
                }
            });
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Unsubscribe from save manager events
                if (_saveManager != null)
                {
                    _saveManager.SaveCompleted -= OnSaveCompleted;
                    _saveManager.ExternalChangeDetected -= OnExternalChangeDetected;
                }
                
                _disposed = true;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Error disposing EmergencyRecoveryCoordinator: {ex.Message}");
            }
        }
    }
}
