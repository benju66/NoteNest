using System;
using System.Windows;
using System.Windows.Controls;

namespace NoteNest.UI.Utils
{
    /// <summary>
    /// Centralized logic for calculating tab insertion indices during drag operations
    /// </summary>
    public static class TabIndexCalculator
    {
        /// <summary>
        /// Calculates the insertion index for a tab based on mouse position within a header panel
        /// </summary>
        /// <param name="headerPanel">The panel containing tab headers</param>
        /// <param name="localPoint">Mouse position in local coordinates of the panel</param>
        /// <returns>Zero-based index where the tab should be inserted</returns>
        public static int CalculateInsertionIndex(Panel headerPanel, Point localPoint, ScrollViewer scrollViewer = null)
        {
            if (headerPanel == null)
                return 0;

            double adjustedX = localPoint.X;
            if (scrollViewer != null)
            {
                adjustedX += scrollViewer.HorizontalOffset;
            }

            double accumulator = 0;
            for (int i = 0; i < headerPanel.Children.Count; i++)
            {
                if (headerPanel.Children[i] is FrameworkElement child && child.IsVisible)
                {
                    accumulator += child.ActualWidth;
                    if (adjustedX < accumulator)
                        return i;
                }
            }

            // If past all children, insert at end
            return headerPanel.Children.Count;
        }

        /// <summary>
        /// Calculates insertion index with additional validation and error handling
        /// </summary>
        /// <param name="headerPanel">The panel containing tab headers</param>
        /// <param name="localPoint">Mouse position in local coordinates</param>
        /// <param name="excludeIndex">Index to exclude from calculation (for reordering within same panel)</param>
        /// <returns>Validated insertion index</returns>
        public static int CalculateInsertionIndexSafe(Panel headerPanel, Point localPoint, int excludeIndex = -1, ScrollViewer scrollViewer = null)
        {
            if (headerPanel == null)
                return 0;

            try
            {
                double adjustedX = localPoint.X;
                if (scrollViewer != null)
                {
                    adjustedX += scrollViewer.HorizontalOffset;
                }

                double accumulator = 0;
                int adjustedIndex = 0;

                for (int i = 0; i < headerPanel.Children.Count; i++)
                {
                    // Skip the excluded index (typically the item being dragged)
                    if (i == excludeIndex)
                        continue;

                    if (headerPanel.Children[i] is FrameworkElement child && child.IsVisible)
                    {
                        accumulator += child.ActualWidth;
                        if (adjustedX < accumulator)
                            return adjustedIndex;
                        adjustedIndex++;
                    }
                }

                // If past all children, insert at end
                return Math.Max(0, headerPanel.Children.Count - (excludeIndex >= 0 ? 1 : 0));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating insertion index: {ex.Message}");
                return 0; // Safe fallback
            }
        }

        /// <summary>
        /// Gets the visual bounds of a tab at the specified index
        /// </summary>
        /// <param name="headerPanel">The panel containing tab headers</param>
        /// <param name="index">Zero-based tab index</param>
        /// <returns>Rectangle bounds of the tab, or empty rect if invalid</returns>
        public static Rect GetTabBounds(Panel headerPanel, int index)
        {
            if (headerPanel == null || index < 0 || index >= headerPanel.Children.Count)
                return Rect.Empty;

            if (headerPanel.Children[index] is FrameworkElement element)
            {
                var position = element.TranslatePoint(new Point(0, 0), headerPanel);
                return new Rect(position.X, position.Y, element.ActualWidth, element.ActualHeight);
            }

            return Rect.Empty;
        }
    }
}
