using System.Windows;
using System.Windows.Controls;
using NoteNest.Core.Interfaces.Split;

namespace NoteNest.UI.Controls
{
    public partial class SplitContainer : UserControl
    {
        private GridSplitter? _splitter;
        private SplitOrientation _orientation;
        
        public static readonly DependencyProperty OrientationProperty =
            DependencyProperty.Register(nameof(Orientation), typeof(SplitOrientation),
                typeof(SplitContainer), new PropertyMetadata(SplitOrientation.Horizontal, OnOrientationChanged));
        
        public SplitOrientation Orientation
        {
            get => (SplitOrientation)GetValue(OrientationProperty);
            set => SetValue(OrientationProperty, value);
        }
        
        public SplitContainer()
        {
            InitializeComponent();
        }
        
        private static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SplitContainer container)
            {
                container.ConfigureLayout();
            }
        }
        
        private void ConfigureLayout()
        {
            ContainerGrid.Children.Clear();
            ContainerGrid.RowDefinitions.Clear();
            ContainerGrid.ColumnDefinitions.Clear();
            
            if (Orientation == SplitOrientation.Horizontal)
            {
                // Create two rows with splitter
                ContainerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                ContainerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) }); // Splitter
                ContainerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                
                _splitter = new GridSplitter
                {
                    Height = 4,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = System.Windows.Media.Brushes.Gray
                };
                Grid.SetRow(_splitter, 1);
                Grid.SetColumnSpan(_splitter, 3);
            }
            else // Vertical
            {
                // Create two columns with splitter
                ContainerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                ContainerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) }); // Splitter
                ContainerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                
                _splitter = new GridSplitter
                {
                    Width = 4,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = System.Windows.Media.Brushes.Gray
                };
                Grid.SetColumn(_splitter, 1);
                Grid.SetRowSpan(_splitter, 3);
            }
            
            if (_splitter != null)
            {
                ContainerGrid.Children.Add(_splitter);
            }
        }
        
        public void SetFirstPane(UIElement element)
        {
            if (Orientation == SplitOrientation.Horizontal)
                Grid.SetRow(element, 0);
            else
                Grid.SetColumn(element, 0);
            
            if (!ContainerGrid.Children.Contains(element))
                ContainerGrid.Children.Add(element);
        }
        
        public void SetSecondPane(UIElement element)
        {
            if (Orientation == SplitOrientation.Horizontal)
                Grid.SetRow(element, 2);
            else
                Grid.SetColumn(element, 2);
            
            if (!ContainerGrid.Children.Contains(element))
                ContainerGrid.Children.Add(element);
        }
    }
}


