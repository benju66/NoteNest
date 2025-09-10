using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NoteNest.Core.Services.Search
{
    public static class WordVariantProcessor
    {
        // Common word endings to handle
        private static readonly Dictionary<string, string[]> SuffixVariants = new()
        {
            { "s", new[] { "", "s", "es" } },
            { "ing", new[] { "", "ing", "ed" } },
            { "ed", new[] { "", "ed", "ing" } },
            { "er", new[] { "", "er", "est" } },
            { "est", new[] { "", "er", "est" } },
            { "ies", new[] { "y", "ies" } },
            { "y", new[] { "y", "ies" } }
        };

        public static HashSet<string> GenerateVariants(string word)
        {
            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (string.IsNullOrWhiteSpace(word) || word.Length < 3)
            {
                variants.Add(word.ToLowerInvariant());
                return variants;
            }

            word = word.ToLowerInvariant();
            variants.Add(word);

            // Generate suffix variants
            foreach (var suffix in SuffixVariants)
            {
                if (word.EndsWith(suffix.Key))
                {
                    var stem = word.Substring(0, word.Length - suffix.Key.Length);
                    foreach (var variant in suffix.Value)
                    {
                        variants.Add(stem + variant);
                    }
                }
            }

            // Handle common patterns
            if (word.EndsWith("ly"))
                variants.Add(word.Substring(0, word.Length - 2));
            
            if (word.EndsWith("ness"))
                variants.Add(word.Substring(0, word.Length - 4));

            // Add base word if we haven't already
            if (variants.Count == 1)
            {
                // Try removing common endings
                string[] endings = { "s", "ed", "ing", "er", "est", "ly" };
                foreach (var ending in endings)
                {
                    if (word.EndsWith(ending) && word.Length > ending.Length + 2)
                    {
                        variants.Add(word.Substring(0, word.Length - ending.Length));
                        break;
                    }
                }
            }

            return variants;
        }

        public static List<string> TokenizeQuery(string query)
        {
            // Split on whitespace and punctuation, filter empty
            return Regex.Split(query, @"[\s\-_.,;:!?]+")
                       .Where(t => !string.IsNullOrWhiteSpace(t) && t.Length > 1)
                       .Select(t => t.ToLowerInvariant())
                       .ToList();
        }
    }
}
