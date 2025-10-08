using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Application.Plugins.Interfaces;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Services.Safety;
using NoteNest.Domain.Common;
using NoteNest.Domain.Plugins;

namespace NoteNest.Infrastructure.Plugins
{
    /// <summary>
    /// File-based plugin data store with isolation and security.
    /// Each plugin gets its own directory for data storage.
    /// </summary>
    public class PluginDataStore : IPluginDataStore
    {
        private readonly string _pluginDataRoot;
        private readonly SafeFileService _fileService;
        private readonly IAppLogger _logger;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _pluginLocks;
        private readonly JsonSerializerOptions _jsonOptions;

        public PluginDataStore(SafeFileService fileService, IAppLogger logger)
        {
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Plugin data stored in isolated directory
            _pluginDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NoteNest", ".plugins");
            
            _pluginLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            EnsureSecureDirectory();
        }

        private void EnsureSecureDirectory()
        {
            if (!Directory.Exists(_pluginDataRoot))
            {
                Directory.CreateDirectory(_pluginDataRoot);
                
                // Hide .plugins directory on Windows
                if (OperatingSystem.IsWindows())
                {
                    var dirInfo = new DirectoryInfo(_pluginDataRoot);
                    dirInfo.Attributes |= FileAttributes.Hidden;
                }
            }
        }

        private string GetPluginDirectory(PluginId pluginId)
        {
            var pluginDir = Path.Combine(_pluginDataRoot, pluginId.Value);
            
            if (!Directory.Exists(pluginDir))
            {
                Directory.CreateDirectory(pluginDir);
            }
            
            return pluginDir;
        }

        private string GetDataFilePath(PluginId pluginId, string key)
        {
            var sanitizedKey = SanitizeKey(key);
            var pluginDir = GetPluginDirectory(pluginId);
            return Path.Combine(pluginDir, $"{sanitizedKey}.json");
        }

        private string SanitizeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key cannot be empty", nameof(key));

            // Remove invalid file name characters
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(key.Where(c => !invalidChars.Contains(c)).ToArray());
            
            if (sanitized.Length == 0)
                throw new ArgumentException("Key contains only invalid characters", nameof(key));

            return sanitized;
        }

        public async Task<Result<T>> LoadDataAsync<T>(PluginId pluginId, string key) where T : class
        {
            var lockObj = _pluginLocks.GetOrAdd(pluginId.Value, _ => new SemaphoreSlim(1));
            await lockObj.WaitAsync();
            
            try
            {
                var filePath = GetDataFilePath(pluginId, key);
                
                if (!File.Exists(filePath))
                {
                    _logger.Debug($"Plugin data file not found: {pluginId.Value}/{key}");
                    return Result.Ok<T>(null);
                }

                var json = await _fileService.ReadTextSafelyAsync(filePath);
                
                if (string.IsNullOrWhiteSpace(json))
                {
                    return Result.Ok<T>(null);
                }

                var data = JsonSerializer.Deserialize<T>(json, _jsonOptions);
                _logger.Debug($"Loaded plugin data: {pluginId.Value}/{key}");
                
                return Result.Ok(data);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to load plugin data: {pluginId.Value}/{key}");
                return Result.Fail<T>($"Failed to load plugin data: {ex.Message}");
            }
            finally
            {
                lockObj.Release();
            }
        }

        public async Task<Result> SaveDataAsync<T>(PluginId pluginId, string key, T data) where T : class
        {
            var lockObj = _pluginLocks.GetOrAdd(pluginId.Value, _ => new SemaphoreSlim(1));
            await lockObj.WaitAsync();
            
            try
            {
                var filePath = GetDataFilePath(pluginId, key);
                var json = JsonSerializer.Serialize(data, _jsonOptions);
                
                await _fileService.WriteTextSafelyAsync(filePath, json);
                _logger.Debug($"Saved plugin data: {pluginId.Value}/{key} ({json.Length} bytes)");
                
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to save plugin data: {pluginId.Value}/{key}");
                return Result.Fail($"Failed to save plugin data: {ex.Message}");
            }
            finally
            {
                lockObj.Release();
            }
        }

        public async Task<Result> DeleteDataAsync(PluginId pluginId, string key)
        {
            var lockObj = _pluginLocks.GetOrAdd(pluginId.Value, _ => new SemaphoreSlim(1));
            await lockObj.WaitAsync();
            
            try
            {
                var filePath = GetDataFilePath(pluginId, key);
                
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.Debug($"Deleted plugin data: {pluginId.Value}/{key}");
                }
                
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to delete plugin data: {pluginId.Value}/{key}");
                return Result.Fail($"Failed to delete plugin data: {ex.Message}");
            }
            finally
            {
                lockObj.Release();
            }
        }

        public async Task<Result<long>> GetStorageSizeAsync(PluginId pluginId)
        {
            try
            {
                var pluginDir = GetPluginDirectory(pluginId);
                
                if (!Directory.Exists(pluginDir))
                {
                    return Result.Ok(0L);
                }

                var files = Directory.GetFiles(pluginDir, "*.json", SearchOption.AllDirectories);
                long totalSize = files.Sum(f => new FileInfo(f).Length);
                
                return Result.Ok(totalSize);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to calculate storage size for plugin: {pluginId.Value}");
                return Result.Fail<long>($"Failed to calculate storage size: {ex.Message}");
            }
        }

        public async Task<Result> BackupPluginDataAsync(PluginId pluginId)
        {
            try
            {
                var sourceDir = GetPluginDirectory(pluginId);
                
                if (!Directory.Exists(sourceDir))
                {
                    return Result.Ok(); // Nothing to backup
                }

                var backupDir = Path.Combine(_pluginDataRoot, ".backups", pluginId.Value, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(backupDir);

                var files = Directory.GetFiles(sourceDir, "*.json");
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    var destFile = Path.Combine(backupDir, fileName);
                    
                    await using var src = File.OpenRead(file);
                    await using var dst = File.Create(destFile);
                    await src.CopyToAsync(dst);
                }

                _logger.Info($"Backed up plugin data: {pluginId.Value} ({files.Length} files)");
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to backup plugin data: {pluginId.Value}");
                return Result.Fail($"Failed to backup plugin data: {ex.Message}");
            }
        }
    }
}

