using System;
using System.IO;
using System.Windows.Input;
using System.Threading.Tasks;
using Microsoft.Win32;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.Core.Events;
using NoteNest.UI.Commands;

namespace NoteNest.UI.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ConfigurationService _configService;
        private readonly AppSettings _originalSettings;
        private AppSettings _settings;
        private bool _showLeftPanelOnStartup = true;
        private bool _showRightPanelOnStartup = false;
        private StorageLocationService _storageService;

        public AppSettings Settings
        {
            get => _settings;
            set => SetProperty(ref _settings, value);
        }

        public bool ShowLeftPanelOnStartup
        {
            get => _showLeftPanelOnStartup;
            set => SetProperty(ref _showLeftPanelOnStartup, value);
        }

        public bool ShowRightPanelOnStartup
        {
            get => _showRightPanelOnStartup;
            set => SetProperty(ref _showRightPanelOnStartup, value);
        }

        public ICommand BrowseDefaultPathCommand { get; }
        public ICommand BrowseMetadataPathCommand { get; }
        public ICommand BrowseCustomPathCommand { get; }

        public SettingsViewModel(ConfigurationService configService)
        {
            _configService = configService;
            _originalSettings = configService.Settings ?? new AppSettings();
            _settings = CloneSettings(_originalSettings);
            _storageService = new StorageLocationService();

            BrowseDefaultPathCommand = new RelayCommand(_ => BrowseDefaultPath());
            BrowseMetadataPathCommand = new RelayCommand(_ => BrowseMetadataPath());
            BrowseCustomPathCommand = new RelayCommand(_ => BrowseCustomPath());
        }

        private void BrowseDefaultPath()
        {
            var dialog = new OpenFolderDialog();
            if (!string.IsNullOrEmpty(Settings.DefaultNotePath) && System.IO.Directory.Exists(Settings.DefaultNotePath))
            {
                dialog.InitialDirectory = Settings.DefaultNotePath;
            }
            if (dialog.ShowDialog() == true)
            {
                Settings.DefaultNotePath = dialog.FolderName;
                OnPropertyChanged(nameof(Settings));
            }
        }

        private void BrowseMetadataPath()
        {
            var dialog = new OpenFolderDialog();
            if (!string.IsNullOrEmpty(Settings.MetadataPath) && System.IO.Directory.Exists(Settings.MetadataPath))
            {
                dialog.InitialDirectory = Settings.MetadataPath;
            }
            if (dialog.ShowDialog() == true)
            {
                Settings.MetadataPath = dialog.FolderName;
                OnPropertyChanged(nameof(Settings));
            }
        }

        public bool UseLocalStorage
        {
            get => Settings.StorageMode == StorageMode.Local;
            set
            {
                if (value)
                {
                    Settings.StorageMode = StorageMode.Local;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UseOneDrive));
                    OnPropertyChanged(nameof(UseCustomPath));
                }
            }
        }

        public bool UseOneDrive
        {
            get => Settings.StorageMode == StorageMode.OneDrive;
            set
            {
                if (value)
                {
                    Settings.StorageMode = StorageMode.OneDrive;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UseLocalStorage));
                    OnPropertyChanged(nameof(UseCustomPath));
                }
            }
        }

        public bool UseCustomPath
        {
            get => Settings.StorageMode == StorageMode.Custom;
            set
            {
                if (value)
                {
                    Settings.StorageMode = StorageMode.Custom;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UseLocalStorage));
                    OnPropertyChanged(nameof(UseOneDrive));
                }
            }
        }

        public string CustomPath
        {
            get => Settings.CustomNotesPath;
            set
            {
                Settings.CustomNotesPath = value;
                OnPropertyChanged();
            }
        }

        private void BrowseCustomPath()
        {
            var dialog = new OpenFolderDialog();
            if (!string.IsNullOrEmpty(CustomPath) && Directory.Exists(CustomPath))
            {
                dialog.InitialDirectory = CustomPath;
            }
            if (dialog.ShowDialog() == true)
            {
                CustomPath = dialog.FolderName;
                UseCustomPath = true;
            }
        }

        public string CurrentStoragePath
        {
            get
            {
                // Show the working copy path after migration
                return Settings.DefaultNotePath;
            }
        }

        public string StorageModeDescription
        {
            get
            {
                switch (Settings.StorageMode)
                {
                    case StorageMode.OneDrive:
                        return "OneDrive - Synced across devices";
                    case StorageMode.Custom:
                        return $"Custom - {Settings.CustomNotesPath}";
                    case StorageMode.Local:
                    default:
                        return "Local - Documents/NoteNest folder";
                }
            }
        }

        // RTF-only architecture: Format properties removed
        // All notes use RTF format automatically

        public async Task CommitSettings()
        {
            try
            {
                // Get the TransactionalSettingsService if available
                var transactionalService = (System.Windows.Application.Current as App)?.ServiceProvider?
                    .GetService(typeof(NoteNest.Core.Services.ITransactionalSettingsService)) 
                    as NoteNest.Core.Services.ITransactionalSettingsService;

                if (transactionalService != null)
                {
                    // Use transaction-based settings change
                    var result = await transactionalService.ApplySettingsAsync(_settings, _originalSettings);
                    
                    if (result.Success)
                    {
                        // Update the original settings reference to match committed settings
                        CopySettings(_originalSettings, _settings);
                        CopySettings(_configService.Settings, _settings);
                        
                        OnPropertyChanged(nameof(CurrentStoragePath));
                        
                        // Show success notification if storage location changed
                        if (!result.SettingsOnly)
                        {
                            var statusNotifier = (System.Windows.Application.Current as App)?.ServiceProvider?
                                .GetService(typeof(NoteNest.Core.Interfaces.IStatusNotifier)) 
                                as NoteNest.Core.Interfaces.IStatusNotifier;
                            
                            statusNotifier?.ShowStatus(
                                $"Storage location changed successfully to: {result.NewStoragePath}", 
                                NoteNest.Core.Interfaces.StatusType.Success, 
                                5000);
                        }
                    }
                    else
                    {
                        // Show error notification
                        var statusNotifier = (System.Windows.Application.Current as App)?.ServiceProvider?
                            .GetService(typeof(NoteNest.Core.Interfaces.IStatusNotifier)) 
                            as NoteNest.Core.Interfaces.IStatusNotifier;
                            
                        statusNotifier?.ShowStatus(
                            $"Settings change failed: {result.ErrorMessage}", 
                            NoteNest.Core.Interfaces.StatusType.Error, 
                            7000);
                            
                        // Optionally throw exception to let caller handle it
                        throw new InvalidOperationException($"Settings change failed: {result.ErrorMessage}", result.Exception);
                    }
                }
                else
                {
                    // Fallback to original implementation if TransactionalSettingsService not available
                    await CommitSettingsFallback();
                }
            }
            catch (Exception ex)
            {
                // Log error and show user notification
                var logger = (System.Windows.Application.Current as App)?.ServiceProvider?
                    .GetService(typeof(NoteNest.Core.Services.Logging.IAppLogger)) 
                    as NoteNest.Core.Services.Logging.IAppLogger;
                    
                logger?.Error(ex, "Failed to commit settings changes");
                
                var statusNotifier = (System.Windows.Application.Current as App)?.ServiceProvider?
                    .GetService(typeof(NoteNest.Core.Interfaces.IStatusNotifier)) 
                    as NoteNest.Core.Interfaces.IStatusNotifier;
                    
                statusNotifier?.ShowStatus(
                    $"Settings save failed: {ex.Message}", 
                    NoteNest.Core.Interfaces.StatusType.Error, 
                    7000);
                    
                throw; // Re-throw to let caller handle
            }
        }

        /// <summary>
        /// Fallback method for committing settings without transaction support
        /// </summary>
        private async Task CommitSettingsFallback()
        {
            // Save to disk and update the service's internal reference
            await _configService.UpdateSettingsAsync(_settings);

            // Update both the original reference and the service's Settings
            CopySettings(_originalSettings, _settings);
            CopySettings(_configService.Settings, _settings);

            // Update PathService immediately
            NoteNest.Core.Services.PathService.RootPath = _settings.DefaultNotePath;

            OnPropertyChanged(nameof(CurrentStoragePath));

            // Broadcast settings changed so ActivityBar can refresh ordering/visibility
            try
            {
                var bus = (System.Windows.Application.Current as App)?.ServiceProvider?.GetService(typeof(Core.Services.IEventBus)) as Core.Services.IEventBus;
                if (bus != null)
                {
                    await bus.PublishAsync(new AppSettingsChangedEvent());
                }
            }
            catch { }
        }

        public void RefreshStorageProperties()
        {
            OnPropertyChanged(nameof(UseLocalStorage));
            OnPropertyChanged(nameof(UseOneDrive));
            OnPropertyChanged(nameof(UseCustomPath));
            OnPropertyChanged(nameof(CustomPath));
            OnPropertyChanged(nameof(CurrentStoragePath));
            OnPropertyChanged(nameof(StorageModeDescription));
            OnPropertyChanged(nameof(Settings));
        }

        public string GetCurrentSavedPath()
        {
            return _originalSettings.DefaultNotePath;
        }

        public string GetSelectedDestinationPath()
        {
            return CalculatePathFromMode(_settings);
        }

        private string CalculatePathFromMode(AppSettings settings)
        {
            if (settings == null)
                return string.Empty;

            switch (settings.StorageMode)
            {
                case StorageMode.OneDrive:
                    var oneDrive = _storageService.GetOneDrivePath();
                    return !string.IsNullOrEmpty(oneDrive)
                        ? System.IO.Path.Combine(oneDrive, "NoteNest")
                        : _storageService.ResolveNotesPath(StorageMode.Local);
                case StorageMode.Custom:
                    return !string.IsNullOrEmpty(settings.CustomNotesPath)
                        ? settings.CustomNotesPath
                        : _storageService.ResolveNotesPath(StorageMode.Local);
                case StorageMode.Local:
                default:
                    return _storageService.ResolveNotesPath(StorageMode.Local);
            }
        }

        private static AppSettings CloneSettings(AppSettings source)
        {
            if (source == null)
            {
                return new AppSettings();
            }

            var clone = new AppSettings
            {
                DefaultNotePath = source.DefaultNotePath,
                MetadataPath = source.MetadataPath,
                AutoSave = source.AutoSave,
                AutoSaveInterval = source.AutoSaveInterval,
                Theme = source.Theme,
                ShowStatusBar = source.ShowStatusBar,
                EditorSettings = new EditorSettings
                {
                    WordWrap = source.EditorSettings.WordWrap,
                    FontSize = source.EditorSettings.FontSize,
                    FontFamily = source.EditorSettings.FontFamily,
                    ShowLineNumbers = source.EditorSettings.ShowLineNumbers,
                    HighlightCurrentLine = source.EditorSettings.HighlightCurrentLine,
                    TabSize = source.EditorSettings.TabSize,
                    InsertSpaces = source.EditorSettings.InsertSpaces,
                    ShowFormattingToolbar = source.EditorSettings.ShowFormattingToolbar,
                    EnhancedListHandling = source.EditorSettings.EnhancedListHandling
                },
                CreateBackup = source.CreateBackup,
                MaxBackups = source.MaxBackups,
                ShowWelcomeScreen = source.ShowWelcomeScreen,
                CheckForUpdates = source.CheckForUpdates,
                StorageMode = source.StorageMode,
                CustomNotesPath = source.CustomNotesPath,
                AutoDetectOneDrive = source.AutoDetectOneDrive,
                DefaultNoteFormat = source.DefaultNoteFormat, // RTF-only architecture
                EnableTaskPanel = source.EnableTaskPanel,
                ParseMarkdownCheckboxes = source.ParseMarkdownCheckboxes,
                QuickNoteHotkey = source.QuickNoteHotkey,
                QuickTaskHotkey = source.QuickTaskHotkey,
                AutoSaveIdleMs = source.AutoSaveIdleMs,
                ShowTreeDirtyDot = source.ShowTreeDirtyDot,
                FileWatcherDebounceMs = source.FileWatcherDebounceMs,
                FileWatcherBufferKB = source.FileWatcherBufferKB,
                ContentCacheMaxMB = source.ContentCacheMaxMB,
                ContentCacheExpirationMinutes = source.ContentCacheExpirationMinutes,
                ContentCacheCleanupMinutes = source.ContentCacheCleanupMinutes,
                SearchIndexContentWordLimit = source.SearchIndexContentWordLimit,
                SettingsSaveDebounceMs = source.SettingsSaveDebounceMs,
                RecentFiles = source.RecentFiles != null ? new System.Collections.Generic.List<string>(source.RecentFiles) : new System.Collections.Generic.List<string>(),
                WindowSettings = source.WindowSettings != null
                    ? new NoteNest.Core.Models.WindowSettings
                    {
                        Width = source.WindowSettings.Width,
                        Height = source.WindowSettings.Height,
                        Left = source.WindowSettings.Left,
                        Top = source.WindowSettings.Top,
                        IsMaximized = source.WindowSettings.IsMaximized
                    }
                    : new NoteNest.Core.Models.WindowSettings()
            };

            return clone;
        }

        private static void CopySettings(AppSettings target, AppSettings source)
        {
            if (target == null || source == null)
            {
                return;
            }

            target.DefaultNotePath = source.DefaultNotePath;
            target.MetadataPath = source.MetadataPath;
            target.AutoSave = source.AutoSave;
            target.AutoSaveInterval = source.AutoSaveInterval;
            target.Theme = source.Theme;
            target.ShowStatusBar = source.ShowStatusBar;
            
            // Copy editor settings
            target.EditorSettings.WordWrap = source.EditorSettings.WordWrap;
            target.EditorSettings.FontSize = source.EditorSettings.FontSize;
            target.EditorSettings.FontFamily = source.EditorSettings.FontFamily;
            target.EditorSettings.ShowLineNumbers = source.EditorSettings.ShowLineNumbers;
            target.EditorSettings.HighlightCurrentLine = source.EditorSettings.HighlightCurrentLine;
            target.EditorSettings.TabSize = source.EditorSettings.TabSize;
            target.EditorSettings.InsertSpaces = source.EditorSettings.InsertSpaces;
            target.EditorSettings.ShowFormattingToolbar = source.EditorSettings.ShowFormattingToolbar;
            target.EditorSettings.EnhancedListHandling = source.EditorSettings.EnhancedListHandling;
            target.CreateBackup = source.CreateBackup;
            target.MaxBackups = source.MaxBackups;
            target.ShowWelcomeScreen = source.ShowWelcomeScreen;
            target.CheckForUpdates = source.CheckForUpdates;
            target.StorageMode = source.StorageMode;
            target.CustomNotesPath = source.CustomNotesPath;
            target.AutoDetectOneDrive = source.AutoDetectOneDrive;
            target.DefaultNoteFormat = source.DefaultNoteFormat; // RTF-only architecture
            target.EnableTaskPanel = source.EnableTaskPanel;
            target.ParseMarkdownCheckboxes = source.ParseMarkdownCheckboxes;
            target.QuickNoteHotkey = source.QuickNoteHotkey;
            target.QuickTaskHotkey = source.QuickTaskHotkey;
            target.AutoSaveIdleMs = source.AutoSaveIdleMs;
            target.ShowTreeDirtyDot = source.ShowTreeDirtyDot;
            target.FileWatcherDebounceMs = source.FileWatcherDebounceMs;
            target.FileWatcherBufferKB = source.FileWatcherBufferKB;
            target.ContentCacheMaxMB = source.ContentCacheMaxMB;
            target.ContentCacheExpirationMinutes = source.ContentCacheExpirationMinutes;
            target.ContentCacheCleanupMinutes = source.ContentCacheCleanupMinutes;
            target.SearchIndexContentWordLimit = source.SearchIndexContentWordLimit;
            target.SettingsSaveDebounceMs = source.SettingsSaveDebounceMs;

            target.RecentFiles = source.RecentFiles != null
                ? new System.Collections.Generic.List<string>(source.RecentFiles)
                : new System.Collections.Generic.List<string>();

            if (target.WindowSettings == null)
            {
                target.WindowSettings = new NoteNest.Core.Models.WindowSettings();
            }

            if (source.WindowSettings != null)
            {
                target.WindowSettings.Width = source.WindowSettings.Width;
                target.WindowSettings.Height = source.WindowSettings.Height;
                target.WindowSettings.Left = source.WindowSettings.Left;
                target.WindowSettings.Top = source.WindowSettings.Top;
                target.WindowSettings.IsMaximized = source.WindowSettings.IsMaximized;
            }
        }
    }
}
