using NoteNest.Core.Models;
using NoteNest.Core.Interfaces.Services;
using System;
using System.IO;

namespace NoteNest.UI.ViewModels
{
    public class NoteTabItem : ViewModelBase, ITabItem
    {
        private readonly NoteModel _note;
        private bool _isDirty;
        private string _content;
        private string _wordCount;
        private string _lastSaved;
        private bool _isRichViewEnabled;

        public NoteModel Note => _note;
        public string Id => _note?.Id ?? string.Empty;

        public string Title => _note.Title;

        public string Content
        {
            get
            {
                try
                {
                    var state = (System.Windows.Application.Current as UI.App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.IWorkspaceStateService)) as NoteNest.Core.Services.IWorkspaceStateService;
                    if (state != null && !string.IsNullOrEmpty(_note?.Id) && state.OpenNotes.TryGetValue(_note.Id, out var wn))
                    {
                        var value = wn.CurrentContent ?? _content ?? string.Empty;
                        return value;
                    }
                }
                catch { }
                return _content ?? _note?.Content ?? string.Empty;
            }
            set
            {
                var newValue = value ?? string.Empty;
                // Always keep local cache for UI binding responsiveness
                var changed = SetProperty(ref _content, newValue);
                if (!changed) return;

                // Push content changes to WorkspaceStateService (single source of truth)
                try
                {
                    var state = (System.Windows.Application.Current as UI.App)?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.IWorkspaceStateService)) as NoteNest.Core.Services.IWorkspaceStateService;
                    state?.UpdateNoteContent(_note.Id, newValue);
                    System.Diagnostics.Debug.WriteLine($"[Tab] Content set noteId={_note?.Id} len={newValue.Length} at={DateTime.Now:HH:mm:ss.fff}");
                    // Determine dirty by comparing with state's OriginalContent
                    if (state != null && state.OpenNotes.TryGetValue(_note.Id, out var wn))
                    {
                        IsDirty = !string.Equals(wn.OriginalContent ?? string.Empty, newValue ?? string.Empty, StringComparison.Ordinal);
                    }
                    else
                    {
                        IsDirty = true;
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Tab][ERROR] Content set failed: {ex.Message}"); }
                UpdateWordCount();
            }
        }

        public bool IsRichViewEnabled
        {
            get => _isRichViewEnabled;
            set
            {
                if (SetProperty(ref _isRichViewEnabled, value))
                {
                    try
                    {
                        var mode = value ? NoteNest.UI.Interfaces.EditorViewMode.RichText : NoteNest.UI.Interfaces.EditorViewMode.PlainText;
                        NoteNest.UI.Services.EditorViewModeStore.SetForNote(Id, mode);
                        var app = System.Windows.Application.Current as UI.App;
                        var config = app?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.ConfigurationService)) as NoteNest.Core.Services.ConfigurationService;
                        if (config?.Settings != null)
                        {
                            var persisted = value ? "RichText" : "PlainText";
                            // Write under both transient Id and stable FilePath for compatibility
                            if (!string.IsNullOrEmpty(Id))
                            {
                                config.Settings.LastEditorViewModeByNoteId[Id] = persisted;
                            }
                            var originalPathKey = _note?.FilePath;
                            var normalizedPathKey = NormalizePath(originalPathKey);
                            if (!string.IsNullOrWhiteSpace(originalPathKey))
                            {
                                config.Settings.LastEditorViewModeByNoteId[originalPathKey] = persisted;
                            }
                            if (!string.IsNullOrWhiteSpace(normalizedPathKey))
                            {
                                config.Settings.LastEditorViewModeByNoteId[normalizedPathKey] = persisted;
                            }
                            try { config.RequestSaveDebounced(250); } catch { }
                            // Also force an immediate save to avoid losing state on quick exit
                            try { _ = config.FlushPendingAsync(); } catch { }
                        }
                    }
                    catch { }
                }
            }
        }

        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    OnPropertyChanged(nameof(Title));
                    UpdateLastSaved();
                }
            }
        }

        public string WordCount
        {
            get => _wordCount ?? "0 words";
            set => SetProperty(ref _wordCount, value);
        }

        public string LastSaved
        {
            get => _lastSaved ?? "Not saved";
            set => SetProperty(ref _lastSaved, value);
        }

        public NoteTabItem(NoteModel note)
        {
            _note = note;
            _content = note.Content;
            _isDirty = false;
            UpdateWordCount();
            UpdateLastSaved();

            // Initialize IsRichViewEnabled from store/settings
            try
            {
                var mode = NoteNest.UI.Services.EditorViewModeStore.GetForNote(Id, NoteNest.UI.Interfaces.EditorViewMode.PlainText);
                var app = System.Windows.Application.Current as UI.App;
                var config = app?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.ConfigurationService)) as NoteNest.Core.Services.ConfigurationService;
                var s = config?.Settings;
                if (s != null && s.LastEditorViewModeByNoteId != null && s.LastEditorViewModeByNoteId.TryGetValue(Id, out var stored))
                {
                    mode = string.Equals(stored, "RichText", StringComparison.OrdinalIgnoreCase) ? NoteNest.UI.Interfaces.EditorViewMode.RichText : NoteNest.UI.Interfaces.EditorViewMode.PlainText;
                }
                else
                {
                    var originalPathKey = _note?.FilePath;
                    var normalizedPathKey = NormalizePath(originalPathKey);
                    if (!string.IsNullOrWhiteSpace(originalPathKey) && s != null && s.LastEditorViewModeByNoteId != null && s.LastEditorViewModeByNoteId.TryGetValue(originalPathKey, out var storedByPath))
                    {
                        mode = string.Equals(storedByPath, "RichText", StringComparison.OrdinalIgnoreCase) ? NoteNest.UI.Interfaces.EditorViewMode.RichText : NoteNest.UI.Interfaces.EditorViewMode.PlainText;
                    }
                    else if (!string.IsNullOrWhiteSpace(normalizedPathKey) && s != null && s.LastEditorViewModeByNoteId != null && s.LastEditorViewModeByNoteId.TryGetValue(normalizedPathKey, out var storedByNormPath))
                    {
                        mode = string.Equals(storedByNormPath, "RichText", StringComparison.OrdinalIgnoreCase) ? NoteNest.UI.Interfaces.EditorViewMode.RichText : NoteNest.UI.Interfaces.EditorViewMode.PlainText;
                    }
                }
                _isRichViewEnabled = mode == NoteNest.UI.Interfaces.EditorViewMode.RichText;
            }
            catch { _isRichViewEnabled = false; }
        }

        private static string NormalizePath(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return string.Empty;
                var full = Path.GetFullPath(path);
                // Remove trailing separators and normalize case for consistent dictionary keys on Windows
                return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
            }
            catch { return path ?? string.Empty; }
        }

        public new void OnPropertyChanged(string propertyName)
        {
            base.OnPropertyChanged(propertyName);
        }

        public void UpdateWordCount()
        {
            if (string.IsNullOrEmpty(Content))
            {
                WordCount = "0 words";
            }
            else
            {
                var words = Content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                WordCount = $"{words} words";
            }
        }

        public void UpdateLastSaved()
        {
            LastSaved = IsDirty ? "Unsaved changes" : $"Saved {DateTime.Now:HH:mm}";
        }
    }
}
