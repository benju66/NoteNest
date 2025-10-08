using System.Windows.Input;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.UI.ViewModels.Shell
{
    /// <summary>
    /// View model for an activity bar item (plugin button).
    /// </summary>
    public class ActivityBarItemViewModel : ViewModelBase
    {
        private bool _isActive;

        public string Id { get; }
        public string Tooltip { get; }
        public object IconTemplate { get; } // ContentControl Template for icon
        public ICommand Command { get; }

        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        public ActivityBarItemViewModel(string id, string tooltip, object iconTemplate, ICommand command)
        {
            Id = id;
            Tooltip = tooltip;
            IconTemplate = iconTemplate;
            Command = command;
        }
    }
}

