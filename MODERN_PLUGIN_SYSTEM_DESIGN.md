# NoteNest Modern Plugin System Architecture

**Design Goal:** Enterprise-grade plugin system aligned with Clean Architecture principles for long-term reliability, maintainability, and performance.

---

## 🏗️ Architecture Overview

### **Layer 1: Domain Layer (`NoteNest.Domain.Plugins`)**

```csharp
// Plugin Identity & Metadata
public class PluginId : ValueObject
{
    public string Value { get; }
    public static PluginId Create(string id) => new(ValidateId(id));
    private PluginId(string value) => Value = value;
}

public class PluginMetadata : ValueObject
{
    public string Name { get; }
    public Version Version { get; }
    public string Description { get; }
    public string Author { get; }
    public IReadOnlyList<string> Dependencies { get; }
    public Version MinimumHostVersion { get; }
    public PluginCategory Category { get; }
}

public enum PluginCategory
{
    Productivity,    // Todo, Calendar, etc.
    Editor,          // Text processing, formatting
    Integration,     // External services, sync
    Utilities,       // Backup, diagnostics
    Themes          // UI customization
}

// Plugin State Management
public class Plugin : AggregateRoot
{
    public PluginId Id { get; private set; }
    public PluginMetadata Metadata { get; private set; }
    public PluginStatus Status { get; private set; }
    public IReadOnlyList<PluginCapability> RequestedCapabilities { get; private set; }
    public IReadOnlyList<PluginCapability> GrantedCapabilities { get; private set; }
    public DateTime LoadedAt { get; private set; }
    public PluginConfiguration Configuration { get; private set; }

    // Domain Methods
    public Result Load(IReadOnlyList<PluginCapability> grantedCapabilities)
    public Result Unload()
    public Result UpdateConfiguration(PluginConfiguration config)
    public Result GrantCapability(PluginCapability capability)
    public Result RevokeCapability(PluginCapability capability)
}

public enum PluginStatus
{
    Discovered,
    Loading,
    Active,
    Paused,
    Error,
    Unloading
}
```

### **Layer 2: Application Layer (`NoteNest.Application.Plugins`)**

#### **CQRS Commands & Queries**

```csharp
// Plugin Management Commands
public class LoadPluginCommand : IRequest<Result<LoadPluginResult>>
{
    public PluginId PluginId { get; set; }
    public PluginLoadOptions Options { get; set; }
    public IReadOnlyList<PluginCapability> RequestedCapabilities { get; set; }
}

public class UnloadPluginCommand : IRequest<Result>
{
    public PluginId PluginId { get; set; }
    public bool Force { get; set; }
}

public class DiscoverPluginsCommand : IRequest<Result<DiscoverPluginsResult>>
{
    public string SearchPath { get; set; }
    public bool IncludeBuiltIn { get; set; } = true;
}

// Plugin Queries
public class GetLoadedPluginsQuery : IRequest<Result<IReadOnlyList<PluginSummary>>>
{
    public PluginCategory? FilterByCategory { get; set; }
    public PluginStatus? FilterByStatus { get; set; }
}

public class GetPluginCapabilitiesQuery : IRequest<Result<IReadOnlyList<PluginCapability>>>
{
    public PluginId PluginId { get; set; }
}
```

#### **Capability System**

```csharp
// Base Capability Contract
public abstract class PluginCapability : ValueObject
{
    public abstract string Name { get; }
    public abstract CapabilityRiskLevel RiskLevel { get; }
    public abstract string Description { get; }
}

public enum CapabilityRiskLevel
{
    Safe,        // UI, read-only data access
    Low,         // Event subscription, settings
    Medium,      // File system read, network
    High,        // File system write, database
    Critical     // System integration, external processes
}

// Core Capabilities
public class EventSubscriptionCapability : PluginCapability
{
    public IReadOnlyList<Type> AllowedEventTypes { get; }
    public override string Name => "EventSubscription";
    public override CapabilityRiskLevel RiskLevel => CapabilityRiskLevel.Low;
    
    public static EventSubscriptionCapability For<T>() where T : IDomainEvent
        => new(new[] { typeof(T) });
}

public class DataPersistenceCapability : PluginCapability
{
    public long MaxStorageSizeBytes { get; }
    public bool AllowBackup { get; }
    public override CapabilityRiskLevel RiskLevel => CapabilityRiskLevel.Medium;
}

public class NoteAccessCapability : PluginCapability
{
    public NoteAccessLevel AccessLevel { get; }
    public IReadOnlyList<CategoryId> AllowedCategories { get; }
    public override CapabilityRiskLevel RiskLevel => 
        AccessLevel == NoteAccessLevel.ReadOnly ? CapabilityRiskLevel.Low : CapabilityRiskLevel.High;
}

public class UIIntegrationCapability : PluginCapability
{
    public IReadOnlyList<UISlotType> AllowedSlots { get; }
    public override CapabilityRiskLevel RiskLevel => CapabilityRiskLevel.Safe;
}
```

#### **Plugin Application Services**

```csharp
public interface IPluginApplicationService
{
    Task<Result<LoadPluginResult>> LoadPluginAsync(LoadPluginCommand command);
    Task<Result> UnloadPluginAsync(UnloadPluginCommand command);
    Task<Result<IReadOnlyList<PluginSummary>>> GetLoadedPluginsAsync(GetLoadedPluginsQuery query);
    Task<Result> GrantCapabilityAsync(PluginId pluginId, PluginCapability capability);
    Task<Result> RevokeCapabilityAsync(PluginId pluginId, PluginCapability capability);
}

// Plugin Registry Management
public interface IPluginRegistry
{
    Task<Result> RegisterPluginAsync(IPluginManifest manifest);
    Task<Result> UnregisterPluginAsync(PluginId pluginId);
    Task<Result<IReadOnlyList<IPluginManifest>>> GetAvailablePluginsAsync();
    Task<Result<IPluginManifest>> GetPluginManifestAsync(PluginId pluginId);
}
```

### **Layer 3: Infrastructure Layer (`NoteNest.Infrastructure.Plugins`)**

#### **Plugin Isolation & Security**

```csharp
// Plugin Sandbox with AssemblyLoadContext
public class PluginSandbox : AssemblyLoadContext, IPluginSandbox
{
    private readonly PluginManifest _manifest;
    private readonly IPluginSecurityPolicy _securityPolicy;
    private readonly IAppLogger _logger;
    
    public PluginSandbox(PluginManifest manifest, IPluginSecurityPolicy securityPolicy) 
        : base($"Plugin_{manifest.Id}", isCollectible: true)
    {
        _manifest = manifest;
        _securityPolicy = securityPolicy;
    }

    protected override Assembly Load(AssemblyName name)
    {
        // Security validation before loading
        if (!_securityPolicy.IsAssemblyAllowed(name))
            throw new PluginSecurityException($"Assembly {name} not allowed");
            
        // Load with restricted permissions
        return LoadFromAssemblyPath(GetAssemblyPath(name));
    }

    public async Task<IPluginInstance> CreatePluginInstanceAsync()
    {
        var assembly = LoadFromAssemblyPath(_manifest.AssemblyPath);
        var pluginType = assembly.GetType(_manifest.EntryPointType);
        
        // Create with DI container integration
        var instance = await CreateInstanceWithDIAsync(pluginType);
        return new PluginInstance(instance, this);
    }
}

// Plugin Security Policy
public interface IPluginSecurityPolicy
{
    bool IsAssemblyAllowed(AssemblyName assemblyName);
    bool IsCapabilityAllowed(PluginId pluginId, PluginCapability capability);
    TimeSpan GetExecutionTimeout(PluginId pluginId);
    long GetMaxMemoryUsage(PluginId pluginId);
}
```

#### **Event Integration**

```csharp
// Enhanced Event Bus with Plugin Support
public class PluginAwareEventBus : IPluginEventBus
{
    private readonly IMediator _mediator;
    private readonly ConcurrentDictionary<PluginId, List<EventSubscription>> _subscriptions;
    private readonly IPluginSecurityPolicy _securityPolicy;
    private readonly IAppLogger _logger;

    public async Task<Result> SubscribeAsync<T>(
        IPluginContext pluginContext, 
        Func<T, Task> handler) where T : IDomainEvent
    {
        // Verify plugin has EventSubscriptionCapability
        var capability = pluginContext.GetCapability<EventSubscriptionCapability>();
        if (capability?.AllowedEventTypes?.Contains(typeof(T)) != true)
            return Result.Fail("Plugin lacks permission for this event type");

        // Create secure handler wrapper
        var secureHandler = CreateSecureHandler(pluginContext, handler);
        
        // Register with timeout and error handling
        var subscription = new EventSubscription<T>(pluginContext.Id, secureHandler);
        AddSubscription(subscription);
        
        return Result.Ok();
    }

    private Func<T, Task> CreateSecureHandler<T>(IPluginContext context, Func<T, Task> handler)
    {
        return async (eventData) =>
        {
            using var timeout = new CancellationTokenSource(_securityPolicy.GetExecutionTimeout(context.Id));
            
            try
            {
                await handler(eventData).WaitAsync(timeout.Token);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Plugin {context.Id} event handler failed");
                // Consider suspending plugin on repeated failures
                await HandlePluginErrorAsync(context.Id, ex);
            }
        };
    }
}
```

#### **Data Persistence**

```csharp
// Secure Plugin Data Store
public class PluginDataStore : IPluginDataStore
{
    private readonly string _pluginDataRoot;
    private readonly IAppLogger _logger;
    private readonly ConcurrentDictionary<PluginId, SemaphoreSlim> _pluginLocks;

    public PluginDataStore()
    {
        // Isolated directory: %LocalAppData%\NoteNest\.plugins
        _pluginDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NoteNest", ".plugins");
        EnsureSecureDirectory();
    }

    public async Task<Result<T>> LoadDataAsync<T>(IPluginContext context, string key) where T : class
    {
        // Verify plugin has DataPersistenceCapability
        var capability = context.GetCapability<DataPersistenceCapability>();
        if (capability == null)
            return Result.Fail<T>("Plugin lacks data persistence capability");

        var pluginDir = GetSecurePluginDirectory(context.Id);
        var filePath = Path.Combine(pluginDir, $"{SanitizeKey(key)}.json");

        // Atomic read with locking
        var lockObj = _pluginLocks.GetOrAdd(context.Id, _ => new SemaphoreSlim(1));
        await lockObj.WaitAsync();
        
        try
        {
            if (!File.Exists(filePath))
                return Result.Ok<T>(null);

            var json = await File.ReadAllTextAsync(filePath);
            var data = JsonSerializer.Deserialize<T>(json, GetSecureJsonOptions());
            
            return Result.Ok(data);
        }
        catch (Exception ex)
        {
            return Result.Fail<T>($"Failed to load plugin data: {ex.Message}");
        }
        finally
        {
            lockObj.Release();
        }
    }

    public async Task<Result> SaveDataAsync<T>(IPluginContext context, string key, T data) where T : class
    {
        var capability = context.GetCapability<DataPersistenceCapability>();
        if (capability == null)
            return Result.Fail("Plugin lacks data persistence capability");

        // Size limit enforcement
        var json = JsonSerializer.Serialize(data, GetSecureJsonOptions());
        if (json.Length > capability.MaxStorageSizeBytes)
            return Result.Fail($"Data exceeds maximum size limit: {capability.MaxStorageSizeBytes} bytes");

        var pluginDir = GetSecurePluginDirectory(context.Id);
        var filePath = Path.Combine(pluginDir, $"{SanitizeKey(key)}.json");

        // Atomic write with backup
        var lockObj = _pluginLocks.GetOrAdd(context.Id, _ => new SemaphoreSlim(1));
        await lockObj.WaitAsync();
        
        try
        {
            await WriteAtomicallyAsync(filePath, json);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to save plugin data: {ex.Message}");
        }
        finally
        {
            lockObj.Release();
        }
    }

    private string GetSecurePluginDirectory(PluginId pluginId)
    {
        var sanitizedId = SanitizePluginId(pluginId.Value);
        var pluginDir = Path.Combine(_pluginDataRoot, sanitizedId);
        
        Directory.CreateDirectory(pluginDir);
        
        // Set restrictive permissions (Windows)
        if (OperatingSystem.IsWindows())
        {
            var dirInfo = new DirectoryInfo(pluginDir);
            var security = dirInfo.GetAccessControl();
            // Add appropriate ACLs for security
        }
        
        return pluginDir;
    }
}
```

### **Layer 4: UI Layer (`NoteNest.UI.Plugins`)**

#### **Plugin UI Integration**

