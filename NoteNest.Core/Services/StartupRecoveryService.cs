using System;
using System.IO;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    public class StartupRecoveryService
    {
        private readonly ISaveManager _saveManager;
        private readonly IAppLogger _logger;

        public StartupRecoveryService(ISaveManager saveManager, IAppLogger logger)
        {
            _saveManager = saveManager;
            _logger = logger;
        }

        public async Task<int> RecoverInterruptedSavesAsync(string notesPath)
        {
            int recovered = 0;

            // Check for .tmp files (interrupted saves)
            var tmpFiles = Directory.GetFiles(notesPath, "*.tmp", SearchOption.AllDirectories);
            foreach (var tmpFile in tmpFiles)
            {
                try
                {
                    var originalFile = tmpFile.Replace(".tmp", "");
                    File.Move(tmpFile, originalFile, overwrite: true);
                    recovered++;
                    _logger.Info($"Recovered interrupted save: {originalFile}");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to recover: {tmpFile}");
                }
            }

            // Check for emergency files on desktop
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var emergencyFiles = Directory.GetFiles(desktop, "NoteNest_Recovery_*.txt");
            
            foreach (var emergencyFile in emergencyFiles)
            {
                try
                {
                    // Extract note ID from filename
                    var fileName = Path.GetFileNameWithoutExtension(emergencyFile);
                    var noteId = fileName.Replace("NoteNest_Recovery_", "");
                    
                    // Prompt user to recover
                    _logger.Info($"Found emergency recovery file: {emergencyFile}");
                    // Note: Add UI prompt here if needed
                    
                    recovered++;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed to process emergency file: {emergencyFile}");
                }
            }

            return recovered;
        }
    }
}
