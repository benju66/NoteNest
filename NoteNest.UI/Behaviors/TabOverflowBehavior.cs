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
using System.Windows.Media.Animation;

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
			public double CachedScrollAmount { get; set; } = -1;
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
				VisibilityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) }
			};

			// Debounced visibility updater: attach once
			handlers.VisibilityTimer.Tick += (ts, te) =>
			{
				UpdateButtonVisibility(scrollViewer, leftButton, rightButton, dropdownButton);
				handlers.VisibilityTimer.Stop();
			};

			// Mouse wheel scrolling removed to avoid interfering with vertical note scrolling

			// Left button - smooth scroll
			if (leftButton != null)
			{
				handlers.LeftClick = (s, e) =>
				{
					var amount = GetDpiAwareScrollAmount(tabControl, handlers) * 0.8;
					var target = Math.Max(0, scrollViewer.HorizontalOffset - amount);
					SmoothScrollTo(scrollViewer, target);
				};
				leftButton.Click += handlers.LeftClick;
			}

			// Right button - smooth scroll
			if (rightButton != null)
			{
				handlers.RightClick = (s, e) =>
				{
					var amount = GetDpiAwareScrollAmount(tabControl, handlers) * 0.8;
					var target = Math.Min(scrollViewer.ScrollableWidth, scrollViewer.HorizontalOffset + amount);
					SmoothScrollTo(scrollViewer, target);
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

			// Batch initial updates in a single layout pass
			tabControl.Dispatcher.BeginInvoke(new Action(() =>
			{
				UpdateButtonVisibility(scrollViewer, leftButton, rightButton, dropdownButton);
				// Cache scroll amount after DPI is stable
				GetDpiAwareScrollAmount(tabControl, handlers);
			}), DispatcherPriority.Loaded);

			WeakEventManager<FrameworkElement, RoutedEventArgs>.AddHandler(tabControl, "Unloaded", OnTabControlUnloaded);
		}

		private static void OnTabControlUnloaded(object sender, RoutedEventArgs e)
		{
			var tabControl = (DraggableTabControl)sender;
			DetachBehavior(tabControl);
		}

		private static double GetDpiAwareScrollAmount(Visual visual, EventHandlers handlers = null)
		{
			// Use cached amount if available
			if (handlers?.CachedScrollAmount > 0)
				return handlers.CachedScrollAmount;

			var source = PresentationSource.FromVisual(visual);
			if (source?.CompositionTarget != null)
			{
				var dpiX = source.CompositionTarget.TransformToDevice.M11;
				var amount = 80 * dpiX; // slightly smaller base for smoother feel
				if (handlers != null) handlers.CachedScrollAmount = amount;
				return amount;
			}
			return 80; // Fallback
		}

		private static void SmoothScrollTo(ScrollViewer scrollViewer, double targetOffset)
		{
			if (scrollViewer == null) return;
			var currentOffset = scrollViewer.HorizontalOffset;
			var distance = targetOffset - currentOffset;
			if (Math.Abs(distance) < 1)
			{
				scrollViewer.ScrollToHorizontalOffset(targetOffset);
				return;
			}

			var animation = new DoubleAnimation
			{
				From = currentOffset,
				To = targetOffset,
				Duration = TimeSpan.FromMilliseconds(200),
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
			};

			var storyboard = new Storyboard();
			storyboard.Children.Add(animation);
			PropertyPath path = new PropertyPath("(ScrollViewer.HorizontalOffset)");
			Storyboard.SetTarget(animation, scrollViewer);
			Storyboard.SetTargetProperty(animation, path);

			// Begin animation – if storyboard is not effective on read-only DP, fall back to immediate set at completion
			scrollViewer.BeginAnimation(ScrollViewer.HorizontalOffsetProperty, animation);
		}

		private static void UpdateButtonVisibility(ScrollViewer scrollViewer, Button leftButton, Button rightButton, Button dropdownButton)
		{
			if (scrollViewer == null) return;

			var extentWidth = scrollViewer.ExtentWidth;
			var viewportWidth = scrollViewer.ViewportWidth;
			if (extentWidth <= viewportWidth)
			{
				if (leftButton?.Visibility == Visibility.Visible)
					leftButton.Visibility = Visibility.Collapsed;
				if (rightButton?.Visibility == Visibility.Visible)
					rightButton.Visibility = Visibility.Collapsed;
				return;
			}

			var offset = scrollViewer.HorizontalOffset;
			var scrollableWidth = scrollViewer.ScrollableWidth;
			const double epsilon = 0.1;
			bool canScrollLeft = offset > epsilon;
			bool canScrollRight = offset < (scrollableWidth - epsilon);

			UpdateButtonIfChanged(leftButton, canScrollLeft);
			UpdateButtonIfChanged(rightButton, canScrollRight);
		}

		private static void UpdateButtonIfChanged(Button button, bool shouldShow)
		{
			if (button == null) return;
			var target = shouldShow ? Visibility.Visible : Visibility.Collapsed;
			if (button.Visibility != target)
				button.Visibility = target;
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
				double targetOffset = scrollViewer.HorizontalOffset;
				const double padding = 10;
				if (position.X < padding)
				{
					// Too far left
					targetOffset = scrollViewer.HorizontalOffset + position.X - padding;
				}
				else if (position.X + width > scrollViewer.ViewportWidth - padding)
				{
					// Too far right
					targetOffset = scrollViewer.HorizontalOffset + (position.X + width - scrollViewer.ViewportWidth + padding);
				}
				else
				{
					return; // already visible
				}
				SmoothScrollTo(scrollViewer, targetOffset);
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


