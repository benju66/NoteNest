using System;
using System.ComponentModel;

namespace NoteNest.Core.Interfaces.Services
{
    public interface IStateManager : INotifyPropertyChanged
    {
        bool IsLoading { get; set; }
        string StatusMessage { get; set; }
        
        event EventHandler<StateChangedEventArgs>? StateChanged;
        
        // Add these methods to the interface
        void BeginOperation(string message = "Loading...");
        void EndOperation(string message = "Ready");
        void ReportProgress(string message);
        void ClearStatus();
    }
    
    public class StateChangedEventArgs : EventArgs
    {
        public string PropertyName { get; set; } = string.Empty;
        public object? OldValue { get; set; }
        public object? NewValue { get; set; }
    }
}