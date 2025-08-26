using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NoteNest.UI.Controls;
using NoteNest.Core.Interfaces.Services;

namespace NoteNest.UI.Behaviors
{
	/// <summary>
	/// Lightweight attached behavior for tab overflow handling with DPI awareness and accessibility.
	/// </summary>
	public static class TabOverflowBehavior
	{
		#region Global Control

		/// <summary>
		/// Global kill switch for quick rollback if needed
		/// </summary>
		public static bool GloballyDisabled { get; set; }

		#endregion

		#region Attached Property

		public static readonly DependencyProperty EnableOverflowProperty =
			DependencyProperty.RegisterAttached(
				"EnableOverflow",
				typeof(bool),
				typeof(TabOverflowBehavior),
				new PropertyMetadata(false, OnEnableOverflowChanged));

		public static bool GetEnableOverflow(DependencyObject obj)
			=> (bool)obj.GetValue(EnableOverflowProperty);

		public static void SetEnableOverflow(DependencyObject obj, bool value)
			=> obj.SetValue(EnableOverflowProperty, value);

		#endregion

		#region Handler Storage for Cleanup

		private class EventHandlers
		{
			public MouseWheelEventHandler MouseWheel { get; set; }
			public ScrollChangedEventHandler ScrollChanged { get; set; }
			public SelectionChangedEventHandler SelectionChanged { get; set; }
			public RoutedEventHandler LeftClick { get; set; }
			public RoutedEventHandler RightClick { get; set; }
			public RoutedEventHandler DropdownClick { get; set; }
			public DispatcherTimer VisibilityTimer { get; set; }
			public SizeChangedEventHandler SizeChanged { get; set; }
		}

		private static readonly Dictionary<DraggableTabControl, EventHandlers> _handlers = new();
		// Fix 1: Track only our Alt+T KeyBinding for proper cleanup
		private static readonly Dictionary<DraggableTabControl, KeyBinding> _altTBindings = new();

		#endregion

		#region Behavior Implementation

		private static void OnEnableOverflowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (GloballyDisabled)
			{
				System.Diagnostics.Debug.WriteLine("WARNING: TabOverflowBehavior.GloballyDisabled is TRUE - overflow will not work!");
				return;
			}

			if (d is DraggableTabControl tabControl)
			{
				System.Diagnostics.Debug.WriteLine($"TabOverflowBehavior: EnableOverflow changed to {e.NewValue} for {tabControl.Name ?? "unnamed"} TabControl");
				if ((bool)e.NewValue)
					AttachBehavior(tabControl);
				else
					DetachBehavior(tabControl);
			}
		}

		private static void AttachBehavior(DraggableTabControl tabControl)
		{
			// Clean up any existing handlers first
			DetachBehavior(tabControl);

			if (!tabControl.IsLoaded)
			{
				WeakEventManager<FrameworkElement, RoutedEventArgs>
					.AddHandler(tabControl, "Loaded", OnTabControlLoaded);
				return;
			}

			SetupOverflowHandling(tabControl);
		}

		private static void OnTabControlLoaded(object sender, RoutedEventArgs e)
		{
			var tabControl = (DraggableTabControl)sender;
			WeakEventManager<FrameworkElement, RoutedEventArgs>
				.RemoveHandler(tabControl, "Loaded", OnTabControlLoaded);
			SetupOverflowHandling(tabControl);
		}

