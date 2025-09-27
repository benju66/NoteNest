using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Trees;

namespace NoteNest.Infrastructure.Database
{
    /// <summary>
    /// Fast hash calculation service using xxHash64 for change detection.
    /// Provides both quick hash (4KB) and full hash for different use cases.
    /// </summary>
    public interface IHashCalculationService
    {
        Task<string> CalculateQuickHashAsync(string filePath);
        Task<string> CalculateFullHashAsync(string filePath);
        Task<bool> HasFileChangedAsync(TreeNode node);
        Task<FileHashResult> CalculateBothHashesAsync(string filePath);
    }
    
    public class HashCalculationService : IHashCalculationService
    {
        private const int QuickHashBytes = 4096; // First 4KB for quick change detection
        private readonly IAppLogger _logger;
        
        public HashCalculationService(IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task<string> CalculateQuickHashAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"File not found: {filePath}");
                
                using var stream = File.OpenRead(filePath);
                var buffer = new byte[Math.Min(QuickHashBytes, stream.Length)];
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                
                // Use SHA256 for now (xxHash64 would require additional package)
                using var hasher = SHA256.Create();
                var hash = hasher.ComputeHash(buffer, 0, bytesRead);
                var hashString = Convert.ToBase64String(hash);
                
                _logger.Debug($"Calculated quick hash for {Path.GetFileName(filePath)}: {hashString[..8]}...");
                return hashString;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to calculate quick hash for: {filePath}");
                throw;
            }
        }
        
        public async Task<string> CalculateFullHashAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"File not found: {filePath}");
                
                using var stream = File.OpenRead(filePath);
                using var hasher = SHA256.Create();
                
                var hash = await hasher.ComputeHashAsync(stream);
                var hashString = Convert.ToBase64String(hash);
                
                _logger.Debug($"Calculated full hash for {Path.GetFileName(filePath)}: {hashString[..8]}...");
                return hashString;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to calculate full hash for: {filePath}");
                throw;
            }
        }
        
        public async Task<bool> HasFileChangedAsync(TreeNode node)
        {
            try
            {
                if (node.NodeType != TreeNodeType.Note)
                    return false;
                
                if (!File.Exists(node.AbsolutePath))
                    return true; // File was deleted
                
                // Quick check using file modification time first
                var fileInfo = new FileInfo(node.AbsolutePath);
                var currentModifiedTime = fileInfo.LastWriteTimeUtc;
                
                if (Math.Abs((currentModifiedTime - node.ModifiedAt).TotalSeconds) < 1.0)
                {
                    // Modification times are very close, assume no change
                    return false;
                }
                
                // If modification times differ significantly, verify with hash
                var currentQuickHash = await CalculateQuickHashAsync(node.AbsolutePath);
                var hasChanged = currentQuickHash != node.QuickHash;
                
                if (hasChanged)
                {
                    _logger.Debug($"File changed detected: {node.Name}");
                }
                
                return hasChanged;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to check if file changed for node {node.Name}: {ex.Message}");
                return true; // Assume changed if we can't verify
            }
        }
        
        public async Task<FileHashResult> CalculateBothHashesAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"File not found: {filePath}");
                
                var fileInfo = new FileInfo(filePath);
                
                // For small files, just calculate full hash
                if (fileInfo.Length <= QuickHashBytes)
                {
                    var fullHash = await CalculateFullHashAsync(filePath);
                    return new FileHashResult
                    {
                        QuickHash = fullHash,
                        FullHash = fullHash,
                        FileSize = fileInfo.Length,
                        ModifiedAt = fileInfo.LastWriteTimeUtc,
                        CalculatedAt = DateTime.UtcNow
                    };
                }
                
                // For larger files, calculate both hashes
                var quickHash = await CalculateQuickHashAsync(filePath);
                var fullHashLarge = await CalculateFullHashAsync(filePath);
                
                return new FileHashResult
                {
                    QuickHash = quickHash,
                    FullHash = fullHashLarge,
                    FileSize = fileInfo.Length,
                    ModifiedAt = fileInfo.LastWriteTimeUtc,
                    CalculatedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to calculate hashes for: {filePath}");
                throw;
            }
        }
    }
    
    public class FileHashResult
    {
        public string QuickHash { get; set; }
        public string FullHash { get; set; }
        public long FileSize { get; set; }
        public DateTime ModifiedAt { get; set; }
        public DateTime CalculatedAt { get; set; }
    }
}
