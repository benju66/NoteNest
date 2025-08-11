using System;

namespace NoteNest.Core.Services.Logging
{
    public interface IAppLogger
    {
        void Debug(string message, params object[] args);
        void Info(string message, params object[] args);
        void Warning(string message, params object[] args);
        void Error(string message, params object[] args);
        void Error(Exception ex, string message, params object[] args);
        void Fatal(string message, params object[] args);
        void Fatal(Exception ex, string message, params object[] args);
    }
}