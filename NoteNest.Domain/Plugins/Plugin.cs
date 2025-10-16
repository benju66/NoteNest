using System;
using System.Collections.Generic;
using System.Linq;
using NoteNest.Domain.Common;
using NoteNest.Domain.Plugins.Events;

namespace NoteNest.Domain.Plugins
{
    /// <summary>
    /// Plugin aggregate root representing a loaded plugin instance.
    /// Manages plugin lifecycle, capabilities, and state.
    /// </summary>
    public class Plugin : AggregateRoot
    {
        public PluginId PluginId { get; private set; }
        // Generate stable Guid from plugin ID string
        public override Guid Id => GenerateDeterministicGuid(PluginId.Value);
        
        private static Guid GenerateDeterministicGuid(string value)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"plugin:{value}"));
            return new Guid(hash);
        }
        public PluginMetadata Metadata { get; private set; }
        public PluginStatus Status { get; private set; }
        public DateTime? LoadedAt { get; private set; }
        public DateTime? UnloadedAt { get; private set; }
        
        private readonly List<string> _requestedCapabilities = new();
        private readonly List<string> _grantedCapabilities = new();

        public IReadOnlyList<string> RequestedCapabilities => _requestedCapabilities.AsReadOnly();
        public IReadOnlyList<string> GrantedCapabilities => _grantedCapabilities.AsReadOnly();

        public Plugin() { } // Public for event sourcing

        public Plugin(PluginId id, PluginMetadata metadata, IReadOnlyList<string> requestedCapabilities)
        {
            PluginId = id ?? throw new ArgumentNullException(nameof(id));
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Status = PluginStatus.Discovered;
            
            if (requestedCapabilities != null)
            {
                _requestedCapabilities.AddRange(requestedCapabilities);
            }

            CreatedAt = UpdatedAt = DateTime.UtcNow;
            
            AddDomainEvent(new PluginDiscoveredEvent(PluginId, Metadata.Name));
        }

        public Result Load(IReadOnlyList<string> grantedCapabilities)
        {
            if (Status == PluginStatus.Active)
                return Result.Fail("Plugin is already loaded");

            if (Status == PluginStatus.Error)
                return Result.Fail("Cannot load plugin in error state");

            // Verify all requested capabilities are granted
            var missingCapabilities = _requestedCapabilities.Except(grantedCapabilities).ToList();
            if (missingCapabilities.Any())
            {
                return Result.Fail($"Missing required capabilities: {string.Join(", ", missingCapabilities)}");
            }

            _grantedCapabilities.Clear();
            _grantedCapabilities.AddRange(grantedCapabilities);
            
            Status = PluginStatus.Active;
            LoadedAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;

            AddDomainEvent(new PluginLoadedEvent(PluginId, Metadata.Name, LoadedAt.Value));
            return Result.Ok();
        }

        public Result Unload()
        {
            if (Status != PluginStatus.Active && Status != PluginStatus.Paused)
                return Result.Fail($"Cannot unload plugin in {Status} state");

            Status = PluginStatus.Unloading;
            UnloadedAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;

            AddDomainEvent(new PluginUnloadedEvent(PluginId, Metadata.Name, UnloadedAt.Value));
            return Result.Ok();
        }

        public Result Pause()
        {
            if (Status != PluginStatus.Active)
                return Result.Fail("Can only pause active plugins");

            Status = PluginStatus.Paused;
            UpdatedAt = DateTime.UtcNow;

            AddDomainEvent(new PluginPausedEvent(PluginId, Metadata.Name));
            return Result.Ok();
        }

        public Result Resume()
        {
            if (Status != PluginStatus.Paused)
                return Result.Fail("Can only resume paused plugins");

            Status = PluginStatus.Active;
            UpdatedAt = DateTime.UtcNow;

            AddDomainEvent(new PluginResumedEvent(PluginId, Metadata.Name));
            return Result.Ok();
        }

        public Result MarkError(string errorMessage)
        {
            Status = PluginStatus.Error;
            UpdatedAt = DateTime.UtcNow;

            AddDomainEvent(new PluginErrorEvent(PluginId, Metadata.Name, errorMessage));
            return Result.Ok();
        }

        public Result GrantCapability(string capability)
        {
            if (string.IsNullOrWhiteSpace(capability))
                return Result.Fail("Capability name cannot be empty");

            if (_grantedCapabilities.Contains(capability))
                return Result.Fail($"Capability '{capability}' already granted");

            _grantedCapabilities.Add(capability);
            UpdatedAt = DateTime.UtcNow;

            AddDomainEvent(new PluginCapabilityGrantedEvent(PluginId, capability));
            return Result.Ok();
        }

        public Result RevokeCapability(string capability)
        {
            if (!_grantedCapabilities.Contains(capability))
                return Result.Fail($"Capability '{capability}' not granted");

            _grantedCapabilities.Remove(capability);
            UpdatedAt = DateTime.UtcNow;

            AddDomainEvent(new PluginCapabilityRevokedEvent(PluginId, capability));
            return Result.Ok();
        }

        public bool HasCapability(string capability)
        {
            return _grantedCapabilities.Contains(capability);
        }
        
        /// <summary>
        /// Apply event to rebuild aggregate state from event stream.
        /// </summary>
        public override void Apply(IDomainEvent @event)
        {
            switch (@event)
            {
                case PluginDiscoveredEvent e:
                    PluginId = e.PluginId;
                    Status = PluginStatus.Discovered;
                    CreatedAt = e.OccurredAt;
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case PluginLoadedEvent e:
                    Status = PluginStatus.Active;
                    LoadedAt = e.LoadedAt;
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case PluginUnloadedEvent e:
                    Status = PluginStatus.Unloading;
                    UnloadedAt = e.UnloadedAt;
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case PluginPausedEvent e:
                    Status = PluginStatus.Paused;
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case PluginResumedEvent e:
                    Status = PluginStatus.Active;
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case PluginErrorEvent e:
                    Status = PluginStatus.Error;
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case PluginCapabilityGrantedEvent e:
                    if (!_grantedCapabilities.Contains(e.Capability))
                        _grantedCapabilities.Add(e.Capability);
                    UpdatedAt = e.OccurredAt;
                    break;
                    
                case PluginCapabilityRevokedEvent e:
                    _grantedCapabilities.Remove(e.Capability);
                    UpdatedAt = e.OccurredAt;
                    break;
            }
        }
    }

    /// <summary>
    /// Plugin lifecycle status.
    /// </summary>
    public enum PluginStatus
    {
        Discovered,  // Found but not loaded
        Loading,     // Currently loading
        Active,      // Running and functional
        Paused,      // Temporarily suspended
        Error,       // Failed state
        Unloading    // Currently unloading
    }
}

