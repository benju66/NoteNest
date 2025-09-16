using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;

namespace NoteNest.UI.Controls.Editor.RTF.Features
{
    /// <summary>
    /// URL detection and link formatting feature for RTF editor
    /// Single Responsibility: Automatic hyperlink detection and formatting
    /// Clean, focused feature module following SRP
    /// </summary>
    public class LinkFeature : IDisposable
    {
        private static readonly Regex UrlPattern = new Regex(
            @"(https?://[^\s]+|www\.[^\s]+\.[^\s]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        
        private DispatcherTimer _timer;
        private RichTextBox _editor;
        private bool _disposed = false;
        
        /// <summary>
        /// Attach link detection to an RTF editor
        /// </summary>
        public void Attach(RichTextBox editor)
        {
            if (_disposed || editor == null) return;
            
            _editor = editor;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _timer.Tick += DetectLinks;
            
            // Start detection on text changes
            editor.TextChanged += OnTextChanged;
            
            System.Diagnostics.Debug.WriteLine("[LinkFeature] Attached to editor");
        }
        
        private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_disposed) return;
            
            // Debounce link detection
            _timer?.Stop();
            _timer?.Start();
        }
        
        private void DetectLinks(object sender, EventArgs e)
        {
            if (_disposed || _editor?.Document == null) return;
            
            try
            {
                _timer.Stop();
                
                // Get document text
                var textRange = new TextRange(_editor.Document.ContentStart, _editor.Document.ContentEnd);
                var text = textRange.Text;
                
                if (string.IsNullOrEmpty(text)) return;
                
                // Find URLs
                var matches = UrlPattern.Matches(text);
                
                foreach (Match match in matches)
                {
                    try
                    {
                        ApplyLinkFormatting(match.Value, match.Index);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LinkFeature] Link formatting failed for {match.Value}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LinkFeature] Link detection failed: {ex.Message}");
            }
        }
        
        private void ApplyLinkFormatting(string url, int startIndex)
        {
            if (_disposed || _editor?.Document == null) return;
            
            try
            {
                // Navigate to position in document
                var documentStart = _editor.Document.ContentStart;
                var startPosition = documentStart.GetPositionAtOffset(startIndex);
                var endPosition = documentStart.GetPositionAtOffset(startIndex + url.Length);
                
                if (startPosition == null || endPosition == null) return;
                
                // Create range for the URL
                var urlRange = new TextRange(startPosition, endPosition);
                
                // Apply blue color and underline
                urlRange.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.Blue);
                urlRange.ApplyPropertyValue(Inline.TextDecorationsProperty, System.Windows.TextDecorations.Underline);
                
                // Make it look clickable
                if (_editor.Cursor != System.Windows.Input.Cursors.Hand)
                {
                    // Note: Full hyperlink functionality would require more complex implementation
                    // This provides visual indication that the text is a URL
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LinkFeature] Link formatting application failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Open URL in default browser (utility method)
        /// </summary>
        public static void OpenUrl(string url)
        {
            try
            {
                var fullUrl = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : $"https://{url}";
                
                Process.Start(new ProcessStartInfo
                {
                    FileName = fullUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LinkFeature] URL opening failed: {ex.Message}");
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            try
            {
                _disposed = true;
                
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Tick -= DetectLinks;
                    _timer = null;
                }
                
                if (_editor != null)
                {
                    _editor.TextChanged -= OnTextChanged;
                    _editor = null;
                }
                
                System.Diagnostics.Debug.WriteLine("[LinkFeature] Disposed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LinkFeature] Disposal failed: {ex.Message}");
            }
        }
    }
}
