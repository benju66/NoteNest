using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Data.Sqlite;
using MediatR;
using FluentValidation;
using NoteNest.Domain.Common;
using NoteNest.Domain.Categories;
using NoteNest.Application.Common.Behaviors;
using NoteNest.Application.Notes.Commands.CreateNote;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Infrastructure.Persistence.Repositories;
using NoteNest.Infrastructure.Services;
using NoteNest.Infrastructure.EventBus;
using NoteNest.Infrastructure.Database;
using NoteNest.Infrastructure.Database.Services;
using NoteNest.UI.ViewModels.Shell;
using NoteNest.UI.ViewModels.Categories;
using NoteNest.UI.ViewModels.Notes;
using NoteNest.UI.ViewModels.Workspace;
using NoteNest.UI.ViewModels;
using NoteNest.UI.Services;
using NoteNest.UI.Interfaces;
using NoteNest.Core.Services.Logging;
using NoteNest.Core.Configuration;
using NoteNest.Core.Interfaces;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Services;
using NoteNest.Core.Services.Implementation;
using NoteNest.Core.Interfaces.Search;
using NoteNest.Core.Services.Search;

namespace NoteNest.UI.Composition
{
    public static class ServiceConfiguration
    {
        public static IServiceCollection ConfigureServices(this IServiceCollection services, IConfiguration configuration)
        {
            // =============================================================================
            // ARCHITECTURE SELECTION: DATABASE vs LEGACY
            // =============================================================================
            
            var useDatabaseArchitecture = configuration.GetValue<bool>("FeatureFlags:UseDatabaseArchitecture", true);
            
            // FIXED: Force database architecture (config loading issue resolved)
            if (true) // Ensure database architecture is always used
            {
                return ConfigureDatabaseArchitecture(services, configuration);
            }
            else
            {
                return ConfigureLegacyArchitecture(services, configuration);
            }
        }
        
        /// <summary>
        /// 🚀 STRUCTURED DATABASE ARCHITECTURE - Explicit DI Registration
        /// </summary>
        private static IServiceCollection ConfigureDatabaseArchitecture(IServiceCollection services, IConfiguration configuration)
        {
            // =============================================================================
            // SECTION 1: CORE INFRASTRUCTURE & LOGGING
            // =============================================================================
            
            // Logging - Foundation for everything
            services.AddSingleton<IAppLogger, ConsoleAppLogger>();
            
            // File System Provider
            services.AddSingleton<IFileSystemProvider, NoteNest.Core.Services.DefaultFileSystemProvider>();
            
            // Event Bus for application-wide events
            services.AddSingleton<NoteNest.Application.Common.Interfaces.IEventBus, InMemoryEventBus>();
            services.AddScoped<IFileService, FileService>();
            
            // Configuration Service (legacy compatibility)
            services.AddSingleton<ConfigurationService>();
            
            // Configuration
            services.AddSingleton(configuration);
            
            // =============================================================================
            // SECTION 2: CONFIGURATION & OPTIONS
            // =============================================================================
            
            // Database paths configuration
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
            
            // Storage Options Configuration
            services.AddSingleton<NoteNest.Core.Configuration.IStorageOptions>(provider =>
            {
                var notesPath = NoteNest.Core.Services.FirstTimeSetupService.ConfiguredNotesPath;
                if (string.IsNullOrWhiteSpace(notesPath))
                {
                    notesPath = notesRootPath;
                }

                var storageOptions = NoteNest.Core.Configuration.StorageOptions.FromNotesPath(notesPath);
                storageOptions.ValidatePaths();

                // Keep legacy PathService in sync for existing components
                NoteNest.Core.Services.PathService.RootPath = storageOptions.NotesPath;

                return storageOptions;
            });

            // Search Options Configuration
            services.AddSingleton<NoteNest.Core.Configuration.ISearchOptions>(provider =>
            {
                var storageOptions = provider.GetRequiredService<NoteNest.Core.Configuration.IStorageOptions>();
                var searchOptions = NoteNest.Core.Configuration.SearchConfigurationOptions.FromStoragePath(storageOptions.MetadataPath);
                searchOptions.ValidateConfiguration();
                return searchOptions;
            });
            
            // =============================================================================
            // SECTION 3: DATABASE FOUNDATION
            // =============================================================================
            
            // Memory caching for performance
            services.AddMemoryCache();
            
            // Core database services
            services.AddSingleton<ITreeDatabaseInitializer>(provider => 
                new TreeDatabaseInitializer(treeConnectionString, provider.GetRequiredService<IAppLogger>()));
            
            services.AddSingleton<ITreeDatabaseRepository>(provider => 
                new TreeDatabaseRepository(treeConnectionString, provider.GetRequiredService<IAppLogger>(), notesRootPath));
            
            services.AddSingleton<ITreePopulationService, TreePopulationService>();
            
            // Database repositories
            services.AddScoped<NoteNest.Application.Common.Interfaces.ICategoryRepository>(provider => 
                new NoteNest.Infrastructure.Database.Services.CategoryTreeDatabaseService(
                    provider.GetRequiredService<ITreeDatabaseRepository>(),
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<IMemoryCache>(),
                    notesRootPath));
            
            services.AddScoped<NoteNest.Application.Common.Interfaces.INoteRepository>(provider => 
                new NoteNest.Infrastructure.Database.Services.NoteTreeDatabaseService(
                    provider.GetRequiredService<ITreeDatabaseRepository>(),
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<IMemoryCache>()));
            
            // File system synchronization
            services.AddSingleton<IDatabaseFileWatcherService>(provider =>
                new DatabaseFileWatcherService(
                    provider.GetRequiredService<ITreeDatabaseRepository>(),
                    provider.GetRequiredService<IMemoryCache>(),
                    provider.GetRequiredService<IAppLogger>(),
                    notesRootPath));
            
            // Auto-start services  
            services.AddHostedService<TreeNodeInitializationService>();
            
            // Register the existing DatabaseFileWatcherService singleton as a hosted service
            services.AddSingleton<IHostedService>(provider => 
                (DatabaseFileWatcherService)provider.GetRequiredService<IDatabaseFileWatcherService>());
            
            // =============================================================================
            // SECTION 4: CORE SERVICES (No UI Dependencies)
            // =============================================================================
            
            // Content Cache
            services.AddSingleton<ContentCache>();
            
            // Note Service - Core note management
            services.AddSingleton<NoteService>(provider =>
            {
                return new NoteService(
                    provider.GetRequiredService<IFileSystemProvider>(),
                    provider.GetRequiredService<ConfigurationService>(),
                    provider.GetRequiredService<IAppLogger>(),
                    null, // IEventBus is optional, Core and Application EventBus are incompatible
                    provider.GetService<NoteNest.Core.Services.Safety.SafeFileService>(),
                    provider.GetService<NoteNest.Core.Services.Notes.INoteStorageService>(),
                    provider.GetService<IUserNotificationService>(),
                    provider.GetRequiredService<NoteNest.Core.Services.NoteMetadataManager>()
                );
            });
            
            // Note Metadata Manager
            services.AddSingleton<NoteNest.Core.Services.NoteMetadataManager>(provider =>
            {
                return new NoteNest.Core.Services.NoteMetadataManager(
                    provider.GetRequiredService<IFileSystemProvider>(),
                    provider.GetRequiredService<IAppLogger>());
            });
            
            // Service Error Handler
            services.AddSingleton<IServiceErrorHandler, NoteNest.Core.Services.Implementation.ServiceErrorHandler>();
            
            // Additional Core Services
            services.AddSingleton<NoteNest.Core.Services.Safety.SafeFileService>();
            services.AddSingleton<NoteNest.Core.Services.Notes.INoteStorageService>(provider =>
            {
                return new NoteNest.Core.Services.Notes.NoteStorageService(
                    provider.GetRequiredService<IFileSystemProvider>(),
                    provider.GetService<NoteNest.Core.Services.Safety.SafeFileService>(),
                    provider.GetService<IAppLogger>()
                );
            });
            
            // =============================================================================
            // SECTION 5: SAVE MANAGEMENT SERVICES (RTF Integration)
            // =============================================================================
            
            // RTF Save Manager - Your specific implementation
            services.AddSingleton<ISaveManager>(provider =>
            {
                var dataPath = notesRootPath;
                var statusNotifier = new NoteNest.Core.Services.BasicStatusNotifier(provider.GetRequiredService<IAppLogger>());
                return new NoteNest.Core.Services.RTFIntegratedSaveEngine(dataPath, statusNotifier);
            });
            
            // =============================================================================
            // SECTION 6: NOTE OPERATIONS & WORKSPACE SERVICES
            // =============================================================================
            
            // Note Operations Service
            services.AddSingleton<INoteOperationsService>(provider =>
            {
                return new NoteNest.Core.Services.Implementation.NoteOperationsService(
                    provider.GetRequiredService<NoteService>(),
                    provider.GetRequiredService<IServiceErrorHandler>(),
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<IFileSystemProvider>(),
                    provider.GetRequiredService<ConfigurationService>(),
                    provider.GetRequiredService<ContentCache>(),
                    provider.GetRequiredService<ISaveManager>()
                );
            });
            
            // Tab Factory
            services.AddSingleton<ITabFactory>(provider =>
            {
                return new NoteNest.UI.Services.UITabFactory(provider.GetRequiredService<ISaveManager>());
            });
            
            // Workspace Service - CRITICAL for SearchViewModel
            services.AddSingleton<IWorkspaceService>(provider =>
            {
                return new NoteNest.Core.Services.Implementation.WorkspaceService(
                    provider.GetRequiredService<ContentCache>(),
                    provider.GetRequiredService<NoteService>(),
                    provider.GetRequiredService<IServiceErrorHandler>(),
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<INoteOperationsService>(),
                    provider.GetRequiredService<ISaveManager>(),
                    provider.GetRequiredService<ITabFactory>()
                );
            });
            
            // =============================================================================
            // SECTION 7: FTS5 SEARCH SERVICES (Explicit Registration)
            // =============================================================================
            
            // FTS5 Repository
            services.AddSingleton<IFts5Repository>(provider =>
            {
                return new Fts5Repository(provider.GetService<IAppLogger>());
            });

            // Search Result Mapper
            services.AddSingleton<ISearchResultMapper, SearchResultMapper>();

            // Search Index Manager
            services.AddSingleton<ISearchIndexManager>(provider =>
            {
                return new Fts5IndexManager(
                    provider.GetRequiredService<IFts5Repository>(),
                    provider.GetRequiredService<ISearchResultMapper>(),
                    provider.GetRequiredService<NoteNest.Core.Configuration.IStorageOptions>(),
                    provider.GetService<IAppLogger>()
                );
            });

            // FTS5 Search Service (implements ISearchService)
            services.AddSingleton<NoteNest.UI.Interfaces.ISearchService>(provider =>
            {
                return new FTS5SearchService(
                    provider.GetRequiredService<IFts5Repository>(),
                    provider.GetRequiredService<ISearchResultMapper>(),
                    provider.GetRequiredService<ISearchIndexManager>(),
                    provider.GetRequiredService<NoteNest.Core.Configuration.ISearchOptions>(),
                    provider.GetRequiredService<NoteNest.Core.Configuration.IStorageOptions>(),
                    provider.GetService<IAppLogger>()
                );
            });
            
            // =============================================================================
            // SECTION 8: VIEW MODELS (Explicit Factory Registration)
            // =============================================================================
            
            // SearchViewModel - EXPLICIT REGISTRATION
            services.AddTransient<SearchViewModel>(provider =>
            {
                return new SearchViewModel(
                    provider.GetRequiredService<NoteNest.UI.Interfaces.ISearchService>(),
                    provider.GetRequiredService<IWorkspaceService>(),
                    provider.GetRequiredService<NoteService>(),
                    provider.GetRequiredService<IAppLogger>()
                );
            });
            
            // Category Tree ViewModel
            services.AddTransient<CategoryTreeViewModel>();
            
            // Note Operations ViewModel
            services.AddTransient<NoteOperationsViewModel>();
            
            // Category Operations ViewModel
            services.AddTransient<CategoryOperationsViewModel>();
            
            // Modern Workspace ViewModel
            services.AddTransient<ModernWorkspaceViewModel>();
            
            // Main Shell ViewModel - All dependencies now available
            services.AddTransient<MainShellViewModel>();
            
            // =============================================================================
            // SECTION 9: UI SERVICES
            // =============================================================================
            
            // Dialog Service
            services.AddScoped<IDialogService, DialogService>();
            
            // User Notification Service (if exists)
            services.AddSingleton<IUserNotificationService>(provider =>
            {
                var logger = provider.GetService<IAppLogger>();
                return new UserNotificationService(System.Windows.Application.Current?.MainWindow, logger);
            });
            
            // =============================================================================
            // SECTION 10: CLEAN ARCHITECTURE (CQRS/MediatR)
            // =============================================================================
            
            // MediatR for CQRS
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(typeof(CreateNoteCommand).Assembly);
            });
            
