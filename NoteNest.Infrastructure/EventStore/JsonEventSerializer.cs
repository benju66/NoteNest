using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NoteNest.Core.Services.Logging;
using NoteNest.Domain.Common;
using NoteNest.Infrastructure.EventStore.Converters;

namespace NoteNest.Infrastructure.EventStore
{
    /// <summary>
    /// JSON-based event serializer with automatic type discovery.
    /// Scans assemblies for event types and maintains type mapping.
    /// </summary>
    public class JsonEventSerializer : IEventSerializer
    {
        private readonly IAppLogger _logger;
        private readonly Dictionary<string, Type> _eventTypes;
        private readonly JsonSerializerOptions _options;
        
        public JsonEventSerializer(IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventTypes = new Dictionary<string, Type>();
            
            _options = new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNameCaseInsensitive = true
            };
            
            // Register custom converters for value objects
            _options.Converters.Add(new NoteIdJsonConverter());
            _options.Converters.Add(new CategoryIdJsonConverter());
            _options.Converters.Add(new PluginIdJsonConverter());
            _options.Converters.Add(new TodoIdJsonConverter());
            
            DiscoverEventTypes();
        }
        
        public string Serialize(IDomainEvent @event)
        {
            try
            {
                return JsonSerializer.Serialize(@event, @event.GetType(), _options);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to serialize event {@event.GetType().Name}", ex);
                throw;
            }
        }
        
        public IDomainEvent Deserialize(string eventType, string eventData)
        {
            if (!_eventTypes.TryGetValue(eventType, out var type))
            {
                throw new InvalidOperationException($"Unknown event type: {eventType}. Available types: {string.Join(", ", _eventTypes.Keys)}");
            }
            
            try
            {
                return (IDomainEvent)JsonSerializer.Deserialize(eventData, type, _options);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to deserialize event {eventType}", ex);
                throw;
            }
        }
        
        private void DiscoverEventTypes()
        {
            // Scan all loaded assemblies for IDomainEvent implementations
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.FullName.StartsWith("NoteNest"));
            
            foreach (var assembly in assemblies)
            {
                try
                {
                    var eventTypes = assembly.GetTypes()
                        .Where(t => typeof(IDomainEvent).IsAssignableFrom(t) 
                                 && !t.IsInterface 
                                 && !t.IsAbstract);
                    
                    foreach (var type in eventTypes)
                    {
                        _eventTypes[type.Name] = type;
                        _logger.Debug($"Registered event type: {type.Name}");
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    _logger.Warning($"Could not load types from assembly {assembly.FullName}: {ex.Message}");
                }
            }
            
            _logger.Info($"Discovered {_eventTypes.Count} event types");
        }
    }
}

