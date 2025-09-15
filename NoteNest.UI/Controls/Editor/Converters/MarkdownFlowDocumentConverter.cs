using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text.RegularExpressions;

namespace NoteNest.UI.Controls.Editor.Converters
{
    /// <summary>
    /// SIMPLIFIED converter for new editor architecture
    /// - Only converts at load/save boundaries (not real-time)
    /// - Supports bullets, numbers, bold, italic, headers
    /// - No task lists, no complex numbering
    /// - Optimized for single-pass conversion
    /// </summary>
    public class MarkdownFlowDocumentConverter
    {
        private readonly MarkdownPipeline _pipeline;

        public MarkdownFlowDocumentConverter()
        {
            // Simplified pipeline - removed task lists, kept core features  
            _pipeline = new MarkdownPipelineBuilder()
                .UseEmphasisExtras()
                .UseListExtras()
                .UseAutoLinks()
                .Build();
        }

        public FlowDocument ConvertToFlowDocument(string markdown, string? fontFamily, double? fontSize)
        {
            var document = new FlowDocument
            {
                FontFamily = new FontFamily(string.IsNullOrWhiteSpace(fontFamily) ? "Calibri" : fontFamily),
                FontSize = fontSize.HasValue && fontSize.Value > 0 ? fontSize.Value : 14,
                LineHeight = (fontSize.HasValue && fontSize.Value > 0 ? fontSize.Value : 14) * 1.4
            };
            // Avoid setting TextOptions on FlowDocument (not a Visual). Host control (RichTextBox)
            // will supply rendering settings.

            if (string.IsNullOrWhiteSpace(markdown))
            {
                document.Blocks.Add(new Paragraph());
                return document;
            }

            // PHASE 2: Pre-process markdown to extract and parse metadata comments
            var (cleanMarkdown, metadataMap) = ExtractMetadataComments(markdown);
            
            var md = Markdown.Parse(cleanMarkdown, _pipeline);
            
            // Track if we need to add spacing between blocks
            MarkdownObject? previousBlock = null;
            int blockIndex = 0;
            
            foreach (var block in md)
            {
                // Add extra spacing between paragraphs if there was a blank line in markdown
                if (previousBlock is ParagraphBlock && block is ParagraphBlock)
                {
                    // Check if there's significant line gap in the source
                    if (previousBlock.Line + 1 < block.Line)
                    {
                        // Add empty paragraph to preserve spacing
                        document.Blocks.Add(new Paragraph());
                    }
                }
                
                var flowBlock = ConvertBlock(block);
                if (flowBlock != null)
                {
                    // PHASE 2: Apply metadata if available for this block
                    if (metadataMap.TryGetValue(blockIndex, out var metadata))
                    {
                        ApplyMetadataToBlock(flowBlock, metadata);
                    }
                    
                    document.Blocks.Add(flowBlock);
                }
                
                previousBlock = block;
                blockIndex++;
            }

            if (document.Blocks.Count == 0)
            {
                document.Blocks.Add(new Paragraph());
            }
            return document;
        }

