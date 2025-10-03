using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Handles persistence of workspace state (tabs and panes) to JSON file
    /// Part of NEW clean architecture (Milestone 2A completion)
    /// </summary>
    public interface IWorkspacePersistenceService
    {
        Task<WorkspaceState?> LoadAsync();
        Task SaveAsync(WorkspaceState state);
    }
    
    public class WorkspacePersistenceService : IWorkspacePersistenceService
    {
        private readonly IAppLogger _logger;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
        private readonly string _stateFilePath;
        
        public WorkspacePersistenceService(IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _jsonOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
            
            // Store in %LocalAppData%\NoteNest\workspace.json (consistent with app pattern)
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var noteNestDir = Path.Combine(localAppData, "NoteNest");
            _stateFilePath = Path.Combine(noteNestDir, "workspace.json");
            
            // Ensure directory exists
            var dir = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            _logger.Debug($"[WorkspacePersistence] State file: {_stateFilePath}");
        }
        
        /// <summary>
        /// Load workspace state from JSON file
        /// </summary>
        public async Task<WorkspaceState?> LoadAsync()
        {
            try
            {
                if (!File.Exists(_stateFilePath))
                {
                    _logger.Info("[WorkspacePersistence] No saved state found (first run or state deleted)");
                    return null;
                }
                
                var json = await File.ReadAllTextAsync(_stateFilePath);
                var state = JsonSerializer.Deserialize<WorkspaceState>(json, _jsonOptions);
                
                if (state == null)
                {
                    _logger.Warning("[WorkspacePersistence] State file was empty or invalid");
                    return null;
                }
                
                _logger.Info($"[WorkspacePersistence] Loaded state: {state.PaneCount} pane(s), " +
                           $"{state.Panes.Count} pane state(s), saved {state.LastSaved:yyyy-MM-dd HH:mm:ss}");
                
                return state;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[WorkspacePersistence] Failed to load workspace state");
                return null; // Don't fail app startup, just start with empty workspace
            }
        }
        
        /// <summary>
        /// Save workspace state to JSON file (atomic write with lock)
        /// </summary>
        public async Task SaveAsync(WorkspaceState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            
            await _saveLock.WaitAsync();
            try
            {
                state.LastSaved = DateTime.UtcNow;
                
                var json = JsonSerializer.Serialize(state, _jsonOptions);
                
                // Atomic write: write to temp file, then move
                var tempFile = _stateFilePath + ".tmp";
                await File.WriteAllTextAsync(tempFile, json);
                File.Move(tempFile, _stateFilePath, overwrite: true);
                
                _logger.Debug($"[WorkspacePersistence] Saved state: {state.PaneCount} pane(s), " +
                            $"{state.Panes.Sum(p => p.Tabs.Count)} total tabs");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[WorkspacePersistence] Failed to save workspace state");
                throw; // Propagate so caller knows save failed
            }
            finally
            {
                _saveLock.Release();
            }
        }
    }
}

