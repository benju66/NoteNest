using System;
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

        public SettingsViewModel(ConfigurationService configService)
        {
            _configService = configService;
            _settings = configService.Settings ?? new AppSettings();

            BrowseDefaultPathCommand = new RelayCommand(_ => BrowseDefaultPath());
            BrowseMetadataPathCommand = new RelayCommand(_ => BrowseMetadataPath());
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
