using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text.RegularExpressions;

namespace NoteNest.UI.Services
{
    public class MarkdownFlowDocumentConverter
    {
        private readonly MarkdownPipeline _pipeline;

        public MarkdownFlowDocumentConverter()
        {
            _pipeline = new MarkdownPipelineBuilder()
                .UseTaskLists()
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
            try
            {
                // Ensure FlowDocument participates in visual settings for clear text
                TextOptions.SetTextFormattingMode(document, TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(document, TextRenderingMode.ClearType);
                TextOptions.SetTextHintingMode(document, TextHintingMode.Fixed);
            }
            catch { }

            if (string.IsNullOrWhiteSpace(markdown))
            {
                document.Blocks.Add(new Paragraph());
                return document;
            }

            var md = Markdown.Parse(markdown, _pipeline);
            foreach (var block in md)
            {
                var flowBlock = ConvertBlock(block);
                if (flowBlock != null)
                {
                    document.Blocks.Add(flowBlock);
                }
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
                    foreach (var block in listItem)
                    {
                        // Detect task list pattern like [ ] or [x] at the start of the first paragraph
                        if (block is ParagraphBlock paraBlock && IsTaskListParagraph(paraBlock))
                        {
                            li.Blocks.Add(CreateTaskParagraph(paraBlock));
                        }
                        else
                        {
                            var fb = ConvertBlock(block);
                            if (fb != null) li.Blocks.Add(fb);
                        }
                    }
                    list.ListItems.Add(li);
                }
            }
            return list;
        }

        private bool IsTaskListParagraph(ParagraphBlock paragraph)
        {
            var text = GetParagraphText(paragraph);
            return Regex.IsMatch(text, "^\\s*\\[( |x|X)\\]\\s+");
        }

        private Paragraph CreateTaskParagraph(ParagraphBlock paragraph)
        {
            var text = GetParagraphText(paragraph);
            var match = Regex.Match(text, "^\\s*\\[( |x|X)\\]\\s+(.*)$");
            bool isChecked = match.Success && (match.Groups[1].Value.Equals("x", StringComparison.OrdinalIgnoreCase));
            string remainder = match.Success ? match.Groups[2].Value : text;

            var p = new Paragraph();
            var cb = new CheckBox { IsChecked = isChecked, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,5,0) };
            p.Inlines.Add(new InlineUIContainer(cb));
            p.Inlines.Add(new Run(remainder));
            return p;
        }

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
            foreach (var block in document.Blocks)
            {
                markdown.Append(ConvertBlockToMarkdown(block));
            }
            return markdown.ToString().TrimEnd();
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
                    sb.AppendLine();
                    break;
                case List l:
                    int i = 1;
                    foreach (var item in l.ListItems)
                    {
                        foreach (var b in item.Blocks)
                        {
                            sb.Append(l.MarkerStyle == TextMarkerStyle.Decimal ? $"{i}. " : "- ");
                            sb.Append(ConvertBlockToMarkdown(b).Trim());
                            sb.AppendLine();
                        }
                        i++;
                    }
                    sb.AppendLine();
                    break;
            }
            return sb.ToString();
        }

        private string ConvertInlineToMarkdown(System.Windows.Documents.Inline inline)
        {
            switch (inline)
            {
                case Run run:
                    var text = run.Text;
                    var bold = run.FontWeight == FontWeights.Bold;
                    var ital = run.FontStyle == FontStyles.Italic;
                    if (bold && ital) return $"***{text}***";
                    if (bold) return $"**{text}**";
                    if (ital) return $"*{text}*";
                    return text;
                case InlineUIContainer ui:
                    if (ui.Child is CheckBox cb)
                    {
                        return cb.IsChecked == true ? "[x] " : "[ ] ";
                    }
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
    }
}


