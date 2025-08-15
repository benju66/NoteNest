using System;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Tests.Services
{
    internal class MockLogger : IAppLogger
    {
        public void Debug(string message, params object[] args) { }
        public void Info(string message, params object[] args) { }
        public void Warning(string message, params object[] args) { }
        public void Error(string message, params object[] args) { }
        public void Error(Exception ex, string message, params object[] args) { }
        public void Fatal(string message, params object[] args) { }
        public void Fatal(Exception ex, string message, params object[] args) { }
    }
}


