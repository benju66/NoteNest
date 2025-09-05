using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace NoteNest.UI.Controls
{
    public partial class FormattedTextEditor
    {
        // Custom commands for list operations
        public static readonly RoutedCommand OutdentListCommand = new RoutedCommand(
            "OutdentList", typeof(FormattedTextEditor));
        
        public static readonly RoutedCommand RemoveListFormattingCommand = new RoutedCommand(
            "RemoveListFormatting", typeof(FormattedTextEditor));

        private void RegisterCommandBindings()
        {
            CommandBindings.Add(new CommandBinding(
                OutdentListCommand,
                ExecuteOutdentList,
                CanExecuteListCommand));
            
            CommandBindings.Add(new CommandBinding(
                RemoveListFormattingCommand,
                ExecuteRemoveListFormatting,
                CanExecuteListCommand));
        }

        private void CanExecuteListCommand(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !IsReadOnly && CaretPosition?.Paragraph != null;
        }

        private void ExecuteOutdentList(object sender, ExecutedRoutedEventArgs e)
        {
            BeginChange();
            try
            {
                // Use WPF's built-in outdent command first
                EditingCommands.DecreaseIndentation.Execute(null, this);
                
                // Then handle our custom list logic
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    FixListStructureAfterOutdent();
                }));
            }
            finally
            {
                EndChange();
            }
        }

        private void ExecuteRemoveListFormatting(object sender, ExecutedRoutedEventArgs e)
        {
            BeginChange();
            try
            {
                var para = CaretPosition?.Paragraph;
                if (para?.Parent is ListItem li && li.Parent is List list)
                {
                    // Use character index for stable positioning
                    var documentText = GetFullDocumentText();
                    var caretIndex = GetCaretCharacterIndex();
                    
                    // Remove list formatting
                    RemoveListFormattingInternal(para);
                    
                    // Restore position after layout update
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                    {
                        SetCaretAtCharacterIndex(caretIndex);
                    }));
                }
            }
            finally
            {
                EndChange();
            }
        }

        private void FixListStructureAfterOutdent()
        {
            // Clean up any invalid list structures after outdent
            var para = CaretPosition?.Paragraph;
            if (para?.Parent is ListItem li && li.Parent is List list)
            {
                if (GetListItemCount(list) == 0)
                {
                    var parentBlocks = GetParentBlockCollection(list);
                    parentBlocks?.Remove(list);
                }
            }
        }
    }
}
