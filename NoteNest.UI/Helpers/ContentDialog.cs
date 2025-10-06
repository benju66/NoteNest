using System.Threading.Tasks;
using System.Windows;

namespace NoteNest.UI.Helpers
{
    /// <summary>
    /// Simple content dialog helper - migrated from ModernWPF to standard WPF
    /// Used by UserNotificationService for simple confirmations
    /// </summary>
    public static class ContentDialog
    {
        public static async Task<bool> ShowAsync(string title, string content, string primaryButton = "OK", string secondaryButton = null)
        {
            var result = false;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var messageBoxResult = secondaryButton != null
                    ? MessageBox.Show(content, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                    : MessageBox.Show(content, title, MessageBoxButton.OK, MessageBoxImage.Information);
                result = messageBoxResult == MessageBoxResult.Yes || messageBoxResult == MessageBoxResult.OK;
            });
            return result;
        }

        public static async Task<bool> ShowYesNoAsync(string title, string content)
        {
            var result = await ShowAsync(title, content, "Yes", "No");
            return result;
        }

        public static async Task ShowErrorAsync(string title, string errorMessage)
        {
            await ShowAsync(title, errorMessage, "OK");
        }
    }
}