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
    
    // Hosted service for database initialization
    public class DatabaseInitializationHostedService : IHostedService
    {
        private readonly ITreeDatabaseInitializer _initializer;
        private readonly IAppLogger _logger;
        
        public DatabaseInitializationHostedService(ITreeDatabaseInitializer initializer, IAppLogger logger)
        {
            _initializer = initializer;
            _logger = logger;
        }
        
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.Info("Initializing tree database...");
                await _initializer.InitializeAsync();
                _logger.Info("Tree database initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize tree database");
                throw;
            }
        }
        
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
    
    // Hosted service for database maintenance
    public class DatabaseMaintenanceService : IHostedService
    {
        private readonly ITreeDatabaseRepository _repository;
        private readonly IAppLogger _logger;
        private Timer _maintenanceTimer;
        
        public DatabaseMaintenanceService(ITreeDatabaseRepository repository, IAppLogger logger)
        {
            _repository = repository;
            _logger = logger;
        }
        
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Starting database maintenance service...");
            
            // Run maintenance every 6 hours
            _maintenanceTimer = new Timer(
                DoMaintenance, 
                null, 
                TimeSpan.FromHours(1), // Initial delay
                TimeSpan.FromHours(6)  // Recurring interval
            );
            
            return Task.CompletedTask;
        }
        
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _maintenanceTimer?.Dispose();
            _logger.Info("Database maintenance service stopped");
            return Task.CompletedTask;
        }
        
        private async void DoMaintenance(object state)
        {
            try
            {
                _logger.Info("Running database maintenance...");
                await _repository.VacuumAsync();
                await _repository.PurgeDeletedNodesAsync();
                _logger.Info("Database maintenance completed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Database maintenance failed");
            }
        }
    }
}