using System;
using System.Threading.Tasks;

namespace NoteNest.Core.Interfaces.Services
{
    public interface IServiceErrorHandler
    {
        Task<bool> SafeExecuteAsync(Func<Task> action, string operationName);
        Task<T?> SafeExecuteAsync<T>(Func<Task<T>> action, string operationName);
        void LogError(Exception ex, string context);
        event EventHandler<ErrorEventArgs>? ErrorOccurred;
    }
    
    public class ErrorEventArgs : EventArgs
    {
        public Exception? Exception { get; set; }
        public string Context { get; set; } = string.Empty;
        public bool IsHandled { get; set; }
    }
}