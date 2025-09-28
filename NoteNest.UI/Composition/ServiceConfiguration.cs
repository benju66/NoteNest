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
using Microsoft.Extensions.Caching.Memory;
using NoteNest.Infrastructure.Database.Services;
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
        /// 🚀 LIGHTNING-FAST DATABASE ARCHITECTURE - Scorched Earth Tree View Replacement
        /// Replaces slow file system scanning with < 50ms database queries
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
            // ENTERPRISE DATABASE FOUNDATION
            // =============================================================================
            
            // Database paths (LOCAL AppData - not synced)
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var databasePath = Path.Combine(localAppData, "NoteNest");
            Directory.CreateDirectory(databasePath);
            
            var treeDbPath = Path.Combine(databasePath, "tree.db");
            
            // Optimized connection string
            var treeConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = treeDbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true,
                DefaultTimeout = 30
            }.ToString();
            
            var notesRootPath = configuration.GetValue<string>("NotesPath") 
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoteNest");
            
            // Core database services
            services.AddSingleton<ITreeDatabaseInitializer>(provider => 
                new TreeDatabaseInitializer(treeConnectionString, provider.GetRequiredService<IAppLogger>()));
            
            services.AddSingleton<ITreeDatabaseRepository>(provider => 
                new TreeDatabaseRepository(treeConnectionString, provider.GetRequiredService<IAppLogger>(), notesRootPath));
            
            services.AddSingleton<ITreePopulationService, TreePopulationService>();
            
            // Memory caching for performance
            services.AddMemoryCache();
            
            // =============================================================================
            // 🚀 LIGHTNING-FAST DATABASE REPOSITORIES (Scorched Earth Replacement)
            // =============================================================================
            
            services.AddScoped<ICategoryRepository>(provider => 
                new NoteNest.Infrastructure.Database.Services.CategoryTreeDatabaseService(
                    provider.GetRequiredService<ITreeDatabaseRepository>(),
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<IMemoryCache>(),
                    notesRootPath));
            
            services.AddScoped<INoteRepository>(provider => 
                new NoteNest.Infrastructure.Database.Services.NoteTreeDatabaseService(
                    provider.GetRequiredService<ITreeDatabaseRepository>(),
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<IMemoryCache>()));
            
            // =============================================================================
            // FILE SYSTEM SYNCHRONIZATION
            // =============================================================================
            
            services.AddSingleton<IDatabaseFileWatcherService>(provider =>
                new DatabaseFileWatcherService(
                    provider.GetRequiredService<ITreeDatabaseRepository>(),
                    provider.GetRequiredService<IMemoryCache>(),
                    provider.GetRequiredService<IAppLogger>(),
                    notesRootPath));
            
            // Auto-start file watcher as hosted service
            services.AddHostedService<DatabaseFileWatcherService>();
            
            // Database initialization service
            services.AddHostedService<TreeNodeInitializationService>();
            
            // =============================================================================
            // INFRASTRUCTURE SERVICES
            // =============================================================================
            
            services.AddSingleton<NoteNest.Application.Common.Interfaces.IEventBus, InMemoryEventBus>();
            services.AddScoped<IFileService, FileService>();
            
            // Core services
            services.AddSingleton<IAppLogger, ConsoleAppLogger>();
            services.AddSingleton<IFileSystemProvider, NoteNest.Core.Services.DefaultFileSystemProvider>();
            services.AddSingleton<ConfigurationService>();
            
            // =============================================================================
            // RTF EDITOR SERVICES - Sophisticated RTF Editor Integration
            // =============================================================================
            
            // RTF Save Manager (for NoteTabItem RTF content saving)
            services.AddSingleton<ISaveManager>(provider =>
            {
                var dataPath = notesRootPath; // Use same notes path
                var statusNotifier = new NoteNest.Core.Services.BasicStatusNotifier(provider.GetRequiredService<IAppLogger>());
                return new NoteNest.Core.Services.RTFIntegratedSaveEngine(dataPath, statusNotifier);
            });
            
            // Note service for RTF content operations
            services.AddScoped<NoteService>();
            
            // Metadata manager for RTF notes
            services.AddScoped<NoteNest.Core.Services.NoteMetadataManager>();
            
            // UI services
            services.AddScoped<IDialogService, DialogService>();
            
            // =============================================================================
            // 🎯 CLEAN ARCHITECTURE VIEWMODELS (Enhanced with Database Performance)
            // =============================================================================
            
            services.AddTransient<MainShellViewModel>();
            services.AddTransient<CategoryTreeViewModel>();  // ⚡ Now database-powered!
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
            services.AddScoped<INoteRepository>(provider => 
                new NoteNest.Infrastructure.Persistence.Repositories.FileSystemNoteRepository(
                    provider.GetRequiredService<IFileSystemProvider>(),
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<IConfiguration>()));
            services.AddScoped<ICategoryRepository>(provider => 
                new FileSystemCategoryRepository(
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<IConfiguration>()));
            
            // Infrastructure services
            services.AddSingleton<NoteNest.Application.Common.Interfaces.IEventBus, InMemoryEventBus>();
            services.AddScoped<IFileService, FileService>();
            
            // Core services
            services.AddSingleton<IAppLogger, ConsoleAppLogger>();
            services.AddSingleton<IFileSystemProvider, NoteNest.Core.Services.DefaultFileSystemProvider>();
            services.AddSingleton<ConfigurationService>();
            
            // =============================================================================
            // RTF EDITOR SERVICES - Sophisticated RTF Editor Integration (Legacy)
            // =============================================================================
            
            var notesRootPath = configuration.GetValue<string>("NotesPath") 
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoteNest");
            
            // RTF Save Manager (for NoteTabItem RTF content saving)
            services.AddSingleton<ISaveManager>(provider =>
            {
                var dataPath = notesRootPath; // Use same notes path
                var statusNotifier = new NoteNest.Core.Services.BasicStatusNotifier(provider.GetRequiredService<IAppLogger>());
                return new NoteNest.Core.Services.RTFIntegratedSaveEngine(dataPath, statusNotifier);
            });
            
            // Note service for RTF content operations
            services.AddScoped<NoteService>();
            
            // Metadata manager for RTF notes
            services.AddScoped<NoteNest.Core.Services.NoteMetadataManager>();
            
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
    
    // Enhanced FileSystemCategoryRepository that scans real directory structure
    public class FileSystemCategoryRepository : ICategoryRepository
    {
        private readonly IAppLogger _logger;
        private readonly IConfiguration _configuration;
        private readonly string _rootPath;
        
        public FileSystemCategoryRepository(IAppLogger logger, IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            
            // Get NotesPath with validation and fallback
            _rootPath = configuration.GetValue<string>("NotesPath");
            if (string.IsNullOrWhiteSpace(_rootPath))
            {
                _rootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MyNotes");
                _logger.Warning($"NotesPath not configured, using default: {_rootPath}");
            }
            
            // Validate and create directory
            try
            {
                if (!Directory.Exists(_rootPath))
                {
                    Directory.CreateDirectory(_rootPath);
                    _logger.Info($"Created notes directory: {_rootPath}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to create notes directory: {_rootPath}");
                // Use a safe fallback directory
                _rootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoteNest");
                Directory.CreateDirectory(_rootPath);
            }
                
            _logger.Info($"FileSystemCategoryRepository initialized with root path: {_rootPath}");
        }

        public async Task<Category> GetByIdAsync(CategoryId id)
        {
            await Task.CompletedTask;
            
            // Parse the path from the ID (we use path-based IDs)
            var path = id.Value;
            if (Directory.Exists(path))
            {
                var dirInfo = new DirectoryInfo(path);
                var parentPath = dirInfo.Parent?.FullName;
                var parentId = !string.IsNullOrEmpty(parentPath) && parentPath != _rootPath 
                    ? CategoryId.From(parentPath) 
                    : null;
                    
                return new Category(id, dirInfo.Name, path, parentId);
            }
            
            return null;
        }

        public async Task<IReadOnlyList<Category>> GetAllAsync()
        {
            await Task.CompletedTask;
            return await ScanAllDirectoriesAsync();
        }

        public async Task<IReadOnlyList<Category>> GetRootCategoriesAsync()
        {
            await Task.CompletedTask;
            
            try
            {
                if (!Directory.Exists(_rootPath))
                {
                    _logger.Warning($"Notes root path does not exist: {_rootPath}");
                    Directory.CreateDirectory(_rootPath);
                }
                
                var rootCategories = new List<Category>();
                var rootDirInfo = new DirectoryInfo(_rootPath);
                
                // Get immediate subdirectories
                var subdirectories = rootDirInfo.GetDirectories()
                    .Where(d => !d.Name.StartsWith(".") && !d.Attributes.HasFlag(FileAttributes.Hidden))
                    .OrderBy(d => d.Name);
                
                foreach (var subdir in subdirectories)
                {
                    var categoryId = CategoryId.From(subdir.FullName);
                    var category = new Category(categoryId, subdir.Name, subdir.FullName, null);
                    rootCategories.Add(category);
                }
                
                _logger.Info($"Found {rootCategories.Count} root categories in: {_rootPath}");
                return rootCategories.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to scan root categories from: {_rootPath}");
                return new List<Category>().AsReadOnly();
            }
        }

        public async Task<Result> CreateAsync(Category category)
        {
            await Task.CompletedTask;
            try
            {
                Directory.CreateDirectory(category.Path);
                _logger.Info($"Created category directory: {category.Path}");
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to create category: {category.Path}");
                return Result.Fail($"Failed to create category: {ex.Message}");
            }
        }

        public async Task<Result> UpdateAsync(Category category)
        {
            await Task.CompletedTask;
            // Category updates would typically involve directory renames
            return Result.Ok();
        }

        public async Task<Result> DeleteAsync(CategoryId id)
        {
            await Task.CompletedTask;
            try
            {
                var path = id.Value;
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                    _logger.Info($"Deleted category directory: {path}");
                }
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to delete category: {id.Value}");
                return Result.Fail($"Failed to delete category: {ex.Message}");
            }
        }

        public async Task<bool> ExistsAsync(CategoryId id)
        {
            await Task.CompletedTask;
            return Directory.Exists(id.Value);
        }
        
        private async Task<IReadOnlyList<Category>> ScanAllDirectoriesAsync()
        {
            try
            {
                var allCategories = new List<Category>();
                await ScanDirectoryRecursive(_rootPath, null, allCategories);
                return allCategories.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to scan all directories");
                return new List<Category>().AsReadOnly();
            }
        }
        
        private async Task ScanDirectoryRecursive(string path, CategoryId parentId, List<Category> categories)
        {
            try
            {
                var dirInfo = new DirectoryInfo(path);
                if (!dirInfo.Exists) return;
                
                var categoryId = CategoryId.From(path);
                var category = new Category(categoryId, dirInfo.Name, path, parentId);
                categories.Add(category);
                
                // Scan subdirectories
                var subdirectories = dirInfo.GetDirectories()
                    .Where(d => !d.Name.StartsWith(".") && !d.Attributes.HasFlag(FileAttributes.Hidden));
                
                foreach (var subdir in subdirectories)
                {
                    await ScanDirectoryRecursive(subdir.FullName, categoryId, categories);
                }
            }
            catch (UnauthorizedAccessException)
            {
                _logger.Warning($"Access denied to directory: {path}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error scanning directory: {path}");
            }
        }
    }
}
