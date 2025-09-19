using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Transaction
{
    /// <summary>
    /// Transaction step that saves all dirty notes before storage location change
    /// Ensures no unsaved changes are lost during the transition
    /// </summary>
    public class SaveAllDirtyStep : TransactionStepBase
    {
        private readonly ISaveManager _saveManager;
        private readonly List<string> _savedNoteIds = new();
        private Dictionary<string, string> _presaveContents = new();

        public override string Description => "Save all dirty notes before location change";
        public override bool CanRollback => false; // Saving can't be undone, but that's okay

        public SaveAllDirtyStep(ISaveManager saveManager, IAppLogger logger) : base(logger)
        {
            _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
        }

        protected override async Task<TransactionStepResult> ExecuteStepAsync()
        {
            try
            {
                // Get all dirty notes
                var dirtyNoteIds = _saveManager.GetDirtyNoteIds();
                
                if (dirtyNoteIds.Count == 0)
                {
                    _logger.Debug("No dirty notes to save");
                    return TransactionStepResult.Succeeded();
                }

                _logger.Info($"Saving {dirtyNoteIds.Count} dirty notes before storage location change");

                // Capture content for logging purposes (not for rollback)
                foreach (var noteId in dirtyNoteIds)
                {
                    _presaveContents[noteId] = _saveManager.GetContent(noteId);
                }

                // Save all dirty notes
                var saveResult = await _saveManager.SaveAllDirtyAsync();
                
                if (saveResult.FailureCount > 0)
                {
                    var totalCount = saveResult.SuccessCount + saveResult.FailureCount;
                    var errorMsg = $"Failed to save {saveResult.FailureCount} out of {totalCount} notes. " +
                                  $"Failed notes: {string.Join(", ", saveResult.FailedNoteIds)}";
                    
                    return TransactionStepResult.Failed(errorMsg);
                }

                // Track what we saved
                _savedNoteIds.AddRange(dirtyNoteIds);

                _logger.Info($"Successfully saved {saveResult.SuccessCount} notes");
                return TransactionStepResult.Succeeded(new { SavedCount = saveResult.SuccessCount });
            }
            catch (Exception ex)
            {
                return TransactionStepResult.Failed($"Exception while saving dirty notes: {ex.Message}", ex);
            }
        }

        protected override async Task<TransactionStepResult> RollbackStepAsync()
        {
            // Note: We can't really "unsave" files, but we can log what happened
            // This step is designed to be non-rollbackable since saving is a good thing
            _logger.Warning($"SaveAllDirtyStep rollback requested - saved {_savedNoteIds.Count} notes cannot be unsaved");
            
            await Task.CompletedTask;
            return TransactionStepResult.Succeeded();
        }
    }
}
