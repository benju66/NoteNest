using System;
using System.Windows;
using System.Windows.Controls;
using NoteNest.Core.Models;
using NoteNest.UI.Interfaces;
using NoteNest.UI.Services;

namespace NoteNest.UI.Controls.Editors
{
	public class EditorContainer : ContentControl
	{
		private readonly IEditorFactory _editorFactory;
		private ITextEditor _currentEditor;
		private string _currentContent = string.Empty;
		private NoteFormat _currentFormat = NoteFormat.PlainText;
		private bool _isUpdatingText;

		public static readonly DependencyProperty ViewModeProperty =
			DependencyProperty.Register(
				name: nameof(ViewMode),
				propertyType: typeof(EditorViewMode),
				ownerType: typeof(EditorContainer),
				typeMetadata: new PropertyMetadata(EditorViewMode.PlainText, OnViewModeChanged));

		public static readonly DependencyProperty FormatProperty =
			DependencyProperty.Register(
				name: nameof(Format),
				propertyType: typeof(NoteFormat),
				ownerType: typeof(EditorContainer),
				typeMetadata: new PropertyMetadata(NoteFormat.PlainText, OnFormatChanged));

		public static readonly DependencyProperty TextProperty =
			DependencyProperty.Register(
				name: nameof(Text),
				propertyType: typeof(string),
				ownerType: typeof(EditorContainer),
				typeMetadata: new PropertyMetadata(string.Empty, OnTextChanged));

		public static readonly DependencyProperty NoteIdProperty =
			DependencyProperty.Register(
				name: nameof(NoteId),
				propertyType: typeof(string),
				ownerType: typeof(EditorContainer),
				typeMetadata: new PropertyMetadata(string.Empty, OnNoteIdChanged));

		public EditorViewMode ViewMode
		{
			get => (EditorViewMode)GetValue(ViewModeProperty);
			set => SetValue(ViewModeProperty, value);
		}

		public NoteFormat Format
		{
			get => (NoteFormat)GetValue(FormatProperty);
			set => SetValue(FormatProperty, value);
		}

		public ITextEditor CurrentEditor => _currentEditor;

		public string NoteId
		{
			get => (string)GetValue(NoteIdProperty) ?? string.Empty;
			set => SetValue(NoteIdProperty, value ?? string.Empty);
		}

		public string Text
		{
			get => (string)GetValue(TextProperty) ?? string.Empty;
			set => SetValue(TextProperty, value ?? string.Empty);
		}

		public EditorContainer()
		{
			_editorFactory = (Application.Current as UI.App)?.ServiceProvider
				?.GetService(typeof(IEditorFactory)) as IEditorFactory;
			CreateEditor();
		}

		private static void OnViewModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var container = (EditorContainer)d;
			container.SwitchEditor();
			// Persist per-note preference
			if (!string.IsNullOrEmpty(container.NoteId))
			{
				EditorViewModeStore.SetForNote(container.NoteId, container.ViewMode);
				try
				{
					var app = Application.Current as UI.App;
					var config = app?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.ConfigurationService)) as NoteNest.Core.Services.ConfigurationService;
					if (config?.Settings != null)
					{
						config.Settings.LastEditorViewModeByNoteId[container.NoteId] = container.ViewMode == EditorViewMode.RichText ? "RichText" : "PlainText";
						// Fire-and-forget save debounce is handled by ConfigurationService
					}
				}
				catch { }
			}
		}

		private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var container = (EditorContainer)d;
			container._currentFormat = (NoteFormat)e.NewValue;
			container.SwitchEditor();
		}

		private void CreateEditor()
		{
			_currentEditor = _editorFactory?.CreateEditor(_currentFormat, ViewMode) ?? new SmartTextEditorAdapter();
			Content = _currentEditor as UIElement;
			_currentEditor.ContentChanged += OnEditorContentChanged;
			if (!string.IsNullOrEmpty(Text))
			{
				_currentEditor.SetContent(Text, _currentFormat);
			}
		}

		private void SwitchEditor()
		{
			if (_currentEditor != null)
			{
				_currentContent = _currentEditor.GetContent();
				_currentEditor.ContentChanged -= OnEditorContentChanged;
				_currentEditor.Dispose();
			}
			CreateEditor();
			if (!string.IsNullOrEmpty(_currentContent))
			{
				_currentEditor.SetContent(_currentContent, _currentFormat);
			}
		}

		private void OnEditorContentChanged(object sender, Interfaces.TextChangedEventArgs e)
		{
			_currentContent = e.NewContent ?? string.Empty;
			if (!_isUpdatingText)
			{
				_isUpdatingText = true;
				try { SetCurrentValue(TextProperty, _currentContent); }
				finally { _isUpdatingText = false; }
			}
			ContentChanged?.Invoke(this, e);
		}

		private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var container = (EditorContainer)d;
			if (container._isUpdatingText) return;
			var newText = e.NewValue as string ?? string.Empty;
			if (container._currentEditor != null)
			{
				var existing = container._currentEditor.GetContent() ?? string.Empty;
				if (string.Equals(existing, newText, StringComparison.Ordinal)) return;
			}
			container._currentContent = newText;
			container._currentEditor?.SetContent(newText, container._currentFormat);
		}

		private static void OnNoteIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var container = (EditorContainer)d;
			var id = e.NewValue as string ?? string.Empty;
			if (!string.IsNullOrEmpty(id))
			{
				var mode = EditorViewModeStore.GetForNote(id, container.ViewMode);
				try
				{
					var app = Application.Current as UI.App;
					var config = app?.ServiceProvider?.GetService(typeof(NoteNest.Core.Services.ConfigurationService)) as NoteNest.Core.Services.ConfigurationService;
					var s = config?.Settings;
					if (s != null && s.LastEditorViewModeByNoteId != null && s.LastEditorViewModeByNoteId.TryGetValue(id, out var stored))
					{
						mode = string.Equals(stored, "RichText", StringComparison.OrdinalIgnoreCase) ? EditorViewMode.RichText : EditorViewMode.PlainText;
					}
				}
				catch { }
				container.ViewMode = mode;
			}
		}

		public event EventHandler<Interfaces.TextChangedEventArgs> ContentChanged;
	}
}


