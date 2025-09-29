using System.Windows;

namespace NoteNest.UI
{
    /// <summary>
    /// Simple test window for scorched earth rebuild
    /// </summary>
    public partial class MinimalMainWindow : Window
    {
        public MinimalMainWindow()
        {
            InitializeComponent();
            System.Diagnostics.Debug.WriteLine("🪟 MinimalMainWindow created");
        }
    }
}
