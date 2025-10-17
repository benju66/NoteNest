using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using NoteNest.Domain.Plugins;

namespace NoteNest.Infrastructure.EventStore.Converters
{
    /// <summary>
    /// JSON converter for PluginId value object.
    /// Serializes as string, deserializes using static From() method.
    /// </summary>
    public class PluginIdJsonConverter : JsonConverter<PluginId>
    {
        public override PluginId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;
            
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException($"Expected string for PluginId, got {reader.TokenType}");
            
            var value = reader.GetString();
            if (string.IsNullOrEmpty(value))
                return null;
            
            return PluginId.From(value);
        }

        public override void Write(Utf8JsonWriter writer, PluginId value, JsonSerializerOptions options)
        {
            if (value == null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(value.Value);
        }
    }
}

