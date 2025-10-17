using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using NoteNest.Domain.Categories;

namespace NoteNest.Infrastructure.EventStore.Converters
{
    /// <summary>
    /// JSON converter for CategoryId value object.
    /// Serializes as string, deserializes using static From() method.
    /// </summary>
    public class CategoryIdJsonConverter : JsonConverter<CategoryId>
    {
        public override CategoryId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;
            
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException($"Expected string for CategoryId, got {reader.TokenType}");
            
            var value = reader.GetString();
            if (string.IsNullOrEmpty(value))
                return null;
            
            return CategoryId.From(value);
        }

        public override void Write(Utf8JsonWriter writer, CategoryId value, JsonSerializerOptions options)
        {
            if (value == null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(value.Value);
        }
    }
}

