using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MediatR;
using FluentValidation;
using NoteNest.Domain.Common;
using NoteNest.Domain.Categories;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Application.Common.Behaviors;
using NoteNest.Application.Notes.Commands.CreateNote;
using NoteNest.Infrastructure.Persistence.Repositories;
using NoteNest.Infrastructure.Services;
using NoteNest.Infrastructure.EventBus;
using NoteNest.UI.ViewModels.Shell;
using NoteNest.UI.ViewModels.Categories;
using NoteNest.UI.ViewModels.Notes;
using NoteNest.UI.ViewModels.Workspace;
using NoteNest.UI.Services;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Services;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NoteNest.Infrastructure.Database;
// ConfigurationService is in NoteNest.Core.Services namespace, already included above

namespace NoteNest.UI.Composition
{
    public static class ServiceConfiguration
    {
        public static IServiceCollection ConfigureServices(this IServiceCollection services, IConfiguration configuration)
        {
            // =============================================================================
            // CHOOSE ARCHITECTURE: DATABASE vs LEGACY
            // =============================================================================
            
            var useDatabaseArchitecture = configuration.GetValue<bool>("FeatureFlags:UseDatabaseArchitecture", true);
            
            if (useDatabaseArchitecture)
            {
                return ConfigureDatabaseArchitecture(services, configuration);
            }
            else
            {
                return ConfigureLegacyArchitecture(services, configuration);
            }
        }
        
        /// <summary>
        /// NEW DATABASE ARCHITECTURE - Full TreeNode implementation with SQLite
        /// </summary>
        private static IServiceCollection ConfigureDatabaseArchitecture(IServiceCollection services, IConfiguration configuration)
        {
            // MediatR with enhanced pipeline
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(typeof(CreateNoteCommand).Assembly);
            });
            
            // Enhanced pipeline behaviors
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            
            // Validation
            services.AddValidatorsFromAssembly(typeof(CreateNoteCommand).Assembly);
            
            // Configuration
            services.AddSingleton(configuration);
            
            // =============================================================================
            // DATABASE SERVICES - ENTERPRISE ARCHITECTURE ACTIVE!
            // =============================================================================
            
            // Database paths (LOCAL AppData - not synced)
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var databasePath = Path.Combine(localAppData, "NoteNest");
            Directory.CreateDirectory(databasePath);
            
            var treeDbPath = Path.Combine(databasePath, "tree.db");
            var stateDbPath = Path.Combine(databasePath, "state.db");
            
