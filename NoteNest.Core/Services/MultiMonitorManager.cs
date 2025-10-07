using System;
using System.Collections.Generic;
using System.Linq;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services
{
    /// <summary>
    /// Manages multi-monitor window positioning and restoration for detached windows
    /// Handles monitor bounds validation, DPI scaling, and fallback positioning
    /// </summary>
    public interface IMultiMonitorManager
    {
        /// <summary>
        /// Get information about all available monitors
        /// </summary>
        List<MonitorInfo> GetAvailableMonitors();
        
        /// <summary>
        /// Get the monitor index for a specific point
        /// </summary>
        int GetMonitorIndexForPoint(double x, double y);
        
        /// <summary>
        /// Validate and adjust window bounds for multi-monitor setup
        /// </summary>
        WindowBounds ValidateWindowBounds(WindowBounds originalBounds, int preferredMonitorIndex = -1);
        
        /// <summary>
        /// Get safe default bounds for a new window
        /// </summary>
        WindowBounds GetSafeDefaultBounds(int preferredMonitorIndex = -1);
        
        /// <summary>
        /// Check if bounds are visible on any monitor
        /// </summary>
        bool AreBoundsVisible(WindowBounds bounds);
    }
    
    public class MultiMonitorManager : IMultiMonitorManager
    {
        private readonly IAppLogger _logger;
        private const double MIN_VISIBLE_PIXELS = 100; // Minimum pixels that must be visible on screen
        
        public MultiMonitorManager(IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public List<MonitorInfo> GetAvailableMonitors()
        {
            try
            {
                // Simplified single-monitor implementation for now
                // Multi-monitor support can be added later with proper WinForms reference
                var monitors = new List<MonitorInfo>
                {
                    new MonitorInfo
                    {
                        Index = 0,
                        IsPrimary = true,
                        Bounds = new ScreenRect(0, 0, 1920, 1080), // Default screen size
                        WorkingArea = new ScreenRect(0, 0, 1920, 1040), // Account for taskbar
                        DeviceName = "Primary"
                    }
                };
                
                _logger.Debug($"[MultiMonitorManager] Using single monitor fallback");
                return monitors;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[MultiMonitorManager] Failed to get available monitors");
                
                // Ultimate fallback
                return new List<MonitorInfo>
                {
                    new MonitorInfo
                    {
                        Index = 0,
                        IsPrimary = true,
                        Bounds = new ScreenRect(0, 0, 1920, 1080),
                        WorkingArea = new ScreenRect(0, 0, 1920, 1040),
                        DeviceName = "Primary"
                    }
                };
            }
        }
        
        public int GetMonitorIndexForPoint(double x, double y)
        {
            try
            {
                // Simplified implementation - always return primary monitor
                // Full multi-monitor support can be added later
                return 0;
            }
            catch (Exception ex)
            {
                _logger.Warning($"[MultiMonitorManager] Failed to get monitor index for point ({x}, {y}): {ex.Message}");
            }
            
            // Fallback to primary monitor
            return 0;
        }
        
        public WindowBounds ValidateWindowBounds(WindowBounds originalBounds, int preferredMonitorIndex = -1)
        {
            if (originalBounds == null)
            {
                return GetSafeDefaultBounds(preferredMonitorIndex);
            }
            
            try
            {
                var monitors = GetAvailableMonitors();
                if (!monitors.Any())
                {
                    _logger.Warning("[MultiMonitorManager] No monitors available, using default bounds");
                    return GetSafeDefaultBounds();
                }
                
                // If bounds are already visible, use them as-is
                if (AreBoundsVisible(originalBounds))
                {
                    _logger.Debug("[MultiMonitorManager] Original bounds are visible, using as-is");
                    return originalBounds;
                }
                
                // Try to restore on preferred monitor
                MonitorInfo targetMonitor = null;
                
                if (preferredMonitorIndex >= 0 && preferredMonitorIndex < monitors.Count)
                {
                    targetMonitor = monitors[preferredMonitorIndex];
                    _logger.Debug($"[MultiMonitorManager] Using preferred monitor {preferredMonitorIndex}");
                }
                else
                {
                    // Find monitor that contained the original window center
                    var centerX = originalBounds.Left + originalBounds.Width / 2;
                    var centerY = originalBounds.Top + originalBounds.Height / 2;
                    var monitorIndex = GetMonitorIndexForPoint(centerX, centerY);
                    
                    if (monitorIndex >= 0 && monitorIndex < monitors.Count)
                    {
                        targetMonitor = monitors[monitorIndex];
                        _logger.Debug($"[MultiMonitorManager] Using monitor {monitorIndex} based on window center");
                    }
                }
                
                // Fallback to primary monitor
                if (targetMonitor == null)
                {
                    targetMonitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
                    _logger.Debug("[MultiMonitorManager] Using primary monitor as fallback");
                }
                
                // Adjust bounds to fit on target monitor
                return AdjustBoundsToMonitor(originalBounds, targetMonitor);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[MultiMonitorManager] Failed to validate window bounds");
                return GetSafeDefaultBounds();
            }
        }
        
        public WindowBounds GetSafeDefaultBounds(int preferredMonitorIndex = -1)
        {
            try
            {
                var monitors = GetAvailableMonitors();
                MonitorInfo targetMonitor;
                
                if (preferredMonitorIndex >= 0 && preferredMonitorIndex < monitors.Count)
                {
                    targetMonitor = monitors[preferredMonitorIndex];
                }
                else
                {
                    targetMonitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
                }
                
                // Center window on target monitor with reasonable size
                var workingArea = targetMonitor.WorkingArea;
                var width = Math.Min(800, workingArea.Width * 0.7);
                var height = Math.Min(600, workingArea.Height * 0.7);
                var left = workingArea.X + (workingArea.Width - width) / 2;
                var top = workingArea.Y + (workingArea.Height - height) / 2;
                
                return new WindowBounds
                {
                    Left = left,
                    Top = top,
                    Width = width,
                    Height = height
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[MultiMonitorManager] Failed to get safe default bounds");
                
                // Ultimate fallback
                return new WindowBounds
                {
                    Left = 100,
                    Top = 100,
                    Width = 800,
                    Height = 600
                };
            }
        }
        
        public bool AreBoundsVisible(WindowBounds bounds)
        {
            if (bounds == null) return false;
            
            try
            {
                var monitors = GetAvailableMonitors();
                
                foreach (var monitor in monitors)
                {
                    var workingArea = monitor.WorkingArea;
                    
                    // Calculate intersection area
                    var intersectionLeft = Math.Max(bounds.Left, workingArea.X);
                    var intersectionTop = Math.Max(bounds.Top, workingArea.Y);
                    var intersectionRight = Math.Min(bounds.Left + bounds.Width, workingArea.X + workingArea.Width);
                    var intersectionBottom = Math.Min(bounds.Top + bounds.Height, workingArea.Y + workingArea.Height);
                    
                    if (intersectionRight > intersectionLeft && intersectionBottom > intersectionTop)
                    {
                        var visibleWidth = intersectionRight - intersectionLeft;
                        var visibleHeight = intersectionBottom - intersectionTop;
                        
                        // Check if enough of the window is visible
                        if (visibleWidth >= MIN_VISIBLE_PIXELS && visibleHeight >= MIN_VISIBLE_PIXELS)
                        {
                            return true;
                        }
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.Warning($"[MultiMonitorManager] Failed to check bounds visibility: {ex.Message}");
                return false; // Assume not visible if we can't determine
            }
        }
        
        private WindowBounds AdjustBoundsToMonitor(WindowBounds originalBounds, MonitorInfo monitor)
        {
            var workingArea = monitor.WorkingArea;
            
            // Ensure window size doesn't exceed monitor size
            var adjustedWidth = Math.Min(originalBounds.Width, workingArea.Width * 0.9);
            var adjustedHeight = Math.Min(originalBounds.Height, workingArea.Height * 0.9);
            
            // Position window within monitor bounds
            var adjustedLeft = originalBounds.Left;
            var adjustedTop = originalBounds.Top;
            
            // Ensure left edge is visible
            if (adjustedLeft + MIN_VISIBLE_PIXELS > workingArea.Right)
            {
                adjustedLeft = workingArea.Right - MIN_VISIBLE_PIXELS;
            }
            if (adjustedLeft + adjustedWidth < workingArea.Left + MIN_VISIBLE_PIXELS)
            {
                adjustedLeft = workingArea.Left;
            }
            
            // Ensure top edge is visible
            if (adjustedTop + MIN_VISIBLE_PIXELS > workingArea.Bottom)
            {
                adjustedTop = workingArea.Bottom - MIN_VISIBLE_PIXELS;
            }
            if (adjustedTop + adjustedHeight < workingArea.Top + MIN_VISIBLE_PIXELS)
            {
                adjustedTop = workingArea.Top;
            }
            
            // Final bounds validation
            adjustedLeft = Math.Max(workingArea.Left - adjustedWidth + MIN_VISIBLE_PIXELS,
                                   Math.Min(adjustedLeft, workingArea.Right - MIN_VISIBLE_PIXELS));
            adjustedTop = Math.Max(workingArea.Top,
                                  Math.Min(adjustedTop, workingArea.Bottom - MIN_VISIBLE_PIXELS));
            
            return new WindowBounds
            {
                Left = adjustedLeft,
                Top = adjustedTop,
                Width = adjustedWidth,
                Height = adjustedHeight
            };
        }
    }
    
    /// <summary>
    /// Information about a monitor/display
    /// </summary>
    public class MonitorInfo
    {
        public int Index { get; set; }
        public bool IsPrimary { get; set; }
        public ScreenRect Bounds { get; set; }
        public ScreenRect WorkingArea { get; set; }
        public string DeviceName { get; set; }
        
        public override string ToString()
        {
            return $"Monitor {Index} ({DeviceName}): {Bounds.Width}x{Bounds.Height} at ({Bounds.X}, {Bounds.Y})" +
                   (IsPrimary ? " [Primary]" : "");
        }
    }
    
    /// <summary>
    /// Simple rectangle struct for Core layer (avoids WPF dependency)
    /// </summary>
    public struct ScreenRect
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        
        public ScreenRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
        
        public double Left => X;
        public double Top => Y;
        public double Right => X + Width;
        public double Bottom => Y + Height;
        
        public bool Contains(double x, double y)
        {
            return x >= X && x <= Right && y >= Y && y <= Bottom;
        }
    }
}