            // Pipeline behaviors
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            
            // FluentValidation
            services.AddValidatorsFromAssembly(typeof(CreateNoteCommand).Assembly);
            
            return services;
        }
        
        /// <summary>
        /// LEGACY ARCHITECTURE - File system based (PRESERVED FOR ROLLBACK)
        /// </summary>
        private static IServiceCollection ConfigureLegacyArchitecture(IServiceCollection services, IConfiguration configuration)
        {
            // =============================================================================
            // SECTION 1: CORE INFRASTRUCTURE & LOGGING
            // =============================================================================
            
            services.AddSingleton<IAppLogger, ConsoleAppLogger>();
            services.AddSingleton<IFileSystemProvider, NoteNest.Core.Services.DefaultFileSystemProvider>();
            services.AddSingleton<NoteNest.Application.Common.Interfaces.IEventBus, InMemoryEventBus>();
            services.AddScoped<IFileService, FileService>();
            services.AddSingleton<ConfigurationService>();
            services.AddSingleton(configuration);
            
            // =============================================================================
            // SECTION 2: LEGACY REPOSITORIES (File System Based)
            // =============================================================================
            
            services.AddScoped<INoteRepository>(provider => 
                new NoteNest.Infrastructure.Persistence.Repositories.FileSystemNoteRepository(
                    provider.GetRequiredService<IFileSystemProvider>(),
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<IConfiguration>()));
            services.AddScoped<ICategoryRepository>(provider => 
                new FileSystemCategoryRepository(
                    provider.GetRequiredService<IAppLogger>(),
                    provider.GetRequiredService<IConfiguration>()));
            
            // =============================================================================
            // SECTION 3: RTF SAVE SYSTEM
            // =============================================================================
            
            var notesRootPath = configuration.GetValue<string>("NotesPath") 
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoteNest");
            
            services.AddSingleton<ISaveManager>(provider =>
            {
                var dataPath = notesRootPath;
                var statusNotifier = new NoteNest.Core.Services.BasicStatusNotifier(provider.GetRequiredService<IAppLogger>());
                return new NoteNest.Core.Services.RTFIntegratedSaveEngine(dataPath, statusNotifier);
            });
            
            // NoteService registration removed - using explicit factory registration from line 188
            services.AddScoped<NoteNest.Core.Services.NoteMetadataManager>();
            
            // =============================================================================
            // SECTION 4: UI SERVICES & VIEW MODELS
            // =============================================================================
            
            services.AddScoped<IDialogService, DialogService>();
            
            services.AddTransient<MainShellViewModel>();
            services.AddTransient<CategoryTreeViewModel>();
            services.AddTransient<NoteOperationsViewModel>();
            services.AddTransient<CategoryOperationsViewModel>();
            services.AddTransient<ModernWorkspaceViewModel>();
            
            // =============================================================================
            // SECTION 5: CQRS/MediatR
            // =============================================================================
            
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(typeof(CreateNoteCommand).Assembly);
            });
            
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            
            services.AddValidatorsFromAssembly(typeof(CreateNoteCommand).Assembly);
            
            return services;
        }
    }
    
    // Enhanced FileSystemCategoryRepository (preserved from original)
    public class FileSystemCategoryRepository : ICategoryRepository
    {
        private readonly IAppLogger _logger;
        private readonly IConfiguration _configuration;
        private readonly string _rootPath;
        
        public FileSystemCategoryRepository(IAppLogger logger, IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            
            _rootPath = configuration.GetValue<string>("NotesPath");
            if (string.IsNullOrWhiteSpace(_rootPath))
            {
                _rootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MyNotes");
                _logger.Warning($"NotesPath not configured, using default: {_rootPath}");
            }
            
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
                _rootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoteNest");
                Directory.CreateDirectory(_rootPath);
            }
                
            _logger.Info($"FileSystemCategoryRepository initialized with root path: {_rootPath}");
        }

        public async Task<Category> GetByIdAsync(CategoryId id)
        {
            await Task.CompletedTask;
            
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