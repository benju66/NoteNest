using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.Generic;
using System.Linq;
using NoteNest.UI.ViewModels.Categories;

namespace NoteNest.UI.Controls
{
	/// <summary>
	/// Handles drag & drop logic for TreeView (categories and notes).
	/// Enables moving notes between categories and reorganizing category hierarchy.
	/// Based on TabDragHandler pattern.
	/// </summary>
	public class TreeViewDragHandler : IDisposable
	{
	private readonly TreeView _treeView;
	private Point _dragStartPosition;
	private bool _isDragging;
	private object _draggedItem; // CategoryViewModel or NoteItemViewModel
	private TreeViewItem _draggedTreeViewItem;
	private Point _dragOffset; // Offset from cursor to adorner top-left
		
		// Visual feedback
		private Window _dragAdornerWindow;
		private Border _dragAdornerContent;
		
		// Drag & drop callbacks
		private readonly Func<object, object, bool> _canDropCallback;
		private readonly Action<object, object> _dropCallback;
		
		public TreeViewDragHandler(
			TreeView treeView,
			Func<object, object, bool> canDropCallback,
			Action<object, object> dropCallback)
		{
			_treeView = treeView ?? throw new ArgumentNullException(nameof(treeView));
			_canDropCallback = canDropCallback ?? throw new ArgumentNullException(nameof(canDropCallback));
			_dropCallback = dropCallback ?? throw new ArgumentNullException(nameof(dropCallback));
			
			// Wire up drag events
			_treeView.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
			_treeView.PreviewMouseMove += OnPreviewMouseMove;
			_treeView.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
			_treeView.PreviewKeyDown += OnPreviewKeyDown;
			_treeView.AllowDrop = true;
		}
		
		private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			// Capture drag start position
			_dragStartPosition = e.GetPosition(null);
			
			// Find if we clicked on a TreeViewItem
			var clickedElement = e.OriginalSource as DependencyObject;
			var treeViewItem = FindAncestor<TreeViewItem>(clickedElement);
			
			if (treeViewItem != null)
			{
				var dataContext = treeViewItem.DataContext;
				
				// Only allow dragging CategoryViewModel or NoteItemViewModel
				if (dataContext is CategoryViewModel || dataContext is NoteItemViewModel)
				{
					_draggedTreeViewItem = treeViewItem;
					_draggedItem = dataContext;
				}
			}
		}
		
		private void OnPreviewMouseMove(object sender, MouseEventArgs e)
		{
			if (e.LeftButton != MouseButtonState.Pressed || _draggedItem == null)
				return;
			
			if (_isDragging)
			{
				// Update adorner position during drag
				UpdateDragAdorner(e.GetPosition(_treeView));
				UpdateDropTarget(e.GetPosition(_treeView));
				return;
			}
			
			// Check if we've moved far enough to start drag
			var currentPosition = e.GetPosition(null);
			var diff = _dragStartPosition - currentPosition;
			
			if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
			    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
			{
				StartDrag();
			}
		}
		
		private void StartDrag()
		{
			if (_isDragging || _draggedItem == null)
				return;
			
			_isDragging = true;
			
			// Calculate drag offset - position adorner near cursor
			// Negative Y moves it up, negative X moves it left
			_dragOffset = new Point(-50, 5); // Center horizontally, just above cursor
			
			// Create drag adorner (ghost image)
			CreateDragAdorner();
			
			// Dim the original item
			if (_draggedTreeViewItem != null)
				_draggedTreeViewItem.Opacity = 0.5;
			
			// Change cursor
			Mouse.OverrideCursor = Cursors.Hand;
			
			// Capture mouse
			_treeView.CaptureMouse();
		}
		
		private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			if (!_isDragging)
			{
				// Drag didn't start, clear tracked item
				_draggedItem = null;
				_draggedTreeViewItem = null;
				return;
			}
			
			// Complete the drop
			CompleteDrop(e.GetPosition(_treeView));
		}
		
		private void OnPreviewKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Escape && _isDragging)
			{
				CancelDrag();
			}
		}
		
		private void CompleteDrop(Point dropPosition)
		{
			try
			{
				// Find drop target at cursor position
				var dropTarget = FindDropTarget(dropPosition);
				
				if (dropTarget != null && _canDropCallback(_draggedItem, dropTarget))
				{
					// Execute drop operation
					_dropCallback(_draggedItem, dropTarget);
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[TreeViewDragHandler] Drop failed: {ex.Message}");
			}
			finally
			{
				EndDrag();
			}
		}
		
		private void CancelDrag()
		{
			EndDrag();
		}
		
		private void EndDrag()
		{
			_isDragging = false;
			
			// Restore original item opacity
			if (_draggedTreeViewItem != null)
				_draggedTreeViewItem.Opacity = 1.0;
			
			// Remove adorner
			if (_dragAdornerWindow != null)
			{
				_dragAdornerWindow.Close();
				_dragAdornerWindow = null;
				_dragAdornerContent = null;
			}
			
			// Release mouse
			if (_treeView.IsMouseCaptured)
				_treeView.ReleaseMouseCapture();
			
			// Restore cursor
			Mouse.OverrideCursor = null;
			
			// Clear drag state
			_draggedItem = null;
			_draggedTreeViewItem = null;
		}
		
		private void CreateDragAdorner()
		{
			// Create a window to show drag preview (works better than adorner for TreeView)
			_dragAdornerWindow = new Window
			{
				WindowStyle = WindowStyle.None,
				AllowsTransparency = true,
				Background = Brushes.Transparent,
				ShowInTaskbar = false,
				Topmost = true,
				Width = 200,
				Height = 30
			};
			
			// Create visual content
			string itemText = GetItemDisplayText(_draggedItem);
			_dragAdornerContent = new Border
			{
				Background = new SolidColorBrush(Color.FromArgb(200, 100, 100, 100)),
				BorderBrush = Brushes.Gray,
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(3),
				Padding = new Thickness(8, 4, 8, 4),
				Child = new TextBlock
				{
					Text = itemText,
					Foreground = Brushes.White,
					FontSize = 12
				}
			};
			
			_dragAdornerWindow.Content = _dragAdornerContent;
			_dragAdornerWindow.Show();
		}
		
		private void UpdateDragAdorner(Point mousePosition)
		{
			if (_dragAdornerWindow == null)
				return;
			
			// Get current mouse position in screen coordinates directly
			// This avoids coordinate system conversion issues that occur with nested controls
			var currentMousePos = Mouse.GetPosition(null); // null = screen coordinates
			_dragAdornerWindow.Left = currentMousePos.X + _dragOffset.X;
			_dragAdornerWindow.Top = currentMousePos.Y + _dragOffset.Y;
		}
		
		private void UpdateDropTarget(Point mousePosition)
		{
			var target = FindDropTarget(mousePosition);
			
			// Update visual feedback based on whether drop is allowed
			if (_dragAdornerContent != null)
			{
				if (target != null && _canDropCallback(_draggedItem, target))
				{
					_dragAdornerContent.Background = new SolidColorBrush(Color.FromArgb(200, 50, 150, 50));
				}
				else
				{
					_dragAdornerContent.Background = new SolidColorBrush(Color.FromArgb(200, 150, 50, 50));
				}
			}
		}
		
		private object FindDropTarget(Point position)
		{
			// Hit test to find TreeViewItem under cursor
			var hitElement = _treeView.InputHitTest(position) as DependencyObject;
			if (hitElement == null)
				return null;
			
			var targetTreeViewItem = FindAncestor<TreeViewItem>(hitElement);
			if (targetTreeViewItem == null)
				return null;
			
			var dataContext = targetTreeViewItem.DataContext;
			
			// Can only drop on CategoryViewModel (not on other notes)
			if (dataContext is CategoryViewModel)
				return dataContext;
			
			return null;
		}
		
		private string GetItemDisplayText(object item)
		{
			if (item is CategoryViewModel category)
				return $"📁 {category.Name}";
			else if (item is NoteItemViewModel note)
				return $"📄 {note.Title}";
			else
				return "Unknown Item";
		}
		
		private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
		{
			while (current != null)
			{
				if (current is T ancestor)
					return ancestor;
				current = VisualTreeHelper.GetParent(current);
			}
			return null;
		}
		
		public void Dispose()
		{
			// Unhook events
			_treeView.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
			_treeView.PreviewMouseMove -= OnPreviewMouseMove;
			_treeView.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
			_treeView.PreviewKeyDown -= OnPreviewKeyDown;
			
			// Clean up adorner
			if (_dragAdornerWindow != null)
			{
				_dragAdornerWindow.Close();
				_dragAdornerWindow = null;
			}
			
			EndDrag();
		}
	}
}