        private System.Windows.Documents.Block? ConvertBlock(MarkdownObject block)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    return CreateHeading(heading);
                case ParagraphBlock paragraph:
                    return CreateParagraph(paragraph);
                case ListBlock list:
                    return CreateList(list);
                default:
                    return new Paragraph();
            }
        }

        private Paragraph CreateHeading(HeadingBlock heading)
        {
            var para = new Paragraph
            {
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 10, 0, 6)
            };
            para.FontSize = heading.Level switch
            {
                1 => 24,
                2 => 20,
                3 => 18,
                _ => 16
            };
            if (heading.Inline != null)
            {
                foreach (var inline in heading.Inline)
                {
                    AddInline(para, inline);
                }
            }
            return para;
        }

        private Paragraph CreateParagraph(ParagraphBlock paragraphBlock)
        {
            var para = new Paragraph();
            if (paragraphBlock.Inline != null)
            {
                foreach (var inline in paragraphBlock.Inline)
                {
                    AddInline(para, inline);
                }
            }
            return para;
        }

        private void AddInline(Paragraph para, Markdig.Syntax.Inlines.Inline inline)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    para.Inlines.Add(new Run(literal.Content.ToString()));
                    break;
                case EmphasisInline emphasis:
                    var run = new Run(GetInlineText(emphasis));
                    if (emphasis.DelimiterCount >= 2)
                    {
                        run.FontWeight = FontWeights.Bold;
                    }
                    else if (emphasis.DelimiterCount == 1)
                    {
                        run.FontStyle = FontStyles.Italic;
                    }
                    para.Inlines.Add(run);
                    break;
                case LinkInline link:
                    var hyperlink = new Hyperlink
                    {
                        NavigateUri = Uri.TryCreate(link.Url, UriKind.RelativeOrAbsolute, out var uri) ? uri : null,
                        Foreground = Brushes.Blue
                    };
                    hyperlink.Inlines.Add(new Run(GetInlineText(link)));
                    hyperlink.RequestNavigate += (s, e) =>
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = e.Uri.ToString(),
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    };
                    para.Inlines.Add(hyperlink);
                    break;
            }
        }

        private List CreateList(ListBlock listBlock)
        {
            var list = new List
            {
                MarkerStyle = listBlock.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                Margin = new Thickness(0, 2, 0, 2)
            };
            foreach (var item in listBlock)
            {
                if (item is ListItemBlock listItem)
                {
                    var li = new ListItem();
                    // Simplified: no task list support, just convert blocks normally
                    foreach (var block in listItem)
                    {
                        var fb = ConvertBlock(block);
                        if (fb != null) li.Blocks.Add(fb);
                    }
                    list.ListItems.Add(li);
                }
            }
            return list;
        }

        // Removed: Task list methods (IsTaskListParagraph, CreateTaskParagraph)
        // Simplified architecture only supports bullets and numbers

        private string GetParagraphText(ParagraphBlock paragraph)
        {
            var sb = new StringBuilder();
            if (paragraph?.Inline != null)
            {
                foreach (var inline in paragraph.Inline)
                {
                    if (inline is LiteralInline lit)
                    {
                        sb.Append(lit.Content);
                    }
                    else if (inline is ContainerInline ci)
                    {
                        sb.Append(GetInlineText(ci));
                    }
                }
            }
            return sb.ToString();
        }

        private string GetInlineText(ContainerInline container)
        {
            var sb = new StringBuilder();
            foreach (var child in container)
            {
                if (child is LiteralInline literal)
                {
                    sb.Append(literal.Content);
                }
                else if (child is ContainerInline c)
                {
                    sb.Append(GetInlineText(c));
                }
            }
            return sb.ToString();
        }

        public string ConvertToMarkdown(FlowDocument document)
        {
            var markdown = new StringBuilder();
            bool isFirstBlock = true;
            System.Windows.Documents.Block? previousBlock = null;
            
            foreach (var block in document.Blocks)
            {
                // PHASE 2: Add metadata preservation comments
                var metadata = ExtractBlockMetadata(block, previousBlock);
                if (!string.IsNullOrEmpty(metadata))
                {
                    markdown.AppendLine(metadata);
                }
                
                if (!isFirstBlock && block is Paragraph)
                {
                    // Preserve paragraph spacing - add blank line before each paragraph (except first)
                    markdown.AppendLine();
                }
                
                markdown.Append(ConvertBlockToMarkdown(block));
                
                previousBlock = block;
                isFirstBlock = false;
            }
            return markdown.ToString().TrimEnd();
        }
        
        /// <summary>
        /// PHASE 2: Extract metadata from block elements for preservation
        /// </summary>
        private string ExtractBlockMetadata(System.Windows.Documents.Block block, System.Windows.Documents.Block? previousBlock)
        {
            var metadata = new List<string>();
            
            try
            {
                // Extract spacing metadata
                if (block is Paragraph para)
                {
                    // Preserve custom margins
                    if (para.Margin.Top > 1.0)
                        metadata.Add($"space-before:{para.Margin.Top:F0}");
                    if (para.Margin.Bottom > 1.0)
                        metadata.Add($"space-after:{para.Margin.Bottom:F0}");
                    if (para.Margin.Left > 0)
                        metadata.Add($"indent:{para.Margin.Left:F0}");
                }
                else if (block is List list)
                {
                    // Preserve list-specific spacing and indentation
                    if (list.Margin.Top > 2.0 || list.Margin.Bottom > 2.0)
                        metadata.Add($"list-spacing:{list.Margin.Top:F0},{list.Margin.Bottom:F0}");
                    if (list.Padding.Left != 28.0) // 28 is our default
                        metadata.Add($"list-indent:{list.Padding.Left:F0}");
                    
                    metadata.Add($"hanging:true"); // Mark as hanging indent list
                }
                
                // Only output metadata comment if we have any metadata
                if (metadata.Count > 0)
                {
                    return $"<!-- nm:{string.Join(" ", metadata)} -->";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Metadata extraction failed: {ex.Message}");
            }
            
            return string.Empty;
        }
        
        /// <summary>
        /// PHASE 2: Extract HTML metadata comments from markdown and return clean markdown + metadata map
        /// </summary>
        private (string cleanMarkdown, Dictionary<int, Dictionary<string, string>> metadataMap) ExtractMetadataComments(string markdown)
        {
            var metadataMap = new Dictionary<int, Dictionary<string, string>>();
            var lines = markdown.Split('\n');
            var cleanLines = new List<string>();
            int blockIndex = 0;
            
            try
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    
                    // Check if this line is a metadata comment
                    if (line.Trim().StartsWith("<!-- nm:") && line.Trim().EndsWith("-->"))
                    {
                        // Parse metadata comment
                        var metadataContent = line.Trim().Substring(8, line.Trim().Length - 11); // Remove <!-- nm: and -->
                        var metadata = ParseMetadata(metadataContent);
                        
                        if (metadata.Count > 0)
                        {
                            metadataMap[blockIndex] = metadata;
                        }
                        
                        // Don't include the metadata comment in clean markdown
                        continue;
                    }
                    
                    cleanLines.Add(line);
                    
                    // Increment block index for content lines (not blank lines)
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        blockIndex++;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Metadata extraction failed: {ex.Message}");
                // Return original markdown if parsing fails
                return (markdown, new Dictionary<int, Dictionary<string, string>>());
            }
            
            return (string.Join("\n", cleanLines), metadataMap);
        }
        
        /// <summary>
        /// PHASE 2: Parse metadata string into key-value pairs
        /// </summary>
        private Dictionary<string, string> ParseMetadata(string metadataContent)
        {
            var metadata = new Dictionary<string, string>();
            
            try
            {
                var parts = metadataContent.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var part in parts)
                {
                    if (part.Contains(':'))
                    {
                        var keyValue = part.Split(':', 2);
                        if (keyValue.Length == 2)
                        {
                            metadata[keyValue[0].Trim()] = keyValue[1].Trim();
                        }
                    }
                    else
                    {
                        // Boolean flags like "hanging"
                        metadata[part.Trim()] = "true";
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Metadata parsing failed: {ex.Message}");
            }
            
            return metadata;
        }
        
        /// <summary>
        /// PHASE 2: Apply preserved metadata to a FlowDocument block
        /// </summary>
        private void ApplyMetadataToBlock(System.Windows.Documents.Block block, Dictionary<string, string> metadata)
        {
            try
            {
                if (block is Paragraph para)
                {
                    // Apply paragraph-specific metadata
                    if (metadata.TryGetValue("space-before", out var spaceBefore) && 
                        double.TryParse(spaceBefore, out var beforeValue))
                    {
                        para.Margin = new Thickness(para.Margin.Left, beforeValue, para.Margin.Right, para.Margin.Bottom);
                    }
                    
                    if (metadata.TryGetValue("space-after", out var spaceAfter) && 
                        double.TryParse(spaceAfter, out var afterValue))
                    {
                        para.Margin = new Thickness(para.Margin.Left, para.Margin.Top, para.Margin.Right, afterValue);
                    }
                    
                    if (metadata.TryGetValue("indent", out var indent) && 
                        double.TryParse(indent, out var indentValue))
                    {
                        para.Margin = new Thickness(indentValue, para.Margin.Top, para.Margin.Right, para.Margin.Bottom);
                    }
                }
                else if (block is List list)
                {
                    // Apply list-specific metadata
                    if (metadata.TryGetValue("list-spacing", out var listSpacing))
                    {
                        var parts = listSpacing.Split(',');
                        if (parts.Length == 2 && 
                            double.TryParse(parts[0], out var topSpacing) && 
                            double.TryParse(parts[1], out var bottomSpacing))
                        {
                            list.Margin = new Thickness(list.Margin.Left, topSpacing, list.Margin.Right, bottomSpacing);
                        }
                    }
                    
                    if (metadata.TryGetValue("list-indent", out var listIndent) && 
                        double.TryParse(listIndent, out var indentValue))
                    {
                        list.Padding = new Thickness(indentValue, list.Padding.Top, list.Padding.Right, list.Padding.Bottom);
                    }
                    
                    // Note: "hanging:true" is informational - our lists are already hanging by default
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Metadata application failed: {ex.Message}");
                // Continue without metadata if application fails
            }
        }

        private string ConvertBlockToMarkdown(System.Windows.Documents.Block block)
        {
            var sb = new StringBuilder();
            switch (block)
            {
                case Paragraph p:
                    if (p.FontWeight == FontWeights.Bold)
                    {
                        if (p.FontSize >= 24) sb.Append("# ");
                        else if (p.FontSize >= 20) sb.Append("## ");
                        else if (p.FontSize >= 18) sb.Append("### ");
                    }
                    foreach (var inline in p.Inlines)
                    {
                        sb.Append(ConvertInlineToMarkdown(inline));
                    }
                    sb.AppendLine();
                    break;
                case List l:
                    sb.Append(ConvertListToMarkdown(l, 0));
                    break;
            }
            return sb.ToString();
        }
        
        /// <summary>
        /// ENHANCED: Convert list to markdown with proper nesting support and accurate indentation
        /// </summary>
        private string ConvertListToMarkdown(List list, int nestingLevel)
        {
            var sb = new StringBuilder();
            var indent = new string(' ', nestingLevel * 2); // Proper nesting calculation
            int itemNumber = 1;
            
            foreach (var item in list.ListItems)
            {
                var marker = GetMarkerForStyle(list.MarkerStyle, itemNumber);
                var hasContent = false;
                
                foreach (var block in item.Blocks)
                {
                    switch (block)
                    {
                        case Paragraph p:
                            sb.Append($"{indent}{marker}");
                            sb.AppendLine(ConvertParagraphContentToMarkdown(p));
                            hasContent = true;
                            marker = new string(' ', marker.Length); // Continuation indent for multi-block items
                            break;
                            
                        case List nestedList:
                            if (!hasContent) {
                                sb.AppendLine($"{indent}{marker}"); // Empty parent item
                                hasContent = true;
                            }
                            sb.Append(ConvertListToMarkdown(nestedList, nestingLevel + 1));
                            break;
                    }
                }
                itemNumber++;
            }
            return sb.ToString();
        }
        
        /// <summary>
        /// ENHANCED: Get appropriate marker for different list styles
        /// </summary>
        private string GetMarkerForStyle(TextMarkerStyle style, int itemNumber)
        {
            return style switch
            {
                TextMarkerStyle.Decimal => $"{itemNumber}. ",
                TextMarkerStyle.LowerLatin => $"{(char)('a' + itemNumber - 1)}. ",
                TextMarkerStyle.UpperLatin => $"{(char)('A' + itemNumber - 1)}. ",
                TextMarkerStyle.LowerRoman => $"{ToRoman(itemNumber).ToLower()}. ",
                TextMarkerStyle.UpperRoman => $"{ToRoman(itemNumber)}. ",
                TextMarkerStyle.Circle => "○ ",
                TextMarkerStyle.Square => "▪ ",
                _ => "- " // Default to bullet for Disc and others
            };
        }
        
        /// <summary>
        /// ENHANCED: Convert paragraph content to markdown preserving inline formatting
        /// </summary>
        private string ConvertParagraphContentToMarkdown(Paragraph p)
        {
            var sb = new StringBuilder();
            foreach (var inline in p.Inlines)
            {
                sb.Append(ConvertInlineToMarkdown(inline));
            }
            return sb.ToString().TrimEnd();
        }
        
        /// <summary>
        /// ENHANCED: Convert integer to roman numerals for proper list formatting
        /// </summary>
        private string ToRoman(int number)
        {
            var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            var literals = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            
            var result = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                while (number >= values[i])
                {
                    number -= values[i];
                    result.Append(literals[i]);
                }
            }
            return result.ToString();
        }

        private static string IndentLines(string text, int spaces)
        {
            var prefix = new string(' ', Math.Max(0, spaces));
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (int idx = 0; idx < lines.Length; idx++)
            {
                if (lines[idx].Length > 0)
                {
                    lines[idx] = prefix + lines[idx];
                }
            }
            return string.Join(Environment.NewLine, lines);
        }

        private string ConvertInlineToMarkdown(System.Windows.Documents.Inline inline)
        {
            switch (inline)
            {
                case Bold boldSpan:
                    {
                        var inner = ConvertSpanChildrenToMarkdown(boldSpan);
                        return WrapWithMarkers(inner, "**", "**");
                    }
                case Italic italicSpan:
                    {
                        var inner = ConvertSpanChildrenToMarkdown(italicSpan);
                        return WrapWithMarkers(inner, "*", "*");
                    }
                case Run run:
                    var text = run.Text;
                    var bold = run.FontWeight == FontWeights.Bold;
                    var ital = run.FontStyle == FontStyles.Italic;
                    if (bold && ital) return WrapWithMarkers(text, "***", "***");
                    if (bold) return WrapWithMarkers(text, "**", "**");
                    if (ital) return WrapWithMarkers(text, "*", "*");
                    return text;
                case InlineUIContainer ui:
                    // Removed: checkbox support for simplified architecture
                    return string.Empty;
                case Hyperlink link:
                    var linkText = new TextRange(link.ContentStart, link.ContentEnd).Text;
                    if (link.NavigateUri != null)
                        return $"[{linkText}]({link.NavigateUri})";
                    return linkText;
                default:
                    return new TextRange(inline.ContentStart, inline.ContentEnd).Text;
            }
        }

        private static string ConvertSpanChildrenToMarkdown(Span span)
        {
            var sb = new StringBuilder();
            foreach (var child in span.Inlines)
            {
                sb.Append(new MarkdownFlowDocumentConverter().ConvertInlineToMarkdown(child));
            }
            return sb.ToString();
        }

        private static string WrapWithMarkers(string text, string open, string close)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            int start = 0;
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
            int end = text.Length - 1;
            while (end >= 0 && char.IsWhiteSpace(text[end])) end--;
            if (end < start) return text; // all whitespace
            string prefix = text.Substring(0, start);
            string core = text.Substring(start, end - start + 1);
            string suffix = text.Substring(end + 1);
            return prefix + open + core + close + suffix;
        }
    }
}


