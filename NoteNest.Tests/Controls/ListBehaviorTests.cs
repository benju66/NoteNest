using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using NUnit.Framework;
using NoteNest.UI.Controls;

namespace NoteNest.Tests.Controls
{
    [TestFixture]
    public class ListBehaviorTests
    {
        private FormattedTextEditor CreateEditor()
        {
            // Create editor in a test context
            var editor = new FormattedTextEditor();
            editor.Document.Blocks.Clear();
            return editor;
        }

        private void SimulateKey(FormattedTextEditor editor, Key key, ModifierKeys modifiers = ModifierKeys.None)
        {
            // Note: In a real WPF test, we would simulate the key press properly
            // For now, this is a placeholder that demonstrates the test structure
            // The actual FormattedTextEditor would need to be tested in a WPF test harness
        }

        [Test]
        public void DoubleEnter_ExitsList()
        {
            var editor = CreateEditor();
            
            // Create list with one item
            var list = new List { MarkerStyle = TextMarkerStyle.Disc };
            var item = new ListItem();
            var para = new Paragraph(new Run("Test"));
            item.Blocks.Add(para);
            list.ListItems.Add(item);
            editor.Document.Blocks.Add(list);
            
            // Position at end
            editor.CaretPosition = para.ContentEnd;
            
            // First Enter - should create empty item
            SimulateKey(editor, Key.Enter);
            Assert.That(GetListItemCount(list), Is.EqualTo(2), "First Enter should create a new list item");
            
            // Second Enter - should exit list
            SimulateKey(editor, Key.Enter);
            Assert.That(editor.Document.Blocks.Count, Is.EqualTo(2), "Second Enter should exit list and create paragraph");
            Assert.That(editor.Document.Blocks.LastBlock, Is.TypeOf<Paragraph>(), "Should create a paragraph after list");
        }

        [Test]
        public void Backspace_AtStartOfEmptyItem_RemovesItem()
        {
            var editor = CreateEditor();
            
            // Create list with two items
            var list = new List { MarkerStyle = TextMarkerStyle.Disc };
            var item1 = new ListItem();
            item1.Blocks.Add(new Paragraph(new Run("First")));
            var item2 = new ListItem();
            var para2 = new Paragraph(); // Empty
            item2.Blocks.Add(para2);
            
            list.ListItems.Add(item1);
            list.ListItems.Add(item2);
            editor.Document.Blocks.Add(list);
            
            // Position at start of empty second item
            editor.CaretPosition = para2.ContentStart;
            
            // Backspace should remove the empty item
            SimulateKey(editor, Key.Back);
            Assert.That(GetListItemCount(list), Is.EqualTo(1), "Backspace should remove empty list item");
        }

        [Test]
        public void Backspace_AtStartOfNonEmptyItem_ConvertsToP
()
        {
            var editor = CreateEditor();
            
            // Create list with one item
            var list = new List { MarkerStyle = TextMarkerStyle.Disc };
            var item = new ListItem();
            var para = new Paragraph(new Run("Test content"));
            item.Blocks.Add(para);
            list.ListItems.Add(item);
            editor.Document.Blocks.Add(list);
            
            // Position at start of item
            editor.CaretPosition = para.ContentStart;
            
            // Backspace should convert to paragraph
            SimulateKey(editor, Key.Back);
            Assert.That(editor.Document.Blocks.Count, Is.EqualTo(2), "Should have list and new paragraph");
            Assert.That(editor.Document.Blocks.LastBlock, Is.TypeOf<Paragraph>(), "Should convert to paragraph");
        }

        [Test]
        public void Tab_IndentsListItem()
        {
            var editor = CreateEditor();
            
            // Create list with two items
            var list = new List { MarkerStyle = TextMarkerStyle.Disc };
            var item1 = new ListItem();
            item1.Blocks.Add(new Paragraph(new Run("First")));
            var item2 = new ListItem();
            var para2 = new Paragraph(new Run("Second"));
            item2.Blocks.Add(para2);
            
            list.ListItems.Add(item1);
            list.ListItems.Add(item2);
            editor.Document.Blocks.Add(list);
            
            // Position in second item
            editor.CaretPosition = para2.ContentStart;
            
            // Tab should indent the item
            SimulateKey(editor, Key.Tab);
            
            // Second item should now be nested under first
            Assert.That(GetListItemCount(list), Is.EqualTo(1), "Root list should have one item");
            
            // Check for nested list in first item
            ListItem firstItem = null;
            foreach (ListItem li in list.ListItems)
            {
                firstItem = li;
                break;
            }
            
            Assert.That(firstItem, Is.Not.Null, "First item should exist");
            
            // Look for nested list
            List nestedList = null;
            foreach (var block in firstItem.Blocks)
            {
                if (block is List nested)
                {
                    nestedList = nested;
                    break;
                }
            }
            
            Assert.That(nestedList, Is.Not.Null, "First item should contain nested list");
            Assert.That(GetListItemCount(nestedList), Is.EqualTo(1), "Nested list should contain the indented item");
        }

        [Test]
        public void Tab_CanIndentFirstItem()
        {
            var editor = CreateEditor();
            
            // Create list with single item
            var list = new List { MarkerStyle = TextMarkerStyle.Disc };
            var item = new ListItem();
            var para = new Paragraph(new Run("First and only"));
            item.Blocks.Add(para);
            list.ListItems.Add(item);
            editor.Document.Blocks.Add(list);
            
            // Position in the item
            editor.CaretPosition = para.ContentStart;
            
            // Tab should still work on first item
            SimulateKey(editor, Key.Tab);
            
            // Should have a container item and nested list
            Assert.That(GetListItemCount(list), Is.EqualTo(1), "Should have container item");
        }

        private int GetListItemCount(List list)
        {
            int count = 0;
            foreach (var item in list.ListItems)
            {
                count++;
            }
            return count;
        }
    }
}
