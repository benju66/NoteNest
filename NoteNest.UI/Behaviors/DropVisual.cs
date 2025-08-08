using System.Windows;

namespace NoteNest.UI.Behaviors
{
    public static class DropVisual
    {
        public static readonly DependencyProperty IsValidDropProperty =
            DependencyProperty.RegisterAttached(
                "IsValidDrop",
                typeof(bool),
                typeof(DropVisual),
                new PropertyMetadata(false));

        public static void SetIsValidDrop(DependencyObject element, bool value)
        {
            element.SetValue(IsValidDropProperty, value);
        }

        public static bool GetIsValidDrop(DependencyObject element)
        {
            return (bool)element.GetValue(IsValidDropProperty);
        }

        public static readonly DependencyProperty IsInvalidDropProperty =
            DependencyProperty.RegisterAttached(
                "IsInvalidDrop",
                typeof(bool),
                typeof(DropVisual),
                new PropertyMetadata(false));

        public static void SetIsInvalidDrop(DependencyObject element, bool value)
        {
            element.SetValue(IsInvalidDropProperty, value);
        }

        public static bool GetIsInvalidDrop(DependencyObject element)
        {
            return (bool)element.GetValue(IsInvalidDropProperty);
        }
    }
}


