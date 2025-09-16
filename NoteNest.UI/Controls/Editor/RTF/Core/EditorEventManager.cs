using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NoteNest.Core.Models;

namespace NoteNest.UI.Controls.Editor.RTF.Core
{
    /// <summary>
    /// Centralized event handler management for consistent cleanup
    /// Single Responsibility: Event subscription lifecycle management
    /// Prevents memory leaks from untracked event handlers
    /// </summary>
    public class EditorEventManager : IDisposable
    {
        private readonly List<EventSubscription> _subscriptions;
        private readonly EditorSettings _settings;
        private readonly object _lockObject = new object();
        private bool _disposed = false;
        
        public EditorEventManager(EditorSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _subscriptions = new List<EventSubscription>();
            
            System.Diagnostics.Debug.WriteLine("[EditorEventManager] Initialized for event lifecycle management");
        }
        
        /// <summary>
        /// Subscribe to an event with automatic cleanup tracking
        /// </summary>
        public void Subscribe<T>(T target, string eventName, Delegate handler) where T : class
        {
            if (target == null || string.IsNullOrEmpty(eventName) || handler == null || _disposed)
                return;
                
            try
            {
                lock (_lockObject)
                {
                    // Use reflection to subscribe to the event
                    var eventInfo = typeof(T).GetEvent(eventName);
                    if (eventInfo != null)
                    {
                        eventInfo.AddEventHandler(target, handler);
                        
                        // Track the subscription for cleanup
                        _subscriptions.Add(new EventSubscription
                        {
                            Target = new WeakReference(target),
                            EventName = eventName,
                            Handler = handler,
                            EventInfo = eventInfo,
                            SubscriptionTime = DateTime.Now
                        });
                        
                        System.Diagnostics.Debug.WriteLine($"[EditorEventManager] Subscribed to {typeof(T).Name}.{eventName}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[EditorEventManager] Warning: Event {eventName} not found on {typeof(T).Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditorEventManager] Subscription failed for {eventName}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Strongly-typed subscription methods for common WPF events
        /// These provide compile-time safety and better performance
        /// </summary>
        public void SubscribeToTextChanged(RichTextBox textBox, TextChangedEventHandler handler)
        {
            if (textBox == null || handler == null || _disposed) return;
            
            try
            {
                textBox.TextChanged += handler;
                TrackStrongSubscription(textBox, "TextChanged", handler, 
                    () => textBox.TextChanged -= handler);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditorEventManager] TextChanged subscription failed: {ex.Message}");
            }
        }
        
        public void SubscribeToSelectionChanged(RichTextBox textBox, RoutedEventHandler handler)
        {
            if (textBox == null || handler == null || _disposed) return;
            
            try
            {
                textBox.SelectionChanged += handler;
                TrackStrongSubscription(textBox, "SelectionChanged", handler, 
                    () => textBox.SelectionChanged -= handler);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditorEventManager] SelectionChanged subscription failed: {ex.Message}");
            }
        }
        
        public void SubscribeToContextMenuOpening(RichTextBox textBox, ContextMenuEventHandler handler)
        {
            if (textBox == null || handler == null || _disposed) return;
            
            try
            {
                textBox.ContextMenuOpening += handler;
                TrackStrongSubscription(textBox, "ContextMenuOpening", handler, 
                    () => textBox.ContextMenuOpening -= handler);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditorEventManager] ContextMenuOpening subscription failed: {ex.Message}");
            }
        }
        
        public void SubscribeToPreviewKeyDown(RichTextBox textBox, KeyEventHandler handler)
        {
            if (textBox == null || handler == null || _disposed) return;
            
            try
            {
                textBox.PreviewKeyDown += handler;
                TrackStrongSubscription(textBox, "PreviewKeyDown", handler, 
                    () => textBox.PreviewKeyDown -= handler);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditorEventManager] PreviewKeyDown subscription failed: {ex.Message}");
            }
        }
        
        public void SubscribeToGotFocus(RichTextBox textBox, RoutedEventHandler handler)
        {
            if (textBox == null || handler == null || _disposed) return;
            
            try
            {
                textBox.GotFocus += handler;
                TrackStrongSubscription(textBox, "GotFocus", handler, 
                    () => textBox.GotFocus -= handler);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditorEventManager] GotFocus subscription failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Track a strongly-typed subscription with a cleanup action
        /// </summary>
        private void TrackStrongSubscription(object target, string eventName, Delegate handler, Action unsubscribeAction)
        {
            lock (_lockObject)
            {
                _subscriptions.Add(new EventSubscription
                {
                    Target = new WeakReference(target),
                    EventName = eventName,
                    Handler = handler,
                    UnsubscribeAction = unsubscribeAction,
                    SubscriptionTime = DateTime.Now
                });
            }
        }
        
        /// <summary>
        /// Manually unsubscribe from a specific event
        /// </summary>
        public void Unsubscribe<T>(T target, string eventName, Delegate handler) where T : class
        {
            if (target == null || string.IsNullOrEmpty(eventName) || handler == null || _disposed)
                return;
                
            try
            {
                lock (_lockObject)
                {
                    var eventInfo = typeof(T).GetEvent(eventName);
                    eventInfo?.RemoveEventHandler(target, handler);
                    
                    // Remove from tracking
                    _subscriptions.RemoveAll(s => 
                        ReferenceEquals(s.Target?.Target, target) && 
                        s.EventName == eventName && 
                        ReferenceEquals(s.Handler, handler));
                        
                    System.Diagnostics.Debug.WriteLine($"[EditorEventManager] Unsubscribed from {typeof(T).Name}.{eventName}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditorEventManager] Unsubscription failed for {eventName}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Clean up dead weak references and expired subscriptions
        /// </summary>
        public void PerformCleanup()
        {
            if (_disposed) return;
            
            try
            {
                lock (_lockObject)
                {
                    var expiredSubscriptions = new List<EventSubscription>();
                    var cleanedCount = 0;
                    
                    for (int i = _subscriptions.Count - 1; i >= 0; i--)
                    {
                        var subscription = _subscriptions[i];
                        
                        try
                        {
                            // Check if target is still alive
                            if (!subscription.Target.IsAlive)
                            {
                                expiredSubscriptions.Add(subscription);
                                _subscriptions.RemoveAt(i);
                                cleanedCount++;
                                continue;
                            }
                            
                            // Check for timeout-based cleanup if configured
                            if (_settings.EventHandlerTimeoutMs > 0)
                            {
                                var age = DateTime.Now - subscription.SubscriptionTime;
                                if (age.TotalMilliseconds > _settings.EventHandlerTimeoutMs)
                                {
                                    // Unsubscribe long-lived handlers (may indicate leaks)
                                    if (subscription.UnsubscribeAction != null)
                                    {
                                        subscription.UnsubscribeAction();
                                    }
                                    else if (subscription.EventInfo != null && subscription.Target.Target != null)
                                    {
                                        subscription.EventInfo.RemoveEventHandler(subscription.Target.Target, subscription.Handler);
                                    }
                                    
                                    expiredSubscriptions.Add(subscription);
                                    _subscriptions.RemoveAt(i);
                                    cleanedCount++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[EditorEventManager] Cleanup failed for subscription {subscription.EventName}: {ex.Message}");
                            expiredSubscriptions.Add(subscription);
                            _subscriptions.RemoveAt(i);
                        }
                    }
                    
                    if (cleanedCount > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[EditorEventManager] Cleanup completed: {cleanedCount} subscriptions removed, {_subscriptions.Count} active");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditorEventManager] Cleanup operation failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get current event management statistics
        /// </summary>
        public EventManagerStats GetStats()
        {
            if (_disposed) return new EventManagerStats();
            
            lock (_lockObject)
            {
                var activeCount = 0;
                var deadCount = 0;
                
                foreach (var subscription in _subscriptions)
                {
                    if (subscription.Target.IsAlive)
                        activeCount++;
                    else
                        deadCount++;
                }
                
                return new EventManagerStats
                {
                    ActiveSubscriptions = activeCount,
                    DeadReferences = deadCount,
                    TotalTracked = _subscriptions.Count,
                    TimeoutEnabled = _settings.EventHandlerTimeoutMs > 0,
                    TimeoutMs = _settings.EventHandlerTimeoutMs
                };
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            try
            {
                _disposed = true;
                
                lock (_lockObject)
                {
                    var unsubscribedCount = 0;
                    
                    // Clean up all tracked subscriptions
                    foreach (var subscription in _subscriptions)
                    {
                        try
                        {
                            if (subscription.Target.IsAlive)
                            {
                                if (subscription.UnsubscribeAction != null)
                                {
                                    subscription.UnsubscribeAction();
                                }
                                else if (subscription.EventInfo != null)
                                {
                                    subscription.EventInfo.RemoveEventHandler(subscription.Target.Target, subscription.Handler);
                                }
                                unsubscribedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[EditorEventManager] Cleanup failed for {subscription.EventName}: {ex.Message}");
                        }
                    }
                    
                    _subscriptions.Clear();
                    
                    System.Diagnostics.Debug.WriteLine($"[EditorEventManager] Disposed with {unsubscribedCount} subscriptions cleaned up");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditorEventManager] Disposal failed: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Represents a tracked event subscription
    /// </summary>
    internal class EventSubscription
    {
        public WeakReference Target { get; set; }
        public string EventName { get; set; }
        public Delegate Handler { get; set; }
        public EventInfo EventInfo { get; set; }
        public Action UnsubscribeAction { get; set; }
        public DateTime SubscriptionTime { get; set; }
    }
    
    /// <summary>
    /// Statistics for event manager monitoring
    /// </summary>
    public class EventManagerStats
    {
        public int ActiveSubscriptions { get; set; }
        public int DeadReferences { get; set; }
        public int TotalTracked { get; set; }
        public bool TimeoutEnabled { get; set; }
        public int TimeoutMs { get; set; }
    }
}
