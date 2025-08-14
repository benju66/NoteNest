using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Models;

namespace NoteNest.Core.Services
{
    public class ConfigurationService
    {
        private readonly IFileSystemProvider _fileSystem;
        private readonly string _settingsPath;
        private AppSettings _settings = new();
        private readonly JsonSerializerOptions _jsonOptions;

        public AppSettings Settings => _settings;

        public ConfigurationService(IFileSystemProvider? fileSystem = null)
        {
            _fileSystem = fileSystem ?? new DefaultFileSystemProvider();
            
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var noteNestPath = Path.Combine(appDataPath, "NoteNest");
            _settingsPath = Path.Combine(noteNestPath, "settings.json");
            
            _jsonOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
        }

        public async Task<AppSettings> LoadSettingsAsync()
        {
            if (await _fileSystem.ExistsAsync(_settingsPath))
            {
                try
                {
                    var json = await _fileSystem.ReadTextAsync(_settingsPath);
                    
                    // Deserialize without invoking constructor defaults
                    var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                    
                    if (loadedSettings != null)
                    {
                        _settings = loadedSettings;

                        // Only resolve paths if they're empty or don't exist
                        if (string.IsNullOrEmpty(_settings.DefaultNotePath) ||
                            !Directory.Exists(_settings.DefaultNotePath))
                        {
                            var storageService = new StorageLocationService();
                            _settings.DefaultNotePath = storageService.ResolveNotesPath(
                                _settings.StorageMode,
                                _settings.CustomNotesPath);
                        }

                        // Ensure metadata path is set
                        if (string.IsNullOrEmpty(_settings.MetadataPath))
                        {
                            _settings.MetadataPath = Path.Combine(_settings.DefaultNotePath, ".metadata");
                        }

                        // Keep global path service in sync with loaded settings
                        PathService.RootPath = _settings.DefaultNotePath;
                    }
                    else
                    {
                        _settings = new AppSettings();
                        PathService.RootPath = _settings.DefaultNotePath;
                        await SaveSettingsAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
                    _settings = new AppSettings();
                    PathService.RootPath = _settings.DefaultNotePath;
                    await SaveSettingsAsync();
                }
            }
            else
            {
                _settings = new AppSettings();
                PathService.RootPath = _settings.DefaultNotePath;
                await SaveSettingsAsync();
            }

            // Ensure defaults AFTER loading/creating settings
            if (_settings.RecentFiles == null)
            {
                _settings.RecentFiles = new System.Collections.Generic.List<string>();
            }
            if (_settings.WindowSettings == null)
            {
                _settings.WindowSettings = new WindowSettings();
            }

            if (string.IsNullOrEmpty(_settings.DefaultNotePath))
            {
                _settings.DefaultNotePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NoteNest");
            }

            if (string.IsNullOrEmpty(_settings.MetadataPath))
            {
                _settings.MetadataPath = Path.Combine(_settings.DefaultNotePath, ".metadata");
            }

            if (_settings.AutoSaveInterval == 0) _settings.AutoSaveInterval = 30;
            if (_settings.FontSize == 0) _settings.FontSize = 14;
            if (_settings.TabSize == 0) _settings.TabSize = 4;
            if (_settings.MaxBackups == 0) _settings.MaxBackups = 5;
            if (string.IsNullOrEmpty(_settings.Theme)) _settings.Theme = "System";
            if (string.IsNullOrEmpty(_settings.FontFamily)) _settings.FontFamily = "Consolas";
            if (string.IsNullOrEmpty(_settings.DefaultNoteFormat)) _settings.DefaultNoteFormat = ".txt";
            if (string.IsNullOrEmpty(_settings.QuickNoteHotkey)) _settings.QuickNoteHotkey = "Win+N";
            if (string.IsNullOrEmpty(_settings.QuickTaskHotkey)) _settings.QuickTaskHotkey = "Win+T";

            return _settings;
        }

        public async Task SaveSettingsAsync()
        {
            var directory = Path.GetDirectoryName(_settingsPath) ?? string.Empty;
            if (!await _fileSystem.ExistsAsync(directory))
            {
                await _fileSystem.CreateDirectoryAsync(directory);
            }

            var json = JsonSerializer.Serialize(_settings, _jsonOptions);
            await _fileSystem.WriteTextAsync(_settingsPath, json);
        }

        public async Task UpdateSettingsAsync(AppSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            PathService.RootPath = _settings.DefaultNotePath;
            await SaveSettingsAsync();
        }

        public async Task EnsureDefaultDirectoriesAsync()
        {
            // Ensure default note path exists
            if (!await _fileSystem.ExistsAsync(_settings.DefaultNotePath))
            {
                await _fileSystem.CreateDirectoryAsync(_settings.DefaultNotePath);
            }

            // Ensure metadata path exists
            if (!await _fileSystem.ExistsAsync(_settings.MetadataPath))
            {
                await _fileSystem.CreateDirectoryAsync(_settings.MetadataPath);
            }
        }

        public void AddRecentFile(string filePath)
        {
            if (_settings.RecentFiles.Contains(filePath))
            {
                _settings.RecentFiles.Remove(filePath);
            }
            
            _settings.RecentFiles.Insert(0, filePath);
            
            // Keep only last 10 recent files
            while (_settings.RecentFiles.Count > 10)
            {
                _settings.RecentFiles.RemoveAt(_settings.RecentFiles.Count - 1);
            }
        }
    }
}