```csharp
// Plugin Panel Factory with MVVM
public interface IPluginPanelFactory
{
    Task<Result<IPluginPanel>> CreatePanelAsync(IPluginPanelDescriptor descriptor);
    Task<Result> RegisterPanelViewModelAsync<TViewModel>(PluginId pluginId) where TViewModel : class;
    Task<Result<UserControl>> CreateViewForViewModelAsync<TViewModel>(TViewModel viewModel);
}

// Plugin Panel Descriptor (ViewModel-based)
public class PluginPanelDescriptor
{
    public PluginId PluginId { get; set; }
    public string Title { get; set; }
    public string Icon { get; set; }
    public Type ViewModelType { get; set; }
    public UISlotType PreferredSlot { get; set; }
    public double PreferredWidth { get; set; } = 300;
    public double MinWidth { get; set; } = 200;
    public double MaxWidth { get; set; } = 600;
    public bool IsPinnable { get; set; } = true;
    public bool IsCloseable { get; set; } = true;
}

public enum UISlotType
{
    LeftPanel,
    RightPanel,
    BottomPanel,
    FloatingWindow,
    ToolbarButton,
    ContextMenu,
    StatusBar
}

// Plugin Panel Container
public class PluginPanelContainer : UserControl, IPluginPanel
{
    private readonly IPluginPanelDescriptor _descriptor;
    private readonly object _viewModel;
    private readonly IPluginContext _context;

    public PluginPanelContainer(IPluginPanelDescriptor descriptor, object viewModel, IPluginContext context)
    {
        _descriptor = descriptor;
        _viewModel = viewModel;
        _context = context;
        
        InitializeView();
        SetupSecurityBoundaries();
    }

    private void InitializeView()
    {
        // Create DataTemplate-based view for ViewModel
        var viewType = GetViewTypeForViewModel(_viewModel.GetType());
        if (viewType != null)
        {
            var view = Activator.CreateInstance(viewType) as FrameworkElement;
            view.DataContext = _viewModel;
            Content = view;
        }
        else
        {
            // Fallback to generic content presenter
            Content = new ContentPresenter { Content = _viewModel };
        }
    }

    private void SetupSecurityBoundaries()
    {
        // Prevent plugin from accessing parent window directly
        // Limit resource usage
        // Monitor for suspicious behavior
    }
}
```

#### **Plugin Host Integration**

```csharp
// Main Plugin Host Service
public class PluginHostService : IPluginHostService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPluginApplicationService _pluginService;
    private readonly IPluginPanelFactory _panelFactory;
    private readonly IAppLogger _logger;
    private readonly ConcurrentDictionary<PluginId, IPluginInstance> _loadedPlugins;

    public async Task<Result> InitializeAsync()
    {
        // Discover built-in plugins
        await DiscoverBuiltInPluginsAsync();
        
        // Load enabled plugins from settings
        await LoadEnabledPluginsAsync();
        
        // Setup plugin UI integration
        await SetupPluginUIAsync();
        
        return Result.Ok();
    }

    private async Task DiscoverBuiltInPluginsAsync()
    {
        var builtInPluginsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        if (Directory.Exists(builtInPluginsPath))
        {
            var command = new DiscoverPluginsCommand 
            { 
                SearchPath = builtInPluginsPath,
                IncludeBuiltIn = true 
            };
            
            await _pluginService.DiscoverPluginsAsync(command);
        }
    }

    public async Task<Result<IPluginPanel>> CreatePluginPanelAsync(PluginId pluginId)
    {
        if (!_loadedPlugins.TryGetValue(pluginId, out var pluginInstance))
            return Result.Fail<IPluginPanel>("Plugin not loaded");

        var descriptor = await pluginInstance.GetPanelDescriptorAsync();
        if (descriptor == null)
            return Result.Fail<IPluginPanel>("Plugin does not provide UI panel");

        // Verify plugin has UIIntegrationCapability
        var uiCapability = pluginInstance.Context.GetCapability<UIIntegrationCapability>();
        if (uiCapability?.AllowedSlots?.Contains(descriptor.PreferredSlot) != true)
            return Result.Fail<IPluginPanel>("Plugin lacks permission for UI slot");

        return await _panelFactory.CreatePanelAsync(descriptor);
    }
}
```

---

## 🔧 Plugin Development Contract

### **IPlugin Interface**

```csharp
// Modern Plugin Contract
public interface IPlugin : IDisposable
{
    PluginManifest Manifest { get; }
    
    Task<Result> InitializeAsync(IPluginContext context);
    Task<Result> ShutdownAsync();
    
    Task<IPluginPanelDescriptor> GetPanelDescriptorAsync();
    Task<IPluginConfiguration> GetConfigurationAsync();
    
    // Health monitoring
    Task<PluginHealthStatus> GetHealthAsync();
}

// Plugin Context with Security
public interface IPluginContext
{
    PluginId Id { get; }
    IServiceProvider ServiceProvider { get; } // Scoped to plugin
    IAppLogger Logger { get; }
    
    // Capability System
    T GetCapability<T>() where T : PluginCapability;
    Task<Result<T>> RequestCapabilityAsync<T>() where T : PluginCapability;
    
    // Secure Communication
    Task<Result> PublishEventAsync<T>(T domainEvent) where T : IDomainEvent;
    Task<Result> SubscribeToEventAsync<T>(Func<T, Task> handler) where T : IDomainEvent;
    
    // Data Access
    Task<Result<TData>> LoadDataAsync<TData>(string key) where TData : class;
    Task<Result> SaveDataAsync<TData>(string key, TData data) where TData : class;
}
```

