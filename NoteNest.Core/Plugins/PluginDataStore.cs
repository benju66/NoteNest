using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Services.Safety;
using NoteNest.Core.Services;

namespace NoteNest.Core.Plugins
{
	public interface IPluginDataStore
	{
		Task<T> LoadDataAsync<T>(string pluginId, string key) where T : class;
		Task SaveDataAsync<T>(string pluginId, string key, T data) where T : class;
		Task<Dictionary<string, object>> LoadSettingsAsync(string pluginId);
		Task SaveSettingsAsync(string pluginId, Dictionary<string, object> settings);
		Task<bool> DeleteDataAsync(string pluginId, string key);
		Task<bool> BackupPluginDataAsync(string pluginId);
		Task<bool> RestorePluginDataAsync(string pluginId, DateTime backupDate);
	}

	public class PluginDataStore : IPluginDataStore
	{
		private readonly string _pluginDataRoot;
		private readonly SafeFileService _fileService;
		private readonly IAppLogger _logger;
		private readonly JsonSerializerOptions _jsonOptions;

		public PluginDataStore(SafeFileService fileService, IAppLogger logger = null)
		{
			_fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
			_logger = logger ?? AppLogger.Instance;
			_pluginDataRoot = Path.Combine(PathService.RootPath, ".plugins");
			_jsonOptions = new JsonSerializerOptions
			{
				WriteIndented = true,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				Converters = { new JsonStringEnumConverter() }
			};
			EnsureDirectoryExists();
		}

		private void EnsureDirectoryExists()
		{
			if (!Directory.Exists(_pluginDataRoot))
			{
				Directory.CreateDirectory(_pluginDataRoot);
				if (OperatingSystem.IsWindows())
				{
					var dirInfo = new DirectoryInfo(_pluginDataRoot);
					dirInfo.Attributes |= FileAttributes.Hidden;
				}
			}
		}

		private string GetPluginDirectory(string pluginId)
		{
			var path = Path.Combine(_pluginDataRoot, pluginId);
			if (!Directory.Exists(path))
			{
				Directory.CreateDirectory(path);
			}
			return path;
		}

		private string GetDataFilePath(string pluginId, string key)
		{
			var pluginDir = GetPluginDirectory(pluginId);
			var fileName = $"{key}.json";
			return Path.Combine(pluginDir, fileName);
		}

		public async Task<T> LoadDataAsync<T>(string pluginId, string key) where T : class
		{
			try
			{
				var filePath = GetDataFilePath(pluginId, key);
				if (!File.Exists(filePath))
				{
					_logger?.Debug($"Data file not found: {pluginId}/{key}");
					return null;
				}

				var json = await _fileService.ReadTextSafelyAsync(filePath);
				if (string.IsNullOrEmpty(json)) return null;
				return JsonSerializer.Deserialize<T>(json, _jsonOptions);
			}
			catch (Exception ex)
			{
				_logger?.Error(ex, $"Failed to load plugin data: {pluginId}/{key}");
				return null;
			}
		}

		public async Task SaveDataAsync<T>(string pluginId, string key, T data) where T : class
		{
			try
			{
				var filePath = GetDataFilePath(pluginId, key);
				var json = JsonSerializer.Serialize(data, _jsonOptions);
				await _fileService.WriteTextSafelyAsync(filePath, json);
				_logger?.Debug($"Plugin data saved: {pluginId}/{key}");
			}
			catch (Exception ex)
			{
				_logger?.Error(ex, $"Failed to save plugin data: {pluginId}/{key}");
				throw;
			}
		}

		public async Task<Dictionary<string, object>> LoadSettingsAsync(string pluginId)
		{
			return await LoadDataAsync<Dictionary<string, object>>(pluginId, "settings");
		}

		public async Task SaveSettingsAsync(string pluginId, Dictionary<string, object> settings)
		{
			await SaveDataAsync(pluginId, "settings", settings);
		}

		public async Task<bool> DeleteDataAsync(string pluginId, string key)
		{
			try
			{
				var filePath = GetDataFilePath(pluginId, key);
				if (File.Exists(filePath))
				{
					File.Delete(filePath);
					_logger?.Debug($"Plugin data deleted: {pluginId}/{key}");
				}
				return await Task.FromResult(true);
			}
			catch (Exception ex)
			{
				_logger?.Error(ex, $"Failed to delete plugin data: {pluginId}/{key}");
				return false;
			}
		}

		public async Task<bool> BackupPluginDataAsync(string pluginId)
		{
			try
			{
				var sourceDir = GetPluginDirectory(pluginId);
				var backupDir = Path.Combine(_pluginDataRoot, ".backups", pluginId, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
				Directory.CreateDirectory(backupDir);
				foreach (var file in Directory.GetFiles(sourceDir))
				{
					var fileName = Path.GetFileName(file);
					var destFile = Path.Combine(backupDir, fileName);
					File.Copy(file, destFile);
				}
				_logger?.Info($"Plugin data backed up: {pluginId}");
				return true;
			}
			catch (Exception ex)
			{
				_logger?.Error(ex, $"Failed to backup plugin data: {pluginId}");
				return false;
			}
		}

		public async Task<bool> RestorePluginDataAsync(string pluginId, DateTime backupDate)
		{
			try
			{
				var backupDir = Path.Combine(_pluginDataRoot, ".backups", pluginId, backupDate.ToString("yyyyMMdd_HHmmss"));
				if (!Directory.Exists(backupDir))
				{
					_logger?.Warning($"Backup not found: {pluginId}/{backupDate}");
					return false;
				}
				var targetDir = GetPluginDirectory(pluginId);
				await BackupPluginDataAsync(pluginId);
				foreach (var file in Directory.GetFiles(targetDir))
				{
					File.Delete(file);
				}
				foreach (var file in Directory.GetFiles(backupDir))
				{
					var fileName = Path.GetFileName(file);
					var destFile = Path.Combine(targetDir, fileName);
					File.Copy(file, destFile);
				}
				_logger?.Info($"Plugin data restored: {pluginId} from {backupDate}");
				return true;
			}
			catch (Exception ex)
			{
				_logger?.Error(ex, $"Failed to restore plugin data: {pluginId}");
				return false;
			}
		}
	}
}