            // Connection strings with performance optimization
            var treeConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = treeDbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true,
                DefaultTimeout = 30
            }.ToString();
            
            // Database services
            services.AddSingleton(provider => new TreeDatabaseConnection(treeConnectionString));
            
            services.AddSingleton<ITreeDatabaseInitializer>(provider => 
                new TreeDatabaseInitializer(treeConnectionString, provider.GetRequiredService<IAppLogger>()));
            
            var notesRootPath = configuration.GetValue<string>("NotesPath") 
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoteNest");
            
            services.AddSingleton<ITreeDatabaseRepository>(provider => 
                new TreeDatabaseRepository(treeConnectionString, provider.GetRequiredService<IAppLogger>(), notesRootPath));
            
            services.AddSingleton<IHashCalculationService, HashCalculationService>();
            
            services.AddSingleton<IDatabaseBackupService>(provider =>
                new DatabaseBackupService(
                    treeConnectionString,
                    provider.GetRequiredService<ITreeDatabaseRepository>(),
                    provider.GetRequiredService<IAppLogger>()));
            
            services.AddSingleton<ITreeMigrationService>(provider =>
                new TreeMigrationService(
                    provider.GetRequiredService<ITreeDatabaseRepository>(),
                    configuration,
                    provider.GetRequiredService<IAppLogger>()));
            
            services.AddSingleton<ITreePerformanceMonitor, TreePerformanceMonitor>();
            
            // Register hosted services for automatic database operations
            services.AddHostedService<DatabaseBackupService>();
            services.AddHostedService<DatabaseInitializationHostedService>();
            services.AddHostedService<DatabaseMaintenanceService>();
            
            // =============================================================================
            // ENHANCED REPOSITORIES (TreeNode-based) - ACTIVE!
            // =============================================================================
            
            services.AddScoped<INoteRepository, NoteNest.Infrastructure.Database.Adapters.TreeNodeNoteRepository>();
            services.AddScoped<ICategoryRepository, NoteNest.Infrastructure.Database.Adapters.TreeNodeCategoryRepository>();
            
            // =============================================================================
            // INFRASTRUCTURE SERVICES
            // =============================================================================
            
            services.AddSingleton<NoteNest.Application.Common.Interfaces.IEventBus, InMemoryEventBus>();
            services.AddScoped<IFileService, FileService>();
            
            // Core services
            services.AddSingleton<IAppLogger, ConsoleAppLogger>();
            services.AddSingleton<IFileSystemProvider, NoteNest.Core.Services.DefaultFileSystemProvider>();
            services.AddSingleton<ConfigurationService>();
            
            // UI services
            services.AddScoped<IDialogService, DialogService>();
            
            // =============================================================================
            // ENHANCED VIEWMODELS (TreeNode-aware) - Ready for tree view integration
            // =============================================================================
            
            // Use existing ViewModels - they'll automatically use the new TreeNode repositories
            services.AddTransient<MainShellViewModel>();
            services.AddTransient<CategoryTreeViewModel>();
            services.AddTransient<NoteOperationsViewModel>();
            services.AddTransient<CategoryOperationsViewModel>();
            services.AddTransient<ModernWorkspaceViewModel>();
            
            return services;
        }
        
        /// <summary>
        /// LEGACY ARCHITECTURE - File system based (WORKING - PRESERVED FOR ROLLBACK)
        /// </summary>
        private static IServiceCollection ConfigureLegacyArchitecture(IServiceCollection services, IConfiguration configuration)
        {
            // LEGACY - WORKING SYSTEM - Keep for rollback safety
            
            // MediatR
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(typeof(CreateNoteCommand).Assembly);
            });
            
            // Add pipeline behaviors  
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            
            // Validation
            services.AddValidatorsFromAssembly(typeof(CreateNoteCommand).Assembly);
            
            // Configuration
            services.AddSingleton(configuration);
            
            // LEGACY REPOSITORIES (file system based)
            services.AddScoped<INoteRepository, NoteNest.Infrastructure.Persistence.Repositories.FileSystemNoteRepository>();
            services.AddScoped<ICategoryRepository, FileSystemCategoryRepository>();
            
            // Infrastructure services
            services.AddSingleton<NoteNest.Application.Common.Interfaces.IEventBus, InMemoryEventBus>();
            services.AddScoped<IFileService, FileService>();
            
            // Core services
            services.AddSingleton<IAppLogger, ConsoleAppLogger>();
            services.AddSingleton<IFileSystemProvider, NoteNest.Core.Services.DefaultFileSystemProvider>();
            
            // UI services
            services.AddScoped<IDialogService, DialogService>();
            
            // LEGACY VIEWMODELS (working)
            services.AddTransient<MainShellViewModel>();
            services.AddTransient<CategoryTreeViewModel>();
            services.AddTransient<NoteOperationsViewModel>();
            services.AddTransient<CategoryOperationsViewModel>();
            services.AddTransient<ModernWorkspaceViewModel>();
            
            return services;
        }
    }
    
    // Temporary implementation for legacy architecture
    public class FileSystemCategoryRepository : ICategoryRepository
    {
        private readonly IAppLogger _logger;
        
        public FileSystemCategoryRepository(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<Category> GetByIdAsync(CategoryId id)
        {
            await Task.CompletedTask;
            return new Category(id, "Test Category", @"C:\Test\Category", null);
        }

        public async Task<IReadOnlyList<Category>> GetAllAsync()
        {
            await Task.CompletedTask;
            return new List<Category>().AsReadOnly();
        }

        public async Task<IReadOnlyList<Category>> GetRootCategoriesAsync()
        {
            await Task.CompletedTask;
            return new List<Category>
            {
                new Category(CategoryId.Create(), "Documents", @"C:\Documents", null),
                new Category(CategoryId.Create(), "Projects", @"C:\Projects", null)
            }.AsReadOnly();
        }

        public async Task<Result> CreateAsync(Category category)
        {
            await Task.CompletedTask;
            return Result.Ok();
        }

        public async Task<Result> UpdateAsync(Category category)
        {
            await Task.CompletedTask;
            return Result.Ok();
        }

        public async Task<Result> DeleteAsync(CategoryId id)
        {
            await Task.CompletedTask;
            return Result.Ok();
        }

        public async Task<bool> ExistsAsync(CategoryId id)
        {
            await Task.CompletedTask;
            return false;
        }
    }
}
