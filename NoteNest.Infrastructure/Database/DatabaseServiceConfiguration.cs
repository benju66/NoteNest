using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.Sqlite;
using NoteNest.Core.Services.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace NoteNest.Infrastructure.Database
{
    /// <summary>
    /// Database service configuration for the new TreeNode architecture.
    /// Provides complete database setup with backup, recovery, and migration services.
    /// </summary>
    public static class DatabaseServiceConfiguration
    {
        public static IServiceCollection AddTreeDatabaseServices(
            this IServiceCollection services, 
            IConfiguration configuration)
        {
            // =============================================================================
            // DATABASE PATHS & CONNECTION STRINGS
            // =============================================================================
            
            // Use LOCAL AppData to avoid cloud sync corruption of databases
            var localAppDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NoteNest");
            
            // Ensure database directory exists
            Directory.CreateDirectory(localAppDataPath);
            
            var treeDbPath = Path.Combine(localAppDataPath, "tree.db");
            var stateDbPath = Path.Combine(localAppDataPath, "state.db");
            
            // Optimized connection strings for performance
            var treeConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = treeDbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true,
                DefaultTimeout = 30,
            }.ToString();
            
            var stateConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = stateDbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = true,
                DefaultTimeout = 15
            }.ToString();
            
            // =============================================================================
            // CORE DATABASE SERVICES
            // =============================================================================
            
            // Register connection strings as configuration
            services.AddSingleton(new TreeDatabaseConnection(treeConnectionString));
            services.AddSingleton(new StateDatabaseConnection(stateConnectionString));
            
            // Database initialization and schema management
            services.AddSingleton<ITreeDatabaseInitializer>(provider => 
                new TreeDatabaseInitializer(treeConnectionString, provider.GetRequiredService<IAppLogger>()));
            
            // Main repository with notes root path
            var notesRootPath = configuration.GetValue<string>("NotesPath") 
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoteNest");
            
            services.AddSingleton<ITreeDatabaseRepository>(provider => 
                new TreeDatabaseRepository(
                    treeConnectionString, 
                    provider.GetRequiredService<IAppLogger>(), 
                    notesRootPath));
            
            // =============================================================================
            // SUPPORTING SERVICES
            // =============================================================================
            
            // Hash calculation for change detection
            services.AddSingleton<IHashCalculationService, HashCalculationService>();
            
            // Backup and recovery services
            services.AddSingleton<IDatabaseBackupService>(provider =>
                new DatabaseBackupService(
                    treeConnectionString,
                    provider.GetRequiredService<ITreeDatabaseRepository>(),
                    provider.GetRequiredService<IAppLogger>()));
            
            // Migration from legacy system
            services.AddSingleton<ITreeMigrationService>(provider =>
                new TreeMigrationService(
                    provider.GetRequiredService<ITreeDatabaseRepository>(),
                    configuration,
                    provider.GetRequiredService<IAppLogger>()));
            
            // Performance monitoring
            services.AddSingleton<ITreePerformanceMonitor, TreePerformanceMonitor>();
            
            // =============================================================================
            // HOSTED SERVICES FOR AUTOMATIC OPERATIONS
            // =============================================================================
            
            // Backup service runs automatic backups and integrity checks
            services.AddHostedService<DatabaseBackupService>();
            
            // Database initialization service ensures database is ready on startup
            services.AddHostedService<DatabaseInitializationHostedService>();
            
            // Optional: Database maintenance service for optimization
            services.AddHostedService<DatabaseMaintenanceService>();
            
            return services;
        }
    }
    
    // =============================================================================
    // CONNECTION STRING WRAPPERS
    // =============================================================================
    
    public class TreeDatabaseConnection
    {
        public string ConnectionString { get; }
        
        public TreeDatabaseConnection(string connectionString)
        {
            ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }
    }
    
    public class StateDatabaseConnection
    {
        public string ConnectionString { get; }
        
        public StateDatabaseConnection(string connectionString)
        {
            ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }
    }
    
    // =============================================================================
    // DATABASE INITIALIZATION HOSTED SERVICE
    // =============================================================================
    
    /// <summary>
    /// Hosted service that ensures database is initialized and migrated on application startup.
    /// </summary>
    public class DatabaseInitializationHostedService : IHostedService
    {
        private readonly ITreeDatabaseInitializer _initializer;
        private readonly ITreeMigrationService _migrationService;
        private readonly IAppLogger _logger;
        
        public DatabaseInitializationHostedService(
            ITreeDatabaseInitializer initializer,
            ITreeMigrationService migrationService,
            IAppLogger logger)
        {
            _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
            _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.Info("Initializing TreeDatabase system...");
                
                // Step 1: Initialize database schema
                if (!await _initializer.InitializeAsync())
                {
                    throw new InvalidOperationException("Failed to initialize database");
                }
                
                // Step 2: Check if migration from legacy system is needed
                if (await _migrationService.IsMigrationNeededAsync())
                {
                    _logger.Info("Legacy system detected, starting migration...");
                    
                    var migrationResult = await _migrationService.MigrateFromLegacyAsync();
                    
                    if (migrationResult.Success)
                    {
                        _logger.Info($"Migration completed successfully: {migrationResult.Message}");
                    }
                    else
                    {
                        _logger.Error($"Migration failed: {migrationResult.Message}");
                        // Continue anyway - database can work with empty state
                    }
                }
                
                // Step 3: Verify database health
                if (!await _initializer.IsHealthyAsync())
                {
                    _logger.Warning("Database health check failed, may need recovery");
                }
                
                _logger.Info("TreeDatabase system initialization completed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize TreeDatabase system");
                throw;
            }
        }
        
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.Info("TreeDatabase initialization service stopping");
            return Task.CompletedTask;
        }
    }
    
    // =============================================================================
    // DATABASE MAINTENANCE HOSTED SERVICE
    // =============================================================================
    
    /// <summary>
    /// Background service for database maintenance, optimization, and cleanup.
    /// </summary>
    public class DatabaseMaintenanceService : BackgroundService
    {
        private readonly ITreeDatabaseRepository _repository;
        private readonly IAppLogger _logger;
        private Timer _maintenanceTimer;
        
        public DatabaseMaintenanceService(
            ITreeDatabaseRepository repository,
            IAppLogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Schedule maintenance for every Sunday at 2 AM
                var timeUntilNextSunday2AM = CalculateTimeUntilNextSunday2AM();
                
                _maintenanceTimer = new Timer(
                    async _ => await PerformMaintenanceAsync(),
                    null,
                    timeUntilNextSunday2AM,
                    TimeSpan.FromDays(7));
                
                _logger.Info($"Database maintenance scheduled for every Sunday at 2 AM (next: {DateTime.Now.Add(timeUntilNextSunday2AM):yyyy-MM-dd HH:mm})");
                
                // Wait for cancellation
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
        }
        
        private async Task PerformMaintenanceAsync()
        {
            try
            {
                _logger.Info("Starting scheduled database maintenance...");
                
                // Optimize database (ANALYZE, VACUUM, cleanup)
                await _repository.OptimizeAsync();
                
                // Purge old soft-deleted items
                var purgedCount = await _repository.PurgeDeletedNodesAsync(daysOld: 30);
                if (purgedCount)
                {
                    _logger.Info($"Purged {purgedCount} old deleted items");
                }
                
                // Full vacuum for space reclamation
                await _repository.VacuumAsync();
                
                _logger.Info("Scheduled database maintenance completed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Scheduled database maintenance failed");
            }
        }
        
        private TimeSpan CalculateTimeUntilNextSunday2AM()
        {
            var now = DateTime.Now;
            var nextSunday = now.Date;
            
            // Find next Sunday
            while (nextSunday.DayOfWeek != DayOfWeek.Sunday)
            {
                nextSunday = nextSunday.AddDays(1);
            }
            
            // If it's already Sunday and past 2 AM, go to next Sunday
            if (nextSunday.Date == now.Date && now.Hour >= 2)
            {
                nextSunday = nextSunday.AddDays(7);
            }
            
            var nextMaintenanceTime = nextSunday.AddHours(2); // 2 AM
            return nextMaintenanceTime - now;
        }
        
        public override void Dispose()
        {
            _maintenanceTimer?.Dispose();
            base.Dispose();
        }
    }
}