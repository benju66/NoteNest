using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;
using NoteNest.Core.Services;
using NoteNest.UI.Controls.Editor.RTF;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// UI wrapper for RTFIntegratedSaveEngine that handles RTF extraction and validation
    /// Bridges between UI RTF operations and Core save engine
    /// </summary>
    public class RTFSaveEngineWrapper
    {
        private readonly RTFIntegratedSaveEngine _coreEngine;

        public RTFSaveEngineWrapper(RTFIntegratedSaveEngine coreEngine)
        {
            _coreEngine = coreEngine ?? throw new ArgumentNullException(nameof(coreEngine));
        }

        /// <summary>
        /// Save from RichTextBox with RTF processing
        /// </summary>
        public async Task<SaveResult> SaveFromRichTextBoxAsync(
            string noteId,
            RichTextBox editor,
            string title = null,
            SaveType saveType = SaveType.Manual)
        {
            if (string.IsNullOrEmpty(noteId) || editor == null)
                return new SaveResult { Success = false, Error = "Invalid note ID or editor" };

            try
            {
                // 1. Extract RTF content using existing operations
                string rtfContent = RTFOperations.SaveToRTF(editor);
                
                if (string.IsNullOrEmpty(rtfContent))
                {
                    return new SaveResult { Success = false, Error = "Failed to extract RTF content" };
                }

                // 2. Validate RTF content using existing security methods
                if (!RTFOperations.IsValidRTFPublic(rtfContent))
                {
                    return new SaveResult { Success = false, Error = "Invalid RTF content" };
                }

                // 3. Sanitize RTF content using existing security methods  
                rtfContent = RTFOperations.SanitizeRTFContentPublic(rtfContent);

                // 4. Call core save engine with processed content
                var result = await _coreEngine.SaveRTFContentAsync(noteId, rtfContent, title, saveType);

                // 5. Memory management for large RTF documents
                if (result.Success && editor?.Document != null)
                {
                    var docSize = RTFOperations.EstimateDocumentSizePublic(editor.Document);
                    if (docSize > 50 * 1024) // >50KB documents
                    {
                        GC.Collect(0, GCCollectionMode.Optimized);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                return new SaveResult 
                { 
                    Success = false, 
                    Error = $"RTF processing failed: {ex.Message}" 
                };
            }
        }

        /// <summary>
        /// Load into RichTextBox with RTF processing
        /// </summary>
        public async Task<LoadResult> LoadToRichTextBoxAsync(string noteId, RichTextBox editor)
        {
            if (string.IsNullOrEmpty(noteId) || editor == null)
                return new LoadResult { Success = false, Error = "Invalid note ID or editor" };

            try
            {
                // 1. Load content using core engine
                var result = await _coreEngine.LoadRTFContentAsync(noteId);
                
                if (!result.Success || string.IsNullOrEmpty(result.Content))
                {
                    return result;
                }

                // 2. Load into RichTextBox using existing RTF operations
                RTFOperations.LoadFromRTF(editor, result.Content);

                return result;
            }
            catch (Exception ex)
            {
                return new LoadResult 
                { 
                    Success = false, 
                    Error = $"RTF loading failed: {ex.Message}" 
                };
            }
        }

        /// <summary>
        /// Check if auto-save should be throttled
        /// </summary>
        public bool ShouldThrottleAutoSave(string noteId, int minimumSeconds = 5)
        {
            return _coreEngine.ShouldThrottleAutoSave(noteId, minimumSeconds);
        }

        /// <summary>
        /// Get save metrics
        /// </summary>
        public SaveMetrics GetMetrics()
        {
            return _coreEngine.GetMetrics();
        }

        /// <summary>
        /// Recover from WAL
        /// </summary>
        public async Task<Dictionary<string, string>> RecoverFromWAL()
        {
            return await _coreEngine.RecoverFromWAL();
        }
    }
}
