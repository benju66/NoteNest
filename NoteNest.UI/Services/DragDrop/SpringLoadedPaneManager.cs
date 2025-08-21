using System;
using System.Threading;
using System.Windows;

namespace NoteNest.UI.Services.DragDrop
{
    public class SpringLoadedPaneManager : IDisposable
    {
        private Timer? _timer;
        private WeakReference<FrameworkElement>? _hoveredPane;
        private const int DelayMs = 500;

        public event EventHandler<FrameworkElement>? PaneActivated;

        public void BeginHover(FrameworkElement pane)
        {
            EndHover();
            _hoveredPane = new WeakReference<FrameworkElement>(pane);
            _timer = new Timer(_ => Trigger(), null, DelayMs, Timeout.Infinite);
        }

        public void EndHover()
        {
            _timer?.Dispose();
            _timer = null;
            _hoveredPane = null;
        }

        private void Trigger()
        {
            if (_hoveredPane?.TryGetTarget(out var pane) == true)
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    PaneActivated?.Invoke(this, pane);
                }));
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}


