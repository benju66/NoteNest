using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Safety
{
	public class SafeFileService : IDisposable
	{
		private readonly IUserNotificationService? _notifications;
		private readonly IAppLogger? _logger;
		private readonly Dictionary<string, SemaphoreSlim> _perFileLocks = new();
		private readonly object _locksLock = new object();
		private bool _disposed = false;

		public SafeFileService(IUserNotificationService? notifications = null, IAppLogger? logger = null)
		{
			_notifications = notifications;
			_logger = logger ?? AppLogger.Instance;
		}

		public async Task<T> ExecuteWithLockAsync<T>(string filePath, Func<Task<T>> operation, int maxRetries = 3)
		{
			var semaphore = GetOrCreateFileLock(filePath);
			for (int attempt = 0; attempt < maxRetries; attempt++)
			{
				try
				{
					await semaphore.WaitAsync(TimeSpan.FromSeconds(5));
					try
					{
						return await operation();
					}
					finally
					{
						semaphore.Release();
					}
				}
				catch (IOException ioEx) when (attempt < maxRetries - 1)
				{
					_logger?.Warning($"File operation failed (attempt {attempt + 1}): {ioEx.Message}");
					await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)));
				}
			}

			throw new IOException($"Failed to access file after {maxRetries} attempts: {filePath}");
		}

		public async Task<bool> TryExecuteAsync(string filePath, Func<Task> operation, bool notifyOnError = true)
		{
			try
			{
				await ExecuteWithLockAsync(filePath, async () => { await operation(); return true; });
				return true;
			}
			catch (Exception ex)
			{
				_logger?.Error(ex, $"Failed to execute operation on: {filePath}");
				if (notifyOnError && _notifications != null)
				{
					await _notifications.ShowErrorAsync($"Failed to access file: {Path.GetFileName(filePath)}", ex);
				}
				return false;
			}
		}

		public async Task<string> ReadTextSafelyAsync(string filePath)
		{
			return await ExecuteWithLockAsync(filePath, async () =>
			{
				using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				using var reader = new StreamReader(stream);
				return await reader.ReadToEndAsync();
			});
		}

		public async Task WriteTextSafelyAsync(string filePath, string content)
		{
			await ExecuteWithLockAsync(filePath, async () =>
			{
				var tempPath = filePath + ".tmp";
				await File.WriteAllTextAsync(tempPath, content ?? string.Empty);
				File.Move(tempPath, filePath, true);
				return true;
			});
		}

		private SemaphoreSlim GetOrCreateFileLock(string filePath)
		{
			if (_disposed)
				throw new ObjectDisposedException(nameof(SafeFileService));
				
			var normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();
			lock (_locksLock)
			{
				if (!_perFileLocks.TryGetValue(normalizedPath, out var semaphore))
				{
					semaphore = new SemaphoreSlim(1, 1);
					_perFileLocks[normalizedPath] = semaphore;
				}
				return semaphore;
			}
		}

		public void Dispose()
		{
			if (_disposed) return;

			try
			{
				_logger?.Debug($"Disposing SafeFileService with {_perFileLocks.Count} file locks");

				lock (_locksLock)
				{
					// Dispose all semaphores
					foreach (var kvp in _perFileLocks)
					{
						try
						{
							kvp.Value?.Dispose();
						}
						catch (Exception ex)
						{
							_logger?.Warning($"Error disposing file lock for {kvp.Key}: {ex.Message}");
						}
					}

					_perFileLocks.Clear();
				}

				_disposed = true;
				_logger?.Info("SafeFileService disposed successfully");
			}
			catch (Exception ex)
			{
				_logger?.Error(ex, "Error disposing SafeFileService");
			}
		}
	}
}


