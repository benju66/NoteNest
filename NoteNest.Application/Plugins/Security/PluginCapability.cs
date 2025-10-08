using System;
using System.Collections.Generic;
using NoteNest.Domain.Common;

namespace NoteNest.Application.Plugins.Security
{
    /// <summary>
    /// Base class for plugin capabilities.
    /// Capabilities define what resources and operations a plugin can access.
    /// </summary>
    public abstract class PluginCapability : ValueObject
    {
        public abstract string Name { get; }
        public abstract CapabilityRiskLevel RiskLevel { get; }
        public abstract string Description { get; }

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Name;
        }
    }

    /// <summary>
    /// Risk level classification for capabilities.
    /// Higher risk levels require more scrutiny and user consent.
    /// </summary>
    public enum CapabilityRiskLevel
    {
        Safe,        // UI operations, read-only access
        Low,         // Event subscriptions, settings
        Medium,      // File system read, network access
        High,        // File system write, database modifications
        Critical     // System integration, external processes
    }

    /// <summary>
    /// Capability for subscribing to domain and application events.
    /// </summary>
    public class EventSubscriptionCapability : PluginCapability
    {
        public IReadOnlyList<Type> AllowedEventTypes { get; }
        
        public override string Name => "EventSubscription";
        public override CapabilityRiskLevel RiskLevel => CapabilityRiskLevel.Low;
        public override string Description => "Subscribe to application events";

        public EventSubscriptionCapability(IReadOnlyList<Type> allowedEventTypes)
        {
            AllowedEventTypes = allowedEventTypes ?? Array.Empty<Type>();
        }

        public static EventSubscriptionCapability AllEvents()
        {
            return new EventSubscriptionCapability(null); // null means all events
        }

        public static EventSubscriptionCapability For(params Type[] eventTypes)
        {
            return new EventSubscriptionCapability(eventTypes);
        }

        public bool IsEventAllowed(Type eventType)
        {
            return AllowedEventTypes == null || AllowedEventTypes.Contains(eventType);
        }
    }

    /// <summary>
    /// Capability for plugin data persistence.
    /// </summary>
    public class DataPersistenceCapability : PluginCapability
    {
        public long MaxStorageSizeBytes { get; }
        public bool AllowBackup { get; }
        
        public override string Name => "DataPersistence";
        public override CapabilityRiskLevel RiskLevel => CapabilityRiskLevel.Medium;
        public override string Description => $"Store plugin data (max {MaxStorageSizeBytes / (1024 * 1024)}MB)";

        public DataPersistenceCapability(long maxStorageSizeBytes = 50 * 1024 * 1024, bool allowBackup = true)
        {
            MaxStorageSizeBytes = maxStorageSizeBytes;
            AllowBackup = allowBackup;
        }

        public static DataPersistenceCapability Standard()
        {
            return new DataPersistenceCapability(50 * 1024 * 1024, true);
        }
    }

    /// <summary>
    /// Capability for UI integration.
    /// </summary>
    public class UIIntegrationCapability : PluginCapability
    {
        public IReadOnlyList<UISlotType> AllowedSlots { get; }
        
        public override string Name => "UIIntegration";
        public override CapabilityRiskLevel RiskLevel => CapabilityRiskLevel.Safe;
        public override string Description => "Display plugin UI in application";

        public UIIntegrationCapability(IReadOnlyList<UISlotType> allowedSlots)
        {
            AllowedSlots = allowedSlots ?? Array.Empty<UISlotType>();
        }

        public static UIIntegrationCapability AllSlots()
        {
            return new UIIntegrationCapability(null); // null means all slots
        }

        public bool IsSlotAllowed(UISlotType slotType)
        {
            return AllowedSlots == null || AllowedSlots.Contains(slotType);
        }
    }

    /// <summary>
    /// UI slot types where plugins can display content.
    /// </summary>
    public enum UISlotType
    {
        RightPanel,      // Todo plugin, tools
        LeftPanel,       // Navigation, explorer
        BottomPanel,     // Output, diagnostics
        FloatingWindow,  // Detached windows
        ToolbarButton,   // Quick actions
        ContextMenu,     // Right-click options
        StatusBar        // Status information
    }

    /// <summary>
    /// Capability for reading note content (read-only).
    /// </summary>
    public class NoteAccessCapability : PluginCapability
    {
        public NoteAccessLevel AccessLevel { get; }
        public IReadOnlyList<string> AllowedCategoryIds { get; }
        
        public override string Name => "NoteAccess";
        public override CapabilityRiskLevel RiskLevel => 
            AccessLevel == NoteAccessLevel.ReadOnly ? CapabilityRiskLevel.Low : CapabilityRiskLevel.High;
        public override string Description => $"{AccessLevel} access to notes";

        public NoteAccessCapability(NoteAccessLevel accessLevel, IReadOnlyList<string> allowedCategoryIds = null)
        {
            AccessLevel = accessLevel;
            AllowedCategoryIds = allowedCategoryIds;
        }

        public static NoteAccessCapability ReadOnly()
        {
            return new NoteAccessCapability(NoteAccessLevel.ReadOnly);
        }

        public bool IsCategoryAllowed(string categoryId)
        {
            return AllowedCategoryIds == null || AllowedCategoryIds.Contains(categoryId);
        }
    }

    public enum NoteAccessLevel
    {
        None,
        ReadOnly,
        ReadWrite
    }
}

