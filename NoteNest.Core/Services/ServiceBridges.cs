using System;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Interfaces.Services;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Status bar service interface for the SupervisedTaskRunner solution
    /// </summary>
    public interface IStatusBarService
    {
        void SetMessage(string message, StatusType type);
        void SetSaveHealth(string indicator, string tooltip);
    }

    /// <summary>
    /// Enhanced dialog service interface that extends existing capabilities
    /// </summary>
    public interface IEnhancedDialogService
    {
        // Inherit existing methods
        Task<bool> ShowConfirmationDialogAsync(string message, string title);
        void ShowError(string message, string title = "Error");
        void ShowInfo(string message, string title = "Information");
        
        // New methods for enhanced functionality
        Task<string> ShowActionDialogAsync(string title, string message, string[] actions);
    }
    
    /// <summary>
    /// Interface for dialog services to avoid circular dependency issues
    /// </summary>
    public interface IUIDialogService
    {
        Task<bool> ShowConfirmationDialogAsync(string message, string title);
        void ShowError(string message, string title = "Error"); 
        void ShowInfo(string message, string title = "Information");
    }

    // StatusType enum moved to NoteNest.Core.Interfaces.StatusType for consistency

    /// <summary>
    /// Bridge that connects IStatusBarService to existing IStateManager
    /// </summary>
    public class StatusBarServiceBridge : IStatusBarService
    {
        private readonly IStateManager _stateManager;
        
        public StatusBarServiceBridge(IStateManager stateManager)
        {
            _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        }
        
        public void SetMessage(string message, NoteNest.Core.Interfaces.StatusType type)
        {
            var icon = type switch
            {
                NoteNest.Core.Interfaces.StatusType.Error => "❌",
                NoteNest.Core.Interfaces.StatusType.Warning => "⚠️", 
                NoteNest.Core.Interfaces.StatusType.Info => "ℹ️",
                _ => ""
            };
            
            _stateManager.StatusMessage = $"{icon} {message}";
        }
        
        public void SetSaveHealth(string indicator, string tooltip)
        {
            _stateManager.StatusMessage = indicator;
            // Note: Tooltip functionality can be added to IStateManager in future if needed
        }
    }

    /// <summary>
    /// Enhanced dialog service that extends existing dialog capabilities
    /// </summary>
    public class EnhancedDialogServiceBridge : IEnhancedDialogService
    {
        private readonly IUIDialogService _baseDialogService;
        
        public EnhancedDialogServiceBridge(IUIDialogService baseDialogService)
        {
            _baseDialogService = baseDialogService ?? throw new ArgumentNullException(nameof(baseDialogService));
        }
        
        // Delegate existing methods to base service
        public Task<bool> ShowConfirmationDialogAsync(string message, string title)
            => _baseDialogService.ShowConfirmationDialogAsync(message, title);
        
        public void ShowError(string message, string title = "Error")
            => _baseDialogService.ShowError(message, title);
        
        public void ShowInfo(string message, string title = "Information")
            => _baseDialogService.ShowInfo(message, title);
        
        // Implement enhanced action dialog using existing capabilities
        public async Task<string> ShowActionDialogAsync(string title, string message, string[] actions)
        {
            if (actions == null || actions.Length == 0)
                return "Cancel";
                
            // For simple two-option scenarios, use existing confirmation dialog
            if (actions.Length == 2)
            {
                var fullMessage = $"{message}\n\nChoose '{actions[0]}' (Yes) or '{actions[1]}' (No)";
                var result = await _baseDialogService.ShowConfirmationDialogAsync(fullMessage, title);
                return result ? actions[0] : actions[1];
            }
            
            // For complex actions, fall back to simple confirmation with first action
            var confirmed = await _baseDialogService.ShowConfirmationDialogAsync(
                $"{message}\n\nProceed with '{actions[0]}'?", title);
            return confirmed ? actions[0] : "Cancel";
        }
    }
}

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Save health monitor interface - implementation will be in UI project
    /// </summary>
    public interface ISaveHealthMonitor : IDisposable
    {
        // Interface only - actual implementation moved to UI project
    }
}
