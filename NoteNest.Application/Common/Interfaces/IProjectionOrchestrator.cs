using System.Threading.Tasks;

namespace NoteNest.Application.Common.Interfaces
{
    /// <summary>
    /// Interface for orchestrating projection updates from event store.
    /// Allows Application layer to trigger projection updates without depending on Infrastructure.
    /// </summary>
    public interface IProjectionOrchestrator
    {
        /// <summary>
        /// Catch up all projections to current event stream.
        /// Processes events they haven't seen yet.
        /// </summary>
        Task CatchUpAsync();
    }
}

