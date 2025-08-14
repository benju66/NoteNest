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
                        
                        // Ensure paths are resolved based on storage mode
                        var storageService = new StorageLocationService();
                        var resolvedPath = storageService.ResolveNotesPath(
                            _settings.StorageMode, 
                            _settings.CustomNotesPath);
                        
                        if (_settings.StorageMode != StorageMode.Local || 
                            !string.Equals(_settings.DefaultNotePath, resolvedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            _settings.DefaultNotePath = resolvedPath;
                            _settings.MetadataPath = Path.Combine(resolvedPath, ".metadata");
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