		private static void SetupOverflowHandling(DraggableTabControl tabControl)
		{
			// Force template application if not already applied
			if (!tabControl.IsArrangeValid || tabControl.Template == null)
			{
				tabControl.ApplyTemplate();
				tabControl.UpdateLayout();
			}

			// Find template parts
			var scrollViewer = tabControl.Template?.FindName("PART_ScrollViewer", tabControl) as ScrollViewer;
			var leftButton = tabControl.Template?.FindName("PART_LeftButton", tabControl) as Button;
			var rightButton = tabControl.Template?.FindName("PART_RightButton", tabControl) as Button;
			var dropdownButton = tabControl.Template?.FindName("PART_DropdownButton", tabControl) as Button;

			if (scrollViewer == null)
			{
				System.Diagnostics.Debug.WriteLine("TabOverflowBehavior: PART_ScrollViewer not found after template application");
				// Schedule a retry after layout completes
				tabControl.Dispatcher.BeginInvoke(new Action(() =>
				{
					var sv = tabControl.Template?.FindName("PART_ScrollViewer", tabControl) as ScrollViewer;
					if (sv != null)
					{
						SetupOverflowHandling(tabControl);
					}
				}), DispatcherPriority.Loaded);
				return;
			}

			var handlers = new EventHandlers
			{
				VisibilityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) }
			};

			// Debounced visibility updater: attach once
			handlers.VisibilityTimer.Tick += (ts, te) =>
			{
				UpdateButtonVisibility(scrollViewer, leftButton, rightButton, dropdownButton);
				handlers.VisibilityTimer.Stop();
			};

			// Mouse wheel scrolling removed to avoid interfering with vertical note scrolling

			// Left button
			if (leftButton != null)
			{
				handlers.LeftClick = (s, e) =>
				{
					var amount = GetDpiAwareScrollAmount(tabControl);
					scrollViewer.ScrollToHorizontalOffset(
						Math.Max(0, scrollViewer.HorizontalOffset - amount));
				};
				leftButton.Click += handlers.LeftClick;
			}

			// Right button
			if (rightButton != null)
			{
				handlers.RightClick = (s, e) =>
				{
					var amount = GetDpiAwareScrollAmount(tabControl);
					scrollViewer.ScrollToHorizontalOffset(
						Math.Min(scrollViewer.ScrollableWidth,
							scrollViewer.HorizontalOffset + amount));
				};
				rightButton.Click += handlers.RightClick;
			}

			// Dropdown button and Alt+T removed per requirements

			// Overflow detection with debounce
			handlers.ScrollChanged = (s, e) =>
			{
				handlers.VisibilityTimer.Stop();
				handlers.VisibilityTimer.Start();
			};
			scrollViewer.ScrollChanged += handlers.ScrollChanged;

			// Also respond to size changes (window resize, DPI changes)
			handlers.SizeChanged = (s, e) =>
			{
				handlers.VisibilityTimer.Stop();
				handlers.VisibilityTimer.Start();
			};
			scrollViewer.SizeChanged += handlers.SizeChanged;

			// Selection changed: ensure selected tab is visible
			handlers.SelectionChanged = (s, e) =>
			{
				if (e.AddedItems.Count > 0)
				{
					tabControl.Dispatcher.BeginInvoke(new Action(() =>
						EnsureSelectedTabVisible(tabControl, scrollViewer)), DispatcherPriority.Loaded);
				}
			};
			tabControl.SelectionChanged += handlers.SelectionChanged;

			_handlers[tabControl] = handlers;

			UpdateButtonVisibility(scrollViewer, leftButton, rightButton, dropdownButton);
			// Ensure one more update after layout completes
			tabControl.Dispatcher.BeginInvoke(new Action(() =>
				UpdateButtonVisibility(scrollViewer, leftButton, rightButton, dropdownButton)),
				DispatcherPriority.Loaded);

			WeakEventManager<FrameworkElement, RoutedEventArgs>.AddHandler(tabControl, "Unloaded", OnTabControlUnloaded);
		}

		private static void OnTabControlUnloaded(object sender, RoutedEventArgs e)
		{
			var tabControl = (DraggableTabControl)sender;
			DetachBehavior(tabControl);
		}

		private static double GetDpiAwareScrollAmount(Visual visual)
		{
			var source = PresentationSource.FromVisual(visual);
			if (source?.CompositionTarget != null)
			{
				var dpiX = source.CompositionTarget.TransformToDevice.M11;
				return 100 * dpiX; // 100 logical pixels
			}
			return 100; // Fallback
		}

		private static void UpdateButtonVisibility(ScrollViewer scrollViewer, Button leftButton, Button rightButton, Button dropdownButton)
		{
			if (scrollViewer == null) return;

			bool isOverflowing = scrollViewer.ExtentWidth > scrollViewer.ViewportWidth;
			bool canScrollLeft = scrollViewer.HorizontalOffset > 0.1;
			bool canScrollRight = scrollViewer.HorizontalOffset < (scrollViewer.ScrollableWidth - 0.1);

			if (leftButton != null)
				leftButton.Visibility = isOverflowing && canScrollLeft ? Visibility.Visible : Visibility.Collapsed;

			if (rightButton != null)
				rightButton.Visibility = isOverflowing && canScrollRight ? Visibility.Visible : Visibility.Collapsed;
		}

		private static void EnsureSelectedTabVisible(DraggableTabControl tabControl, ScrollViewer scrollViewer)
		{
			if (tabControl.SelectedItem == null || scrollViewer == null) return;

			var container = tabControl.ItemContainerGenerator.ContainerFromItem(
				tabControl.SelectedItem) as TabItem;
			if (container == null || !container.IsLoaded) return;

			try
			{
				var transform = container.TransformToAncestor(scrollViewer);
				var position = transform.Transform(new Point(0, 0));
				var width = container.ActualWidth;

				if (position.X < 0)
				{
					scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + position.X - 10);
				}
				else if (position.X + width > scrollViewer.ViewportWidth)
				{
					scrollViewer.ScrollToHorizontalOffset(
						scrollViewer.HorizontalOffset + (position.X + width - scrollViewer.ViewportWidth + 10));
				}
			}
			catch { }
		}

		// Dropdown menu removed per requirements

		private static void DetachBehavior(DraggableTabControl tabControl)
		{
			if (!_handlers.TryGetValue(tabControl, out var handlers))
			{
				return;
			}

			// Mouse wheel detach removed (not attached)

			var scrollViewer = tabControl.Template?.FindName("PART_ScrollViewer", tabControl) as ScrollViewer;
			if (scrollViewer != null && handlers.ScrollChanged != null)
				scrollViewer.ScrollChanged -= handlers.ScrollChanged;

			if (scrollViewer != null && handlers.SizeChanged != null)
				scrollViewer.SizeChanged -= handlers.SizeChanged;

			if (handlers.SelectionChanged != null)
				tabControl.SelectionChanged -= handlers.SelectionChanged;

			var leftButton = tabControl.Template?.FindName("PART_LeftButton", tabControl) as Button;
			if (leftButton != null && handlers.LeftClick != null)
				leftButton.Click -= handlers.LeftClick;

			var rightButton = tabControl.Template?.FindName("PART_RightButton", tabControl) as Button;
			if (rightButton != null && handlers.RightClick != null)
				rightButton.Click -= handlers.RightClick;

			// Dropdown detach removed (feature disabled)

			// Alt+T binding removal not needed (feature disabled)

			handlers.VisibilityTimer?.Stop();

			_handlers.Remove(tabControl);
			_altTBindings.Remove(tabControl);
		}

		#endregion

		#region Helper Command Class

		private class RelayCommand : ICommand
		{
			private readonly Action _execute;
			public event EventHandler CanExecuteChanged;
			public RelayCommand(Action execute) { _execute = execute; }
			public bool CanExecute(object parameter) => true;
			public void Execute(object parameter) => _execute();
		}

		#endregion
	}
}


