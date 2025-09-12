using System.Windows.Input;

namespace NoteNest.UI.Controls.Editor.Commands
{
    public static class EditorCommands
    {
        public static readonly RoutedCommand ToggleBulletList = new RoutedCommand(
            "ToggleBulletList", typeof(EditorCommands));
        
        public static readonly RoutedCommand ToggleNumberedList = new RoutedCommand(
            "ToggleNumberedList", typeof(EditorCommands));
        
        public static readonly RoutedCommand ToggleTaskList = new RoutedCommand(
            "ToggleTaskList", typeof(EditorCommands));
        
        public static readonly RoutedCommand IndentList = new RoutedCommand(
            "IndentList", typeof(EditorCommands));
        
        public static readonly RoutedCommand OutdentList = new RoutedCommand(
            "OutdentList", typeof(EditorCommands));
        
        // Add keyboard gestures
        static EditorCommands()
        {
            ToggleBulletList.InputGestures.Add(new KeyGesture(Key.B, ModifierKeys.Control | ModifierKeys.Shift));
            ToggleNumberedList.InputGestures.Add(new KeyGesture(Key.N, ModifierKeys.Control | ModifierKeys.Shift));
            IndentList.InputGestures.Add(new KeyGesture(Key.Tab));
            OutdentList.InputGestures.Add(new KeyGesture(Key.Tab, ModifierKeys.Shift));
        }
    }
}
