using System;
using System.Threading;
using System.Windows;
using NoteNest.UI.Config;
using System.Windows.Threading;

namespace NoteNest.UI.Services.DragDrop
{
    public class SpringLoadedPaneManager : IDisposable
    {
        private Timer? _timer;
        private WeakReference<FrameworkElement>? _hoveredPane;
        private int DelayMs => DragConfig.Instance.SpringLoadDelayMs;
        private readonly Dispatcher _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

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
            if (_hoveredPane?.TryGetTarget(out var _) == true)
            {
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_hoveredPane?.TryGetTarget(out var validPane) == true)
                    {
                        PaneActivated?.Invoke(this, validPane);
                    }
                }));
            }
        }

        public void Dispose()
        {
            EndHover();
        }
    }
}


