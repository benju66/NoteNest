using System;

namespace NoteNest.Core.Interfaces
{
    /// <summary>
    /// Interface for status notifications in the RTF-integrated save system
    /// Provides a clean abstraction for showing save status to users
    /// </summary>
    public interface IStatusNotifier
    {
        /// <summary>
        /// Show a status message to the user
        /// </summary>
        /// <param name="message">The message to display</param>
        /// <param name="type">The type/severity of the message</param>
        /// <param name="duration">Duration in milliseconds (0 = no auto-clear)</param>
        void ShowStatus(string message, StatusType type, int duration = 3000);
    }

    /// <summary>
    /// Status message types for user feedback
    /// </summary>
    public enum StatusType
    {
        Info,
        Success,
        Warning,
        Error,
        InProgress
    }
}
