using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using NoteNest.UI.Controls.Editor.Core;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Controls
{
    /// <summary>
    /// OPTION B: Custom UserControl for complete editor isolation
    /// Each tab gets its own instance - no cross-contamination possible
    /// </summary>
    public partial class NoteEditorContainer : UserControl
    {
        private bool _isLoading;
        private NoteTabItem _currentTabItem;

        public NoteEditorContainer()
        {
            InitializeComponent();
            
            // Wire up data context change detection
            DataContextChanged += OnDataContextChanged;
        }

        /// <summary>
        /// Handle DataContext changes (when tab switches or new tab loads)
        /// </summary>
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // ZERO-RISK IMPROVEMENT: Always clean up old connection first
            if (_currentTabItem != null)
            {
                Editor.TextChanged -= OnEditorTextChanged;
                _currentTabItem = null;
            }

            if (e.NewValue is NoteTabItem newTabItem)
            {
                _currentTabItem = newTabItem;
                
                // Load content for this tab
                LoadTabContent();
                
                // Wire up change notifications for this tab
                Editor.TextChanged += OnEditorTextChanged;
                
                System.Diagnostics.Debug.WriteLine($"[CONTAINER] Bound to tab: {newTabItem.Title}");
            }
            else if (e.NewValue == null)
            {
                // ZERO-RISK IMPROVEMENT: Handle null DataContext gracefully
                System.Diagnostics.Debug.WriteLine($"[CONTAINER] DataContext cleared (null)");
            }
            else
            {
                // ZERO-RISK IMPROVEMENT: Handle unexpected DataContext types
                System.Diagnostics.Debug.WriteLine($"[CONTAINER] Unexpected DataContext type: {e.NewValue?.GetType().Name}");
            }
        }

        /// <summary>
        /// Load content into this editor instance (complete isolation)
        /// </summary>
        private void LoadTabContent()
        {
            if (_currentTabItem == null) return;

            _isLoading = true;
            try
            {
                var content = _currentTabItem.Content ?? string.Empty;
                
                // COMPLETE ISOLATION: Each container loads its own content
                Editor.Document.Blocks.Clear();
                Editor.LoadFromMarkdown(content);
                Editor.MarkClean();
                
                System.Diagnostics.Debug.WriteLine($"[CONTAINER] Loaded {content.Length} chars for: {_currentTabItem.Title}");
            }
            catch (Exception ex)
            {
                // ZERO-RISK IMPROVEMENT: Enhanced error logging for diagnostics
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to load content for {_currentTabItem?.Title}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// Handle editor changes and notify the tab's save system
        /// </summary>
        private void OnEditorTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // Don't process changes during loading
            if (_isLoading || _currentTabItem == null) return;

            try
            {
                // Get current content and notify the tab
                var content = Editor.SaveToMarkdown();
                _currentTabItem.UpdateContentFromEditor(content);
                
                // Trigger the tab's save coordination system
                _currentTabItem.NotifyContentChanged();
                
                System.Diagnostics.Debug.WriteLine($"[CONTAINER] Content changed in: {_currentTabItem.Title} ({content.Length} chars)");
            }
            catch (Exception ex)
            {
                // ZERO-RISK IMPROVEMENT: Enhanced error logging for diagnostics
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to handle content change for {_currentTabItem?.Title}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Editor loaded - ready for use
        /// </summary>
        private void Editor_Loaded(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[CONTAINER] Editor loaded");
            
            // Load content if we have a tab
            if (_currentTabItem != null)
            {
                LoadTabContent();
            }
        }

        /// <summary>
        /// Clean up when editor is unloaded
        /// </summary>
        private void Editor_Unloaded(object sender, RoutedEventArgs e)
        {
            // ZERO-RISK IMPROVEMENT: Fix memory leak by removing DataContext event handler
            DataContextChanged -= OnDataContextChanged;
            Editor.TextChanged -= OnEditorTextChanged;
            _currentTabItem = null;
            
            System.Diagnostics.Debug.WriteLine($"[CONTAINER] Editor unloaded and cleaned up (memory leak fixed)");
        }

        /// <summary>
        /// Public property to access the underlying editor (for advanced scenarios)
        /// </summary>
        public FormattedTextEditor UnderlyingEditor => Editor;
    }
}
