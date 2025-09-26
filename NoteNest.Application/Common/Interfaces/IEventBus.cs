using System.Threading.Tasks;
using NoteNest.Domain.Common;

namespace NoteNest.Application.Common.Interfaces
{
    public interface IEventBus
    {
        Task PublishAsync<T>(T domainEvent) where T : IDomainEvent;
    }
}
