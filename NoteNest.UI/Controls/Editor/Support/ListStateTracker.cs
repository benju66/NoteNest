using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;

namespace NoteNest.UI.Controls.Editor.Support
{
    public class ListStateTracker
    {
        public class ListFormatInfo
        {
            public int LineNumber { get; set; }
            public TextMarkerStyle Style { get; set; }
            public int IndentLevel { get; set; }
        }

        public string SerializeState(FlowDocument document)
        {
            if (document == null) return string.Empty;
            var state = new List<ListFormatInfo>();
            int lineNum = 0;

            foreach (var block in document.Blocks)
            {
                SerializeBlock(block, state, ref lineNum, 0);
            }

            try { return JsonSerializer.Serialize(state); } catch { return string.Empty; }
        }

        public void RestoreState(FlowDocument document, string serializedState)
        {
            // Phase 1 persistence: no-op apply (structure already present).
            // Reserved for future enhancement to rebuild structure if needed.
            _ = document;
            _ = serializedState;
        }

        private void SerializeBlock(Block block, List<ListFormatInfo> state, ref int lineNum, int indentLevel)
        {
            if (block is List list)
            {
                foreach (ListItem item in list.ListItems)
                {
                    foreach (var b in item.Blocks)
                    {
                        SerializeBlock(b as Block, state, ref lineNum, indentLevel + 1);
                    }
                }
            }
            else if (block is Paragraph)
            {
                state.Add(new ListFormatInfo
                {
                    LineNumber = lineNum++,
                    Style = TextMarkerStyle.None, // paragraph line
                    IndentLevel = indentLevel
                });
            }
            else if (block != null)
            {
                lineNum++;
            }
        }
    }
}


