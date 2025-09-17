using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Models;
using System.Linq;

namespace NoteNest.Core.Services
{
    public class ConfigurationService
    {
        private readonly IFileSystemProvider _fileSystem;
        private readonly IEventBus? _eventBus;
        private readonly string _settingsPath;
        private AppSettings _settings = new();
        private readonly JsonSerializerOptions _jsonOptions;
        private System.Threading.Timer? _saveTimer;
        private readonly System.Threading.SemaphoreSlim _saveLock = new(1, 1);

        public AppSettings Settings => _settings;

        public ConfigurationService(IFileSystemProvider? fileSystem = null, IEventBus? eventBus = null)
        {
            _fileSystem = fileSystem ?? new DefaultFileSystemProvider();
            _eventBus = eventBus;
            
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var noteNestPath = Path.Combine(appDataPath, "NoteNest");
            _settingsPath = Path.Combine(noteNestPath, "settings.json");
            
            _jsonOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };

            // Subscribe to note saved events for recent files tracking
            if (_eventBus != null)
            {
                _eventBus.Subscribe<NoteNest.Core.Events.NoteSavedEvent>(e =>
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(e?.FilePath))
                        {
                            AddRecentFile(e.FilePath);
                            RequestSaveDebounced();
                        }
                    }
                    catch { }
                });
            }
        }

        public async Task<AppSettings> LoadSettingsAsync()
        {
            await MigrateSettingsIfNeeded();
            if (await _fileSystem.ExistsAsync(_settingsPath))
            {
                try
                {
                    var json = await _fileSystem.ReadTextAsync(_settingsPath);
                    
                    // Deserialize without invoking constructor defaults
                    var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                    
                    if (loadedSettings != null)
                    {
                        // PHASE 2A: Migrate settings from old format to RTF-only architecture
                        await MigrateNoteFormatSettings(loadedSettings);
                        
                        _settings = loadedSettings;

                        // Only resolve paths if they're empty; do NOT overwrite if directory is missing
                        if (string.IsNullOrEmpty(_settings.DefaultNotePath))
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
            if (_settings.EditorSettings.FontSize == 0) _settings.EditorSettings.FontSize = 14;
            if (_settings.EditorSettings.TabSize == 0) _settings.EditorSettings.TabSize = 4;
            if (_settings.MaxBackups == 0) _settings.MaxBackups = 5;
            if (string.IsNullOrEmpty(_settings.Theme)) _settings.Theme = "System";
            if (string.IsNullOrEmpty(_settings.EditorSettings.FontFamily)) _settings.EditorSettings.FontFamily = "Consolas";
            // Default format enum handled by AppSettings default value
            if (string.IsNullOrEmpty(_settings.QuickNoteHotkey)) _settings.QuickNoteHotkey = "Win+N";
            if (string.IsNullOrEmpty(_settings.QuickTaskHotkey)) _settings.QuickTaskHotkey = "Win+T";

            return _settings;
        }

        private async Task MigrateSettingsIfNeeded()
        {
            try
            {
                var roamingPath = _settingsPath;
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var localNoteNest = Path.Combine(localAppData, "NoteNest");
                var localPath = Path.Combine(localNoteNest, "settings.json");

                var roamingExists = await _fileSystem.ExistsAsync(roamingPath);
                var localExists = await _fileSystem.ExistsAsync(localPath);
                if (!roamingExists && localExists)
                {
                    var directory = Path.GetDirectoryName(roamingPath) ?? string.Empty;
                    if (!await _fileSystem.ExistsAsync(directory))
                    {
                        await _fileSystem.CreateDirectoryAsync(directory);
                    }
                    await _fileSystem.CopyAsync(localPath, roamingPath, overwrite: true);
                }
            }
            catch
            {
                // Ignore migration errors; fallback to defaults
            }
        }

        public async Task SaveSettingsAsync()
        {
            var directory = Path.GetDirectoryName(_settingsPath) ?? string.Empty;
            if (!await _fileSystem.ExistsAsync(directory))
            {
                await _fileSystem.CreateDirectoryAsync(directory);
            }

            var json = JsonSerializer.Serialize(_settings, _jsonOptions);
            var tempPath = _settingsPath + ".tmp";
            var backupPath = _settingsPath + ".bak";

            // Write to temp first
            await _fileSystem.WriteTextAsync(tempPath, json);

            // Replace atomically where supported (Windows)
            try
            {
                await _fileSystem.ReplaceAsync(tempPath, _settingsPath, backupPath);
            }
            catch
            {
                // Fallback: delete and move
                if (await _fileSystem.ExistsAsync(_settingsPath))
                {
                    await _fileSystem.DeleteAsync(_settingsPath);
                }
                await _fileSystem.MoveAsync(tempPath, _settingsPath, overwrite: false);
            }
        }

        public async Task FlushPendingAsync()
        {
            // Acquire the save lock to ensure any in-flight debounced save completes
            await _saveLock.WaitAsync();
            try
            {
                // Cancel timer and perform immediate save
                _saveTimer?.Dispose();
                _saveTimer = null;
                await SaveSettingsAsync();
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public async Task UpdateSettingsAsync(AppSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            PathService.RootPath = _settings.DefaultNotePath;
            await SaveSettingsAsync();
        }

        // Tree expansion persistence helpers
        public async Task SaveExpandedCategoriesAsync(System.Collections.Generic.IEnumerable<string> expandedIds)
        {
            _settings.ExpandedCategoryIds = expandedIds?.ToList() ?? new System.Collections.Generic.List<string>();
            await SaveSettingsAsync();
        }

        public async Task<System.Collections.Generic.HashSet<string>> LoadExpandedCategoriesAsync()
        {
            await Task.CompletedTask;
            return new System.Collections.Generic.HashSet<string>(_settings?.ExpandedCategoryIds ?? new System.Collections.Generic.List<string>());
        }

        // Debounced save requester for callers who may call frequently
        public void RequestSaveDebounced(int debounceMs = 0)
        {
            if (debounceMs <= 0)
            {
                try { debounceMs = _settings?.SettingsSaveDebounceMs > 0 ? _settings.SettingsSaveDebounceMs : 5000; }
                catch { debounceMs = 5000; }
            }
            _saveTimer?.Dispose();
            _saveTimer = new System.Threading.Timer(async _ =>
            {
                await _saveLock.WaitAsync();
                try
                {
                    await SaveSettingsAsync();
                }
                finally
                {
                    _saveLock.Release();
                }
            }, null, debounceMs, System.Threading.Timeout.Infinite);
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

            // Migrate old "Projects" folder to "Notes" folder if needed
            await MigrateProjectsToNotesAsync();
        }

        private async Task MigrateProjectsToNotesAsync()
        {
            try
            {
                var oldProjectsPath = Path.Combine(_settings.DefaultNotePath, "Projects");
                var newNotesPath = Path.Combine(_settings.DefaultNotePath, "Notes");
                
                // Only migrate if old exists and new doesn't
                if (await _fileSystem.ExistsAsync(oldProjectsPath) && !await _fileSystem.ExistsAsync(newNotesPath))
                {
                    try 
                    {
                        Directory.Move(oldProjectsPath, newNotesPath);
                        System.Diagnostics.Debug.WriteLine("Successfully migrated Projects folder to Notes folder");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to migrate Projects to Notes: {ex.Message}");
                        // Don't throw - let the app continue and create Notes folder normally
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during Projects→Notes migration: {ex.Message}");
            }
        }

        public void AddRecentFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            // Normalize and ensure case-insensitive uniqueness on Windows
            var fullPath = Path.GetFullPath(filePath);

            // Rebuild recent files with case-insensitive comparer
            var recent = new System.Collections.Generic.LinkedList<string>();
            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            // Seed existing list
            foreach (var existing in _settings.RecentFiles ?? new System.Collections.Generic.List<string>())
            {
                var normalized = existing;
                try { normalized = Path.GetFullPath(existing); } catch { }
                if (seen.Add(normalized))
                {
                    recent.AddLast(normalized);
                }
            }

            // Remove if already present and add to front
            if (seen.Contains(fullPath))
            {
                var node = recent.Find(fullPath);
                if (node != null) recent.Remove(node);
            }
            else
            {
                seen.Add(fullPath);
            }
            recent.AddFirst(fullPath);

            // Trim to max recent files
            var max = _settings.MaxRecentFiles > 0 ? _settings.MaxRecentFiles : 20;
            while (recent.Count > max)
            {
                recent.RemoveLast();
            }

            _settings.RecentFiles = recent.ToList();
        }
        /// <summary>
        /// PHASE 2A: Migrate settings from old multi-format architecture to RTF-only
        /// Handles existing settings files that might contain old enum values
        /// </summary>
        private async Task MigrateNoteFormatSettings(AppSettings settings)
        {
            if (settings == null) return;

            bool settingsChanged = false;

            try
            {
                // PHASE 2B: Create backup before migration (safety first)
                await BackupSettingsBeforeMigration();

                // Ensure DefaultNoteFormat is RTF (handle any old string values)
                if (settings.DefaultNoteFormat != NoteFormat.RTF)
                {
                    var oldValue = settings.DefaultNoteFormat;
                    settings.DefaultNoteFormat = NoteFormat.RTF;
                    settingsChanged = true;
                    System.Diagnostics.Debug.WriteLine($"[Settings Migration] DefaultNoteFormat: {oldValue} → RTF");
                }

                // Clean up obsolete format-related properties that shouldn't exist in RTF-only
                // These will be removed in Phase 4, but for now ensure they have safe values
                try
                {
                    var settingsType = typeof(AppSettings);
                    
                    // If AutoDetectFormat property exists, ensure it's false (meaningless in RTF-only)
                    var autoDetectProp = settingsType.GetProperty("AutoDetectFormat");
                    if (autoDetectProp != null && autoDetectProp.GetValue(settings) is bool autoDetect && autoDetect)
                    {
                        autoDetectProp.SetValue(settings, false);
                        settingsChanged = true;
                        System.Diagnostics.Debug.WriteLine("[Settings Migration] AutoDetectFormat disabled (RTF-only)");
                    }

                    // If ConvertTxtToMdOnSave property exists, ensure it's false (meaningless in RTF-only)
                    var convertProp = settingsType.GetProperty("ConvertTxtToMdOnSave");
                    if (convertProp != null && convertProp.GetValue(settings) is bool convert && convert)
                    {
                        convertProp.SetValue(settings, false);
                        settingsChanged = true;
                        System.Diagnostics.Debug.WriteLine("[Settings Migration] ConvertTxtToMdOnSave disabled (RTF-only)");
                    }
                }
                catch (Exception reflectionEx)
                {
                    // Reflection-based cleanup failed - not critical
                    System.Diagnostics.Debug.WriteLine($"[Settings Migration] Property cleanup failed: {reflectionEx.Message}");
                }

                // If we made changes, save the migrated settings
                if (settingsChanged)
                {
                    System.Diagnostics.Debug.WriteLine("[Settings Migration] Saving migrated settings");
                    await SaveSettingsAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings Migration] Error during migration: {ex.Message}");
                // Ensure RTF is set even if migration fails
                settings.DefaultNoteFormat = NoteFormat.RTF;
            }
        }

        /// <summary>
        /// PHASE 2B: Create backup of existing settings before migration
        /// </summary>
        private async Task BackupSettingsBeforeMigration()
        {
            try
            {
                if (await _fileSystem.ExistsAsync(_settingsPath))
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    var backupPath = _settingsPath.Replace(".json", $".backup_{timestamp}.json");
                    
                    var existingContent = await _fileSystem.ReadTextAsync(_settingsPath);
                    await _fileSystem.WriteTextAsync(backupPath, existingContent);
                    
                    System.Diagnostics.Debug.WriteLine($"[Settings Migration] Backup created: {backupPath}");
                }
            }
            catch (Exception ex)
            {
                // Backup failure is not critical - log but continue
                System.Diagnostics.Debug.WriteLine($"[Settings Migration] Backup failed: {ex.Message}");
            }
        }
    }
}
