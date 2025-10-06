using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NoteNest.UI.ViewModels.Workspace;

namespace NoteNest.UI.Controls.Workspace
{
    /// <summary>
    /// Helper utilities for hit-testing and drop zone detection during tab drag/drop
    /// Part of Milestone 2B: Drag & Drop
    /// </summary>
    public static class TabHitTestHelper
    {
        /// <summary>
        /// Find a TabItem from a point within the TabControl
        /// </summary>
        public static TabItem FindTabItemAtPoint(TabControl tabControl, Point point)
        {
            if (tabControl == null) return null;
            
            // Convert point to TabControl coordinates
            var element = tabControl.InputHitTest(point) as DependencyObject;
            
            // Walk up visual tree to find TabItem
            return FindAncestor<TabItem>(element);
        }
        
        /// <summary>
        /// Calculate the insertion index for a drop operation
        /// </summary>
        public static int CalculateInsertionIndex(TabControl tabControl, Point position, TabViewModel draggedTab)
        {
            if (tabControl?.Items == null || tabControl.Items.Count == 0)
                return 0;
            
            // Find the tab panel (horizontal list of tab headers)
            var tabPanel = FindVisualChild<Panel>(tabControl);
            if (tabPanel == null)
                return tabControl.Items.Count;
            
            for (int i = 0; i < tabControl.Items.Count; i++)
            {
                var container = tabControl.ItemContainerGenerator.ContainerFromIndex(i) as TabItem;
                if (container?.DataContext == draggedTab)
                    continue; // Skip the dragged tab itself
                    
                if (container == null) continue;
                
                try
                {
                    // Get tab bounds in TabControl coordinates
                    var transform = container.TransformToAncestor(tabControl);
                    var bounds = transform.TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
                    
                    // Check if position is in the left half of this tab (insert before)
                    if (position.X < bounds.Left + bounds.Width / 2)
                        return i;
                }
                catch
                {
                    // Transform failed, skip this tab
                    continue;
                }
            }
            
            // Insert at end
            return tabControl.Items.Count;
        }
        
        /// <summary>
        /// Get the visual bounds of the insertion point for drawing the indicator line
        /// </summary>
        public static Rect GetInsertionPointBounds(TabControl tabControl, int insertionIndex)
        {
            if (tabControl?.Items == null || tabControl.Items.Count == 0)
            {
                // Empty TabControl - show insertion at start of tab panel
                var tabPanel = FindVisualChild<Panel>(tabControl);
                if (tabPanel != null)
                {
                    try
                    {
                        var transform = tabPanel.TransformToAncestor(tabControl);
                        var panelBounds = transform.TransformBounds(new Rect(0, 0, tabPanel.ActualWidth, tabPanel.ActualHeight));
                        return new Rect(panelBounds.Left, panelBounds.Top, 2, panelBounds.Height);
                    }
                    catch { }
                }
                return Rect.Empty;
            }
            
            // Get bounds of tab at insertion index (or last tab if inserting at end)
            int targetIndex = Math.Min(insertionIndex, tabControl.Items.Count - 1);
            var targetTab = tabControl.ItemContainerGenerator.ContainerFromIndex(targetIndex) as TabItem;
            
            if (targetTab == null)
                return Rect.Empty;
            
            try
            {
                var transform = targetTab.TransformToAncestor(tabControl);
                var bounds = transform.TransformBounds(new Rect(0, 0, targetTab.ActualWidth, targetTab.ActualHeight));
                
                // If inserting at end, show line at right edge
                if (insertionIndex >= tabControl.Items.Count)
                {
                    return new Rect(bounds.Right, bounds.Top, 2, bounds.Height);
                }
                else
                {
                    // Show line at left edge of target tab
                    return new Rect(bounds.Left, bounds.Top, 2, bounds.Height);
                }
            }
            catch
            {
                return Rect.Empty;
            }
        }
        
        /// <summary>
        /// Find the PaneView that contains the given TabControl
        /// </summary>
        public static PaneView FindPaneView(TabControl tabControl)
        {
            return FindAncestor<PaneView>(tabControl);
        }
        
        /// <summary>
        /// Find an ancestor of a specific type in the visual tree
        /// </summary>
        public static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T ancestor)
                    return ancestor;
                
                // Safety check: Only walk visual tree, skip document elements
                try
                {
                    current = VisualTreeHelper.GetParent(current);
                }
                catch (InvalidOperationException)
                {
                    // Hit a non-visual element (Paragraph, Run, etc.) - stop traversal
                    return null;
                }
            }
            return null;
        }
        
        /// <summary>
        /// Find a visual child of a specific type
        /// </summary>
        public static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is T typedChild)
                    return typedChild;
                
                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            
            return null;
        }
    }
}

