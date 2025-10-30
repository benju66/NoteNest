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
            
            // Wire up toolbar Split button to workspace command
            Toolbar.SplitRequested += OnSplitRequested;
            
            // Listen for DataContext changes
            DataContextChanged += OnDataContextChanged;
            
            // Listen for unload to clean up
            Unloaded += OnUnloaded;
            
            System.Diagnostics.Debug.WriteLine("[TabContentView] Initialized");
        }
        
        private void OnSplitRequested(object sender, EventArgs e)
        {
            // Find the MainShellViewModel by walking up the visual tree
            try
            {
                var window = Window.GetWindow(this);
                if (window?.DataContext is NoteNest.UI.ViewModels.Shell.MainShellViewModel shellViewModel)
                {
                    if (shellViewModel.Workspace?.SplitVerticalCommand?.CanExecute(null) == true)
                    {
                        shellViewModel.Workspace.SplitVerticalCommand.Execute(null);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TabContentView] Split command failed: {ex.Message}");
            }
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
                
                // CRITICAL: Load content immediately when DataContext changes
                // This handles WPF TabControl's lazy instantiation where RequestContentLoad()
                // may be called before TabContentView exists. Direct call acts as fallback.
                // Note: Potential double-load is harmless (protected by _isLoading flag)
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
            
            // Defensive: Unsubscribe TextChanged during load to prevent events from firing
            // This ensures _isLoading flag provides complete protection
            Editor.TextChanged -= OnEditorTextChanged;
            
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
                // Resubscribe after load completes
                Editor.TextChanged += OnEditorTextChanged;
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
                Toolbar.SplitRequested -= OnSplitRequested;
                
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

