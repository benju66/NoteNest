using System;
using System.Collections.Generic;
using System.Linq;
using NoteNest.Domain.Common;

namespace NoteNest.Domain.Plugins
{
    /// <summary>
    /// Value object containing plugin metadata and descriptive information.
    /// Immutable once created.
    /// </summary>
    public class PluginMetadata : ValueObject
    {
        public string Name { get; }
        public Version Version { get; }
        public string Description { get; }
        public string Author { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public Version MinimumHostVersion { get; }
        public PluginCategory Category { get; }

        public PluginMetadata(
            string name,
            Version version,
            string description,
            string author,
            IReadOnlyList<string> dependencies,
            Version minimumHostVersion,
            PluginCategory category)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Author = author ?? "Unknown";
            Dependencies = dependencies ?? Array.Empty<string>();
            MinimumHostVersion = minimumHostVersion ?? new Version(1, 0);
            Category = category;
        }

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Name;
            yield return Version;
            yield return Author;
            yield return Category;
        }
    }

    /// <summary>
    /// Plugin category for organization and filtering.
    /// </summary>
    public enum PluginCategory
    {
        Productivity,    // Todo, Calendar, Time tracking
        Editor,          // Text processing, formatting, snippets
        Integration,     // External services, sync, APIs
        Utilities,       // Backup, diagnostics, tools
        Themes,          // UI customization, appearance
        Analytics        // Statistics, insights, reporting
    }
}

