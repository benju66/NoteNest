using System.Threading.Tasks;
using System.Windows;
using ModernWpf.Controls;

namespace NoteNest.UI.Helpers
{
    public static class ContentDialog
    {
        public static async Task<ContentDialogResult> ShowAsync(string title, string content, string primaryButton = "OK", string secondaryButton = null)
        {
            var dialog = new ModernWpf.Controls.ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = primaryButton,
                SecondaryButtonText = secondaryButton,
                DefaultButton = ContentDialogButton.Primary
            };

            if (System.Windows.Application.Current.MainWindow != null)
            {
                dialog.Owner = System.Windows.Application.Current.MainWindow;
            }

            return await dialog.ShowAsync();
        }

        public static async Task<bool> ShowYesNoAsync(string title, string content)
        {
            var result = await ShowAsync(title, content, "Yes", "No");
            return result == ContentDialogResult.Primary;
        }

        public static async Task ShowErrorAsync(string title, string errorMessage)
        {
            await ShowAsync(title, errorMessage, "OK");
        }
    }
}


