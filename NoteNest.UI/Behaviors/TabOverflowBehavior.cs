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
			if (GloballyDisabled) return;

			if (d is DraggableTabControl tabControl)
			{
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
			// Find template parts
			var scrollViewer = tabControl.Template?.FindName("PART_ScrollViewer", tabControl) as ScrollViewer;
			var leftButton = tabControl.Template?.FindName("PART_LeftButton", tabControl) as Button;
			var rightButton = tabControl.Template?.FindName("PART_RightButton", tabControl) as Button;
			var dropdownButton = tabControl.Template?.FindName("PART_DropdownButton", tabControl) as Button;

			if (scrollViewer == null)
			{
				System.Diagnostics.Debug.WriteLine("TabOverflowBehavior: PART_ScrollViewer not found; template may not be applied.");
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

			// Mouse wheel scrolling with DPI awareness
			handlers.MouseWheel = (s, e) =>
			{
				if (tabControl.IsMouseOver && scrollViewer != null)
				{
					var scrollAmount = GetDpiAwareScrollAmount(tabControl);
					scrollViewer.ScrollToHorizontalOffset(
						scrollViewer.HorizontalOffset + (e.Delta > 0 ? -scrollAmount : scrollAmount));
					e.Handled = true;
				}
			};
			tabControl.PreviewMouseWheel += handlers.MouseWheel;

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

			// Dropdown button and Alt+T binding (Fix 1 tracking)
			if (dropdownButton != null)
			{
				handlers.DropdownClick = (s, e) => ShowDropdownMenu(tabControl, dropdownButton);
				dropdownButton.Click += handlers.DropdownClick;

				var altTCommand = new RoutedCommand();
				var altTBinding = new KeyBinding(altTCommand, new KeyGesture(Key.T, ModifierKeys.Alt))
				{
					Command = new RelayCommand(() => ShowDropdownMenu(tabControl, dropdownButton))
				};
				tabControl.InputBindings.Add(altTBinding);
				_altTBindings[tabControl] = altTBinding; // track for cleanup
			}

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

			if (dropdownButton != null)
				dropdownButton.Visibility = isOverflowing ? Visibility.Visible : Visibility.Collapsed;
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

		private static void ShowDropdownMenu(DraggableTabControl tabControl, Button dropdownButton)
		{
			var menu = new ContextMenu
			{
				PlacementTarget = dropdownButton,
				Placement = PlacementMode.Bottom,
				StaysOpen = false
			};

			// Fix 4: Theme-aware resources for highlight
			try
			{
				var accentBrush = Application.Current.FindResource("SystemControlHighlightAccentBrush") as Brush;
				if (accentBrush != null)
				{
					menu.Resources[SystemColors.HighlightBrushKey] = accentBrush;
				}
			}
			catch { }

			int index = 0;
			foreach (var item in tabControl.Items)
			{
				if (item is ITabItem tab)
				{
					var menuItem = new MenuItem
					{
						Header = tab.Title,
						Tag = index++,
						MaxWidth = 300
					};

					// Selection indicator with theme support
					if (tab == tabControl.SelectedItem)
					{
						menuItem.FontWeight = FontWeights.Bold;
						try
						{
							menuItem.Background = Application.Current.FindResource("SystemControlHighlightListLowBrush") as Brush;
						}
						catch { }
					}

					// Dirty indicator with theme-aware colors
					if (tab.IsDirty)
					{
						var stack = new StackPanel { Orientation = Orientation.Horizontal };
						var dirtyIndicator = new TextBlock { Text = "● ", FontSize = 8, VerticalAlignment = VerticalAlignment.Center };
						try
						{
							dirtyIndicator.Foreground = Application.Current.FindResource("SystemControlForegroundBaseHighBrush") as Brush;
						}
						catch { }
						var titleText = new TextBlock { Text = tab.Title, VerticalAlignment = VerticalAlignment.Center };
						stack.Children.Add(dirtyIndicator);
						stack.Children.Add(titleText);
						menuItem.Header = stack;
					}

					menuItem.Click += (s, e) =>
					{
						tabControl.SelectedItem = tab;
						var sv = tabControl.Template?.FindName("PART_ScrollViewer", tabControl) as ScrollViewer;
						if (sv != null)
							EnsureSelectedTabVisible(tabControl, sv);
					};

					menu.Items.Add(menuItem);
				}
			}

			menu.IsOpen = true;
		}

		private static void DetachBehavior(DraggableTabControl tabControl)
		{
			if (!_handlers.TryGetValue(tabControl, out var handlers))
			{
				// Remove only our Alt+T binding even if handlers were not created
				if (_altTBindings.TryGetValue(tabControl, out var bindingOnly))
				{
					tabControl.InputBindings.Remove(bindingOnly);
					_altTBindings.Remove(tabControl);
				}
				return;
			}

			if (handlers.MouseWheel != null)
				tabControl.PreviewMouseWheel -= handlers.MouseWheel;

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

			var dropdownButton = tabControl.Template?.FindName("PART_DropdownButton", tabControl) as Button;
			if (dropdownButton != null && handlers.DropdownClick != null)
				dropdownButton.Click -= handlers.DropdownClick;

			// Fix 1: Remove only our Alt+T binding
			if (_altTBindings.TryGetValue(tabControl, out var altTBinding))
			{
				tabControl.InputBindings.Remove(altTBinding);
				_altTBindings.Remove(tabControl);
			}

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