### **Plugin Manifest**

```csharp
// Plugin Manifest (plugin.json)
public class PluginManifest
{
    public string Id { get; set; }
    public string Name { get; set; }
    public Version Version { get; set; }
    public string Description { get; set; }
    public string Author { get; set; }
    public string AssemblyPath { get; set; }
    public string EntryPointType { get; set; }
    public Version MinimumHostVersion { get; set; }
    public IReadOnlyList<PluginCapability> RequiredCapabilities { get; set; }
    public IReadOnlyList<string> Dependencies { get; set; }
    public PluginCategory Category { get; set; }
    public bool IsBuiltIn { get; set; }
    
    // Validation
    public ValidationResult Validate();
}
```

---

## 📋 Service Registration

### **DI Configuration**

```csharp
// Plugin System Service Registration
public static class PluginServiceExtensions
{
    public static IServiceCollection AddPluginSystem(this IServiceCollection services, IConfiguration configuration)
    {
        // Core Plugin Services
        services.AddSingleton<IPluginHostService, PluginHostService>();
        services.AddSingleton<IPluginRegistry, PluginRegistry>();
        services.AddSingleton<IPluginDataStore, PluginDataStore>();
        services.AddSingleton<IPluginSecurityPolicy, DefaultPluginSecurityPolicy>();
        
        // Application Services
        services.AddScoped<IPluginApplicationService, PluginApplicationService>();
        
        // Event Integration
        services.AddSingleton<IPluginEventBus>(provider =>
        {
            var mediator = provider.GetRequiredService<IMediator>();
            var securityPolicy = provider.GetRequiredService<IPluginSecurityPolicy>();
            var logger = provider.GetRequiredService<IAppLogger>();
            
            return new PluginAwareEventBus(mediator, securityPolicy, logger);
        });
        
        // UI Integration
        services.AddSingleton<IPluginPanelFactory, PluginPanelFactory>();
        
        // CQRS Handlers
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(LoadPluginCommand).Assembly);
        });
        
        // Configuration
        var pluginConfig = configuration.GetSection("Plugins").Get<PluginConfiguration>();
        services.AddSingleton(pluginConfig ?? new PluginConfiguration());
        
        return services;
    }
}
```

---

## 🚀 Performance & Security

### **Performance Targets**
- Plugin load time: <500ms
- Event handling overhead: <10ms
- UI integration: <100ms
- Memory per plugin: <50MB baseline
- Startup impact: <200ms total

### **Security Features**
- Assembly load context isolation
- Capability-based permissions
- Resource usage limits
- Execution timeouts
- Sandboxed file system access
- Event subscription validation

### **Monitoring & Diagnostics**
- Plugin health checks
- Resource usage tracking
- Performance metrics
- Error reporting
- Automatic plugin suspension on failures

---

## 🎯 Implementation Phases

### **Phase 1: Foundation (2 weeks)**
- Domain models and value objects
- Basic plugin loading infrastructure
- Simple capability system
- File-based plugin data store

### **Phase 2: CQRS Integration (1 week)**
- MediatR command/query handlers
- Event bus integration
- Plugin application services

### **Phase 3: Security & Isolation (2 weeks)**
- AssemblyLoadContext sandboxing
- Capability validation
- Resource limits and monitoring
- Security policies

### **Phase 4: UI Integration (1 week)**
- Plugin panel factory
- ViewModel-based UI integration
- Main window integration points

### **Phase 5: Advanced Features (1 week)**
- Plugin discovery and registry
- Configuration management
- Health monitoring
- Error handling and recovery

---

## ✅ Success Criteria

**Functional:**
- Can load/unload plugins dynamically
- Plugins isolated from core application
- UI integration without tight coupling
- Robust error handling and recovery

**Performance:**
- Zero impact on core app when no plugins loaded
- Minimal overhead when plugins active
- Fast plugin load/unload cycles

**Security:**
- Plugins cannot access unauthorized resources
- Capability system prevents privilege escalation
- Sandboxing protects host application

**Maintainability:**
- Clear separation of concerns
- Well-defined plugin contracts
- Comprehensive testing infrastructure
- Good documentation and examples

This architecture provides a solid foundation for the Todo plugin while maintaining the flexibility for future plugin development. The capability-based security model ensures plugins can only access what they need, and the Clean Architecture alignment ensures long-term maintainability.
