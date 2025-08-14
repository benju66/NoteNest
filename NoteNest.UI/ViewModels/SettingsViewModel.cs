using System;
using System.IO;
using System.Windows.Input;
using System.Threading.Tasks;
using Microsoft.Win32;
using NoteNest.Core.Models;
using NoteNest.Core.Services;
using NoteNest.UI.Commands;

namespace NoteNest.UI.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ConfigurationService _configService;
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
            _settings = configService.Settings ?? new AppSettings();
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

        public async Task SaveSettings()
        {
            try
            {
                await _configService.UpdateSettingsAsync(_settings);
            }
            catch (Exception ex)
            {
                // Handle error appropriately (log, notify UI, etc.)
                throw;
            }
        }
    }
}
