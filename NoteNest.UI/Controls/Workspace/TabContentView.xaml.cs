using System;
using System.Windows;
using System.Windows.Controls;
using NoteNest.UI.ViewModels.Workspace;

namespace NoteNest.UI.Controls.Workspace
{
    /// <summary>
    /// Code-behind for TabContentView - handles RTF editor UI concerns
    /// Bridges between TabViewModel (business logic) and RTFEditor (UI control)
    /// </summary>
    public partial class TabContentView : UserControl
    {
        private TabViewModel _viewModel;
        private bool _isLoading;
        
        public TabContentView()
        {
            InitializeComponent();
            
            // Wire up editor events
            Editor.TextChanged += OnEditorTextChanged;
            
            // Connect toolbar to editor
            Toolbar.TargetEditor = Editor;
            
            // Listen for DataContext changes
            DataContextChanged += OnDataContextChanged;
            
            // Listen for unload to clean up
            Unloaded += OnUnloaded;
            
            System.Diagnostics.Debug.WriteLine("[TabContentView] Initialized");
        }
        
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Clean up old ViewModel
            if (_viewModel != null)
            {
                _viewModel.LoadContentRequested -= LoadContentIntoEditor;
                _viewModel.SaveContentRequested -= SaveContentFromEditor;
                System.Diagnostics.Debug.WriteLine($"[TabContentView] Unbound from: {_viewModel.Title}");
            }
            
            // Bind to new ViewModel
            _viewModel = DataContext as TabViewModel;
            if (_viewModel != null)
            {
                _viewModel.LoadContentRequested += LoadContentIntoEditor;
                _viewModel.SaveContentRequested += SaveContentFromEditor;
                
                // Load initial content
                LoadContentIntoEditor();
                
                System.Diagnostics.Debug.WriteLine($"[TabContentView] Bound to: {_viewModel.Title}");
            }
        }
        
        private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoading || _viewModel == null) return;
            
            try
            {
                // Extract RTF content
                var rtfContent = Editor.SaveContent();
                
                // Notify ViewModel (which updates SaveManager)
                _viewModel.OnContentChanged(rtfContent);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TabContentView] Content extraction failed: {ex.Message}");
            }
        }
        
        private void LoadContentIntoEditor()
        {
            if (_viewModel == null) return;
            
            _isLoading = true;
            try
            {
                var content = _viewModel.GetContentToLoad();
                Editor.LoadContent(content);
                Editor.MarkClean();
                
                System.Diagnostics.Debug.WriteLine($"[TabContentView] Loaded content: {content?.Length ?? 0} chars for {_viewModel.Title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TabContentView] Load error: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
            }
        }
        
        private string SaveContentFromEditor()
        {
            try
            {
                var content = Editor.SaveContent();
                System.Diagnostics.Debug.WriteLine($"[TabContentView] Saved content: {content?.Length ?? 0} chars");
                return content;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TabContentView] Save error: {ex.Message}");
                return string.Empty;
            }
        }
        
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Clean up
            try
            {
                Editor.TextChanged -= OnEditorTextChanged;
                
                if (_viewModel != null)
                {
                    _viewModel.LoadContentRequested -= LoadContentIntoEditor;
                    _viewModel.SaveContentRequested -= SaveContentFromEditor;
                }
                
                System.Diagnostics.Debug.WriteLine("[TabContentView] Unloaded and cleaned up");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TabContentView] Cleanup error: {ex.Message}");
            }
        }
    }
}

