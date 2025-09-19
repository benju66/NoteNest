using System;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Transaction
{
    /// <summary>
    /// Transaction step that atomically replaces the current SaveManager with a new one
    /// Handles event subscriptions and ensures no data is lost during the transition
    /// </summary>
    public class ReplaceSaveManagerStep : TransactionStepBase
    {
        private readonly ISaveManagerFactory _saveManagerFactory;
        private readonly ISaveManager _newSaveManager;
        private SaveManagerState _capturedState;
        private ISaveManager _oldSaveManager;

        public override string Description => "Replace SaveManager with new instance";
        public override bool CanRollback => true;

        public ReplaceSaveManagerStep(
            ISaveManagerFactory saveManagerFactory,
            ISaveManager newSaveManager,
            IAppLogger logger) : base(logger)
        {
            _saveManagerFactory = saveManagerFactory ?? throw new ArgumentNullException(nameof(saveManagerFactory));
            _newSaveManager = newSaveManager ?? throw new ArgumentNullException(nameof(newSaveManager));
        }

        protected override async Task<TransactionStepResult> ExecuteStepAsync()
        {
            try
            {
                _logger.Info("Replacing SaveManager with new instance");

                // Capture current SaveManager and its state for rollback
                _oldSaveManager = _saveManagerFactory.Current;
                _capturedState = await _saveManagerFactory.CaptureStateAsync();

                // Perform the atomic replacement
                await _saveManagerFactory.ReplaceSaveManagerAsync(_newSaveManager);

                // Verify the replacement was successful
                if (_saveManagerFactory.Current != _newSaveManager)
                {
                    return TransactionStepResult.Failed("SaveManager replacement failed - factory still returns old instance");
                }

                // Test the new SaveManager is functioning
                var testResult = await TestNewSaveManagerAsync(_newSaveManager);
                if (!testResult.Success)
                {
                    return testResult;
                }

                _logger.Info("SaveManager replacement completed successfully");
                return TransactionStepResult.Succeeded(new { OldSaveManager = _oldSaveManager, NewSaveManager = _newSaveManager });
            }
            catch (Exception ex)
            {
                return TransactionStepResult.Failed($"Exception during SaveManager replacement: {ex.Message}", ex);
            }
        }

        protected override async Task<TransactionStepResult> RollbackStepAsync()
        {
            try
            {
                if (_capturedState == null)
                {
                    return TransactionStepResult.Failed("No captured state available for rollback");
                }

                _logger.Info("Rolling back SaveManager replacement");

                // Restore the original SaveManager state
                await _saveManagerFactory.RestoreStateAsync(_capturedState);

                // Verify rollback was successful
                if (_saveManagerFactory.Current == _newSaveManager)
                {
                    return TransactionStepResult.Failed("SaveManager rollback failed - factory still returns new instance");
                }

                _logger.Info("SaveManager replacement rollback completed successfully");
                return TransactionStepResult.Succeeded();
            }
            catch (Exception ex)
            {
                return TransactionStepResult.Failed($"Exception during SaveManager replacement rollback: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Test that the new SaveManager is functioning correctly
        /// </summary>
        private async Task<TransactionStepResult> TestNewSaveManagerAsync(ISaveManager saveManager)
        {
            try
            {
                // Test basic operations
                var dirtyNotes = saveManager.GetDirtyNoteIds();
                if (dirtyNotes == null)
                {
                    return TransactionStepResult.Failed("New SaveManager returned null for GetDirtyNoteIds()");
                }

                // Test that events are properly wired (this would be expanded with actual event testing)
                // For now, we'll just verify the SaveManager responds to method calls

                _logger.Debug($"New SaveManager functionality test passed - {dirtyNotes.Count} dirty notes found");
                await Task.CompletedTask;
                return TransactionStepResult.Succeeded();
            }
            catch (Exception ex)
            {
                return TransactionStepResult.Failed($"New SaveManager functionality test failed: {ex.Message}", ex);
            }
        }
    }
}
