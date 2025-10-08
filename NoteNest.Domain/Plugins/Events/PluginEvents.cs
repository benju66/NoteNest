using System;
using NoteNest.Domain.Common;

namespace NoteNest.Domain.Plugins.Events
{
    public record PluginDiscoveredEvent(PluginId PluginId, string Name) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record PluginLoadedEvent(PluginId PluginId, string Name, DateTime LoadedAt) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record PluginUnloadedEvent(PluginId PluginId, string Name, DateTime UnloadedAt) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record PluginPausedEvent(PluginId PluginId, string Name) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record PluginResumedEvent(PluginId PluginId, string Name) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record PluginErrorEvent(PluginId PluginId, string Name, string ErrorMessage) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record PluginCapabilityGrantedEvent(PluginId PluginId, string Capability) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public record PluginCapabilityRevokedEvent(PluginId PluginId, string Capability) : IDomainEvent
    {
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
}

