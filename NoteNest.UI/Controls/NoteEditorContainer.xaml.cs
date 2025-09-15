using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using NoteNest.UI.Controls.Editor;
using NoteNest.UI.Controls.Editor.Core;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Controls
{
    /// <summary>
    /// Container that hosts the appropriate editor based on file format
    /// </summary>
    public partial class NoteEditorContainer : UserControl, INotifyPropertyChanged
    {
        private INotesEditor _editor;
        private NoteTabItem _currentTabItem;
        private bool _isLoading;
        
        // Property for toolbar binding
        public INotesEditor UnderlyingEditor => _editor;
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        public NoteEditorContainer()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }
        
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Clean up old editor
            if (_editor != null)
            {
                _editor.ContentChanged -= OnEditorContentChanged;
                if (_editor is UIElement element)
                {
                    EditorHost.Children.Remove(element);
                }
                _editor = null;
            }
            
            // Set up new editor
            if (e.NewValue is NoteTabItem tabItem)
            {
                _currentTabItem = tabItem;
                CreateEditorForTab(tabItem);
                LoadContent(tabItem);
            }
        }
        
        private void CreateEditorForTab(NoteTabItem tabItem)
        {
            // Determine format from file path
            var format = EditorFactory.DetectFormat(tabItem.Note.FilePath);
            
            // Create appropriate editor
            _editor = EditorFactory.CreateEditor(format);
            
            // Wire up events
            _editor.ContentChanged += OnEditorContentChanged;
            
            // Add to visual tree
            if (_editor is UIElement element)
            {
                EditorHost.Children.Clear();
                EditorHost.Children.Add(element);
                
                // Raise property change for toolbar binding
                OnPropertyChanged(nameof(UnderlyingEditor));
            }
            
            System.Diagnostics.Debug.WriteLine($"[CONTAINER] Created {format} editor for: {tabItem.Title}");
        }
        
        private void LoadContent(NoteTabItem tabItem)
        {
            if (_editor == null || tabItem == null) return;
            
            _isLoading = true;
            try
            {
                var content = tabItem.Content ?? string.Empty;
                _editor.LoadContent(content);
                _editor.MarkClean();
                
                System.Diagnostics.Debug.WriteLine($"[CONTAINER] Loaded {content.Length} chars into {_editor.Format} editor");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to load content: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
            }
        }
        
        private void OnEditorContentChanged(object sender, EventArgs e)
        {
            if (_isLoading || _currentTabItem == null || _editor == null) return;
            
            try
            {
                var content = _editor.SaveContent();
                _currentTabItem.UpdateContentFromEditor(content);
                _currentTabItem.NotifyContentChanged();
                
                System.Diagnostics.Debug.WriteLine($"[CONTAINER] Content changed, saved {content.Length} chars");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to save content: {ex.Message}");
            }
        }
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
