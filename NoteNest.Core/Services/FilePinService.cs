using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Simple, reliable pin service using JSON file storage
    /// Treats pins as user preferences, separate from note metadata
    /// </summary>
    public class FilePinService : IPinService
    {
        private readonly IFileSystemProvider _fileSystem;
        private readonly IAppLogger _logger;
        private readonly string _pinsFilePath;
        private readonly string _backupPath;
        private readonly SemaphoreSlim _lock = new(1, 1);
        
        private HashSet<string> _pinnedNoteIds = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, PinEntry> _pinEntries = new(StringComparer.OrdinalIgnoreCase);
        private bool _initialized = false;
        
        public event EventHandler<PinChangedEventArgs> PinChanged;
        
        public FilePinService(IFileSystemProvider fileSystem, ConfigurationService config, IAppLogger logger)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Store pins in metadata directory
            var metadataPath = config?.Settings?.MetadataPath ?? 
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NoteNest");
            
            _pinsFilePath = Path.Combine(metadataPath, "pins.json");
            _backupPath = Path.Combine(metadataPath, "pins.backup.json");
        }
        
        private async Task EnsureInitializedAsync()
        {
            if (_initialized) return;
            
            await _lock.WaitAsync();
            try
            {
                if (_initialized) return;
                
                await LoadPinsAsync();
                _initialized = true;
                _logger.Debug($"FilePinService initialized with {_pinnedNoteIds.Count} pins");
            }
            finally
            {
                _lock.Release();
            }
        }
        
        public async Task<bool> IsPinnedAsync(string noteId)
        {
            if (string.IsNullOrEmpty(noteId)) return false;
            
            await EnsureInitializedAsync();
            return _pinnedNoteIds.Contains(noteId);
        }
        
        public async Task<bool> TogglePinAsync(string noteId, string filePath)
        {
            if (string.IsNullOrEmpty(noteId)) return false;
            
            await EnsureInitializedAsync();
            
            await _lock.WaitAsync();
            try
            {
                bool wasPinned = _pinnedNoteIds.Contains(noteId);
                
                if (wasPinned)
                {
                    _pinnedNoteIds.Remove(noteId);
                    _pinEntries.Remove(noteId);
                    _logger.Info($"Unpinned note: {noteId}");
                }
                else
                {
                    _pinnedNoteIds.Add(noteId);
                    _pinEntries[noteId] = new PinEntry(noteId, filePath ?? string.Empty);
                    _logger.Info($"Pinned note: {noteId}");
                }
                
                await SavePinsAsync();
                
                // Notify UI of change
                OnPinChanged(noteId, filePath ?? string.Empty, !wasPinned);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to toggle pin for note: {noteId}");
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }
        
        public async Task<bool> PinAsync(string noteId, string filePath)
        {
            if (string.IsNullOrEmpty(noteId)) return false;
            
            await EnsureInitializedAsync();
            
            await _lock.WaitAsync();
            try
            {
                if (_pinnedNoteIds.Contains(noteId))
                {
                    return true; // Already pinned
                }
                
                _pinnedNoteIds.Add(noteId);
                _pinEntries[noteId] = new PinEntry(noteId, filePath ?? string.Empty);
                
                await SavePinsAsync();
                
                _logger.Info($"Pinned note: {noteId}");
                OnPinChanged(noteId, filePath ?? string.Empty, true);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to pin note: {noteId}");
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }
        
        public async Task<bool> UnpinAsync(string noteId)
        {
            if (string.IsNullOrEmpty(noteId)) return false;
            
            await EnsureInitializedAsync();
            
            await _lock.WaitAsync();
            try
            {
                if (!_pinnedNoteIds.Contains(noteId))
                {
                    return true; // Already unpinned
                }
                
                var entry = _pinEntries.TryGetValue(noteId, out var existingEntry) ? existingEntry : null;
                var filePath = entry?.FilePath ?? string.Empty;
                
                _pinnedNoteIds.Remove(noteId);
                _pinEntries.Remove(noteId);
                
                await SavePinsAsync();
                
                _logger.Info($"Unpinned note: {noteId}");
                OnPinChanged(noteId, filePath, false);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to unpin note: {noteId}");
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }
        
        public async Task<IReadOnlyList<string>> GetPinnedNoteIdsAsync()
        {
            await EnsureInitializedAsync();
            return _pinnedNoteIds.ToList();
        }
        
        public async Task UpdateFilePathAsync(string noteId, string newPath)
        {
            if (string.IsNullOrEmpty(noteId) || string.IsNullOrEmpty(newPath)) return;
            
            await EnsureInitializedAsync();
            
            await _lock.WaitAsync();
            try
            {
                if (_pinEntries.TryGetValue(noteId, out var entry))
                {
                    entry.FilePath = newPath;
                    await SavePinsAsync();
                    _logger.Debug($"Updated pin path for {noteId}: {newPath}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to update file path for pin: {noteId}");
            }
            finally
            {
                _lock.Release();
            }
        }
        
        private async Task LoadPinsAsync()
        {
            try
            {
                if (!await _fileSystem.ExistsAsync(_pinsFilePath))
                {
                    _pinnedNoteIds.Clear();
                    _pinEntries.Clear();
                    return;
                }
                
                var json = await _fileSystem.ReadTextAsync(_pinsFilePath);
                var entries = JsonSerializer.Deserialize<List<PinEntry>>(json) ?? new List<PinEntry>();
                
                // Rebuild collections
                _pinnedNoteIds.Clear();
                _pinEntries.Clear();
                
                foreach (var entry in entries)
                {
                    if (!string.IsNullOrEmpty(entry.NoteId))
                    {
                        _pinnedNoteIds.Add(entry.NoteId);
                        _pinEntries[entry.NoteId] = entry;
                    }
                }
                
                _logger.Debug($"Loaded {_pinnedNoteIds.Count} pinned items from {_pinsFilePath}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load pins, attempting backup restore");
                
                // Try to restore from backup
                if (await RestoreFromBackupAsync())
                {
                    await LoadPinsAsync(); // Recursive call after restore
                }
                else
                {
                    // Initialize empty on total failure
                    _pinnedNoteIds.Clear();
                    _pinEntries.Clear();
                }
            }
        }
        
        private async Task SavePinsAsync()
        {
            try
            {
                // Create backup first
                await CreateBackupAsync();
                
                // Ensure directory exists
                var directory = Path.GetDirectoryName(_pinsFilePath);
                if (!string.IsNullOrEmpty(directory) && !await _fileSystem.ExistsAsync(directory))
                {
                    await _fileSystem.CreateDirectoryAsync(directory);
                }
                
                // Convert to list for serialization
                var entries = _pinEntries.Values.OrderBy(e => e.PinnedAt).ToList();
                var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                
                // Write to file
                await _fileSystem.WriteTextAsync(_pinsFilePath, json);
                
                _logger.Debug($"Saved {entries.Count} pins to {_pinsFilePath}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save pins");
                throw;
            }
        }
        
        private async Task CreateBackupAsync()
        {
            try
            {
                if (await _fileSystem.ExistsAsync(_pinsFilePath))
                {
                    var content = await _fileSystem.ReadTextAsync(_pinsFilePath);
                    await _fileSystem.WriteTextAsync(_backupPath, content);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to create backup: {ex.Message}");
                // Don't throw - backup failure shouldn't stop the main operation
            }
        }
        
        private async Task<bool> RestoreFromBackupAsync()
        {
            try
            {
                if (await _fileSystem.ExistsAsync(_backupPath))
                {
                    var content = await _fileSystem.ReadTextAsync(_backupPath);
                    await _fileSystem.WriteTextAsync(_pinsFilePath, content);
                    _logger.Info("Restored pins from backup");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to restore from backup");
            }
            
            return false;
        }
        
        protected virtual void OnPinChanged(string noteId, string filePath, bool isPinned)
        {
            try
            {
                PinChanged?.Invoke(this, new PinChangedEventArgs(noteId, filePath, isPinned));
            }
            catch (Exception ex)
            {
                _logger.Warning($"Error in PinChanged event handler: {ex.Message}");
            }
        }
        
        public void Dispose()
        {
            _lock?.Dispose();
        }
    }
}
