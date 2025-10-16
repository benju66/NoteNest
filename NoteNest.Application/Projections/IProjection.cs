using System.Threading.Tasks;
using NoteNest.Domain.Common;

namespace NoteNest.Application.Projections
{
    /// <summary>
    /// Projection that builds read models from events.
    /// Each projection maintains its own denormalized view optimized for queries.
    /// </summary>
    public interface IProjection
    {
        /// <summary>
        /// Name of the projection (for tracking and logging).
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// Handle a domain event and update the projection.
        /// </summary>
        Task HandleAsync(IDomainEvent @event);
        
        /// <summary>
        /// Rebuild the projection from scratch.
        /// Clears all data and replays all events.
        /// </summary>
        Task RebuildAsync();
        
        /// <summary>
        /// Get the last processed stream position for catch-up.
        /// </summary>
        Task<long> GetLastProcessedPositionAsync();
        
        /// <summary>
        /// Set the last processed stream position after handling events.
        /// </summary>
        Task SetLastProcessedPositionAsync(long position);
    }
}

