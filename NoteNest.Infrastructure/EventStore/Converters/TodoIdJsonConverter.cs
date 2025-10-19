using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using NoteNest.Domain.Todos;

namespace NoteNest.Infrastructure.EventStore.Converters
{
    /// <summary>
    /// JSON converter for TodoId value object.
    /// Supports DUAL FORMATS for backward compatibility:
    /// - Object format: {"Value": "guid"} (old - from before converter existed)
    /// - String format: "guid" (new - cleaner)
    /// Serializes as string, deserializes using static From() method.
    /// </summary>
    public class TodoIdJsonConverter : JsonConverter<TodoId>
    {
        public override TodoId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Handle null
            if (reader.TokenType == JsonTokenType.Null)
                return null;
            
            // NEW FORMAT: String (cleaner, used going forward)
            // Example: "TodoId": "12345678-abcd-..."
            if (reader.TokenType == JsonTokenType.String)
            {
                var guidString = reader.GetString();
                if (string.IsNullOrEmpty(guidString))
                    return null;
                
                var guid = Guid.Parse(guidString);
                return TodoId.From(guid);
            }
            
            // OLD FORMAT: Nested object (from before converter was added)
            // Example: "TodoId": {"Value": "12345678-abcd-..."}
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                
                // Extract Value property
                if (doc.RootElement.TryGetProperty("Value", out var valueProperty))
                {
                    var guidString = valueProperty.GetString();
                    if (string.IsNullOrEmpty(guidString))
                        return null;
                    
                    var guid = Guid.Parse(guidString);
                    return TodoId.From(guid);
                }
                
                throw new JsonException("TodoId object missing 'Value' property");
            }
            
            throw new JsonException($"Unexpected token type for TodoId: {reader.TokenType}. Expected String or StartObject.");
        }

        public override void Write(Utf8JsonWriter writer, TodoId value, JsonSerializerOptions options)
        {
            // Always write as string (cleaner format going forward)
            if (value == null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(value.Value.ToString());
        }
    }
}

