using System.Windows;
using System.Windows.Controls;
using NoteNest.UI.Controls.Editor.Core;

namespace NoteNest.UI.Controls.Editor
{
    public partial class EditorToolbar : UserControl
    {
        public static readonly DependencyProperty EditorProperty =
            DependencyProperty.Register(nameof(Editor), typeof(FormattedTextEditor),
                typeof(EditorToolbar), new PropertyMetadata(null));

        public FormattedTextEditor Editor
        {
            get => (FormattedTextEditor)GetValue(EditorProperty);
            set => SetValue(EditorProperty, value);
        }

        public EditorToolbar()
        {
            InitializeComponent();
        }

        // Move all toolbar click handlers from SplitPaneView here
        private void BulletList_Click(object sender, RoutedEventArgs e)
        {
            Editor?.Focus();
            Editor?.InsertBulletList();
        }

        private void NumberedList_Click(object sender, RoutedEventArgs e)
        {
            Editor?.Focus();
            Editor?.InsertNumberedList();
        }

        private void TaskList_Click(object sender, RoutedEventArgs e)
        {
            // Task list not yet implemented
        }

        private void Indent_Click(object sender, RoutedEventArgs e)
        {
            Editor?.Focus();
            Editor?.IndentSelection();
        }

        private void Outdent_Click(object sender, RoutedEventArgs e)
        {
            Editor?.Focus();
            Editor?.OutdentSelection();
        }
    }
}
