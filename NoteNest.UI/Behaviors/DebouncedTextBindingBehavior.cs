using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NoteNest.UI.Behaviors
{
	public static class DebouncedTextBindingBehavior
	{
		private static readonly DependencyProperty DebouncerProperty =
			DependencyProperty.RegisterAttached(
				"Debouncer",
				typeof(DispatcherTimer),
				typeof(DebouncedTextBindingBehavior),
				new PropertyMetadata(null));

		public static readonly DependencyProperty DebounceTimeProperty =
			DependencyProperty.RegisterAttached(
				"DebounceTime",
				typeof(int),
				typeof(DebouncedTextBindingBehavior),
				new PropertyMetadata(500, OnDebounceTimeChanged));

		public static int GetDebounceTime(DependencyObject obj)
		{
			return (int)obj.GetValue(DebounceTimeProperty);
		}

		public static void SetDebounceTime(DependencyObject obj, int value)
		{
			obj.SetValue(DebounceTimeProperty, value);
		}

		private static void OnDebounceTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is TextBox textBox)
			{
				textBox.TextChanged -= OnTextChanged;
				textBox.TextChanged += OnTextChanged;
				textBox.Unloaded -= OnTextBoxUnloaded;
				textBox.Unloaded += OnTextBoxUnloaded;
			}
		}

		private static void OnTextChanged(object sender, TextChangedEventArgs e)
		{
			var textBox = (TextBox)sender;
			var debouncer = (DispatcherTimer)textBox.GetValue(DebouncerProperty);
			debouncer?.Stop();
			debouncer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(GetDebounceTime(textBox))
			};
			debouncer.Tick += (s, args) =>
			{
				debouncer.Stop();
				var binding = textBox.GetBindingExpression(TextBox.TextProperty);
				binding?.UpdateSource();
			};
			textBox.SetValue(DebouncerProperty, debouncer);
			debouncer.Start();
		}

		private static void OnTextBoxUnloaded(object sender, RoutedEventArgs e)
		{
			var textBox = (TextBox)sender;
			var debouncer = (DispatcherTimer)textBox.GetValue(DebouncerProperty);
			debouncer?.Stop();
			textBox.TextChanged -= OnTextChanged;
			textBox.Unloaded -= OnTextBoxUnloaded;
		}
	}
}


