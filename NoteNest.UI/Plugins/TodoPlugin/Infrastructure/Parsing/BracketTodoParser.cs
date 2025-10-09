using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Utils;

namespace NoteNest.UI.Plugins.TodoPlugin.Infrastructure.Parsing
{
    /// <summary>
    /// Parses todo items from RTF content using bracket syntax: [todo text]
    /// Leverages SmartRtfExtractor for reliable RTF-to-plain-text conversion.
    /// </summary>
    public class BracketTodoParser
    {
        private readonly IAppLogger _logger;
        private readonly Regex _bracketPattern;
        
        public BracketTodoParser(IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Pattern: [any text without nested brackets]
            // Compiled for performance
            _bracketPattern = new Regex(
                @"\[([^\[\]]+)\]",
                RegexOptions.Compiled | RegexOptions.Multiline);
        }
        
        /// <summary>
        /// Extract todos from RTF file content.
        /// </summary>
        public List<TodoCandidate> ExtractFromRtf(string rtfContent)
        {
            if (string.IsNullOrEmpty(rtfContent))
            {
                _logger.Debug("[BracketParser] Empty RTF content, no todos to extract");
                return new List<TodoCandidate>();
            }
            
            try
            {
                // Step 1: Extract plain text using battle-tested SmartRtfExtractor
                var plainText = SmartRtfExtractor.ExtractPlainText(rtfContent);
                
                if (string.IsNullOrWhiteSpace(plainText))
                {
                    _logger.Debug("[BracketParser] No plain text extracted from RTF");
                    return new List<TodoCandidate>();
                }
                
                // Step 2: Find bracket patterns in plain text
                return ExtractFromPlainText(plainText);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[BracketParser] Failed to extract todos from RTF");
                return new List<TodoCandidate>();
            }
        }
        
        /// <summary>
        /// Extract todos from plain text (for testing or pre-extracted content).
        /// </summary>
        public List<TodoCandidate> ExtractFromPlainText(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText))
                return new List<TodoCandidate>();
            
            var candidates = new List<TodoCandidate>();
            var lines = plainText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var charPosition = 0;
            
            for (int lineNumber = 0; lineNumber < lines.Length; lineNumber++)
            {
                var line = lines[lineNumber];
                var matches = _bracketPattern.Matches(line);
                
                foreach (Match match in matches)
                {
                    var todoText = match.Groups[1].Value.Trim();
                    
                    // Skip empty or whitespace-only brackets
                    if (string.IsNullOrWhiteSpace(todoText))
                        continue;
                    
                    // Skip brackets that look like metadata or placeholders
                    if (IsLikelyNotATodo(todoText))
                        continue;
                    
                    candidates.Add(new TodoCandidate
                    {
                        Text = todoText,
                        LineNumber = lineNumber,
                        CharacterOffset = charPosition + match.Index,
                        OriginalMatch = match.Value,
                        Confidence = CalculateConfidence(todoText),
                        LineContext = line.Trim()
                    });
                }
                
                charPosition += line.Length + 1; // +1 for newline
            }
            
            _logger.Debug($"[BracketParser] Extracted {candidates.Count} todo candidates from {lines.Length} lines");
            return candidates;
        }
        
        /// <summary>
        /// Calculate confidence score for a candidate (0.0 to 1.0).
        /// </summary>
        private double CalculateConfidence(string text)
        {
            var confidence = 0.9; // Base confidence for explicit brackets
            
            // Reduce confidence for very short text (might be abbreviations)
            if (text.Length < 5)
                confidence -= 0.2;
            
            // Increase confidence for action words
            var actionWords = new[] { "call", "email", "send", "buy", "fix", "update", "review", "check" };
            if (actionWords.Any(word => text.ToLowerInvariant().StartsWith(word)))
                confidence += 0.05;
            
            // Ensure confidence stays in range
            return Math.Max(0.0, Math.Min(1.0, confidence));
        }
        
        /// <summary>
        /// Filter out brackets that are likely not todos (metadata, placeholders, etc.).
        /// </summary>
        private bool IsLikelyNotATodo(string text)
        {
            var lowerText = text.ToLowerInvariant();
            
            // Skip common metadata patterns
            var metadataPatterns = new[]
            {
                "note:", "source:", "reference:", "link:", "url:",
                "date:", "time:", "author:", "version:",
                "tbd", "todo", "n/a", "wip", "draft"
            };
            
            // If text is ONLY metadata (not a sentence with metadata)
            if (text.Length < 15 && metadataPatterns.Any(p => lowerText.Contains(p)))
                return true;
            
            // Skip single words (likely abbreviations or labels)
            if (!text.Contains(' ') && text.Length < 15)
                return true;
            
            return false;
        }
    }
    
    /// <summary>
    /// Represents a potential todo item extracted from text.
    /// </summary>
    public class TodoCandidate
    {
        public string Text { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public int CharacterOffset { get; set; }
        public string OriginalMatch { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public string LineContext { get; set; } = string.Empty;
        
        /// <summary>
        /// Generate stable ID for matching across syncs.
        /// Combines text hash with line number for stability.
        /// </summary>
        public string GetStableId()
        {
            // Use first 50 chars + line number for stability
            var keyText = Text.Length > 50 ? Text.Substring(0, 50) : Text;
            return $"{LineNumber}:{keyText.GetHashCode():X8}";
        }
    }
}

