using System;
using System.Collections.Generic;
using NoteNest.Domain.Common;

namespace NoteNest.Domain.Plugins
{
    /// <summary>
    /// Value object representing a unique plugin identifier.
    /// Ensures plugin IDs are valid and immutable.
    /// </summary>
    public class PluginId : ValueObject
    {
        public string Value { get; }

        private PluginId(string value)
        {
            Value = value;
        }

        public static PluginId From(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Plugin ID cannot be empty", nameof(value));

            if (value.Length > 100)
                throw new ArgumentException("Plugin ID cannot exceed 100 characters", nameof(value));

            // Validate format: lowercase, alphanumeric, hyphens only
            if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^[a-z0-9\-]+$"))
                throw new ArgumentException("Plugin ID must be lowercase alphanumeric with hyphens only", nameof(value));

            return new PluginId(value);
        }

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }

        public override string ToString() => Value;
    }
}

