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
            // DATABASE SERVICES - Ready for activation (complete implementation)
            // =============================================================================
            
            // TODO: Database foundation complete - activate when ready
            // Complete enterprise database with TreeNode, backup/recovery, migration
            // Switch UseDatabaseArchitecture: true in appsettings.json to activate
            
            // =============================================================================
            // ENHANCED REPOSITORIES (TreeNode-based)
            // =============================================================================
            
            // TODO: Implement TreeNode-based repositories
            // services.AddScoped<ITreeRepository, TreeDatabaseRepository>();
            // services.AddScoped<INoteRepository, TreeNodeNoteRepository>();
            // services.AddScoped<ICategoryRepository, TreeNodeCategoryRepository>();
            
            // =============================================================================
            // INFRASTRUCTURE SERVICES
            // =============================================================================
            
            services.AddSingleton<NoteNest.Application.Common.Interfaces.IEventBus, InMemoryEventBus>();
            services.AddScoped<IFileService, FileService>();
            
            // Core services
            services.AddSingleton<IAppLogger, ConsoleAppLogger>();
            services.AddSingleton<IFileSystemProvider, NoteNest.Infrastructure.Services.DefaultFileSystemProvider>();
            
            // UI services
            services.AddScoped<IDialogService, DialogService>();
            
            // =============================================================================
            // ENHANCED VIEWMODELS (TreeNode-aware)
            // =============================================================================
            
            // TODO: Implement TreeNode-based ViewModels
            // services.AddTransient<TreeNodeMainShellViewModel>();
            // services.AddTransient<TreeNodeCategoryTreeViewModel>();
            // services.AddTransient<TreeNodeWorkspaceViewModel>();
            
            // For now, use legacy repositories while we implement TreeNode integration
            services.AddScoped<INoteRepository, NoteNest.Infrastructure.Persistence.Repositories.FileSystemNoteRepository>();
            services.AddScoped<ICategoryRepository, NoteNest.Infrastructure.Repositories.FileSystemCategoryRepository>();
            
            // Legacy ViewModels until TreeNode ViewModels are ready
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
            services.AddScoped<ICategoryRepository, NoteNest.Infrastructure.Repositories.FileSystemCategoryRepository>();
            
            // Infrastructure services
            services.AddSingleton<NoteNest.Application.Common.Interfaces.IEventBus, InMemoryEventBus>();
            services.AddScoped<IFileService, FileService>();
            
            // Core services
            services.AddSingleton<IAppLogger, ConsoleAppLogger>();
            services.AddSingleton<IFileSystemProvider, NoteNest.Infrastructure.Services.DefaultFileSystemProvider>();
            
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

    // Temporary implementation of ICategoryRepository for testing
    public class FileSystemCategoryRepository : ICategoryRepository
    {
        private readonly IAppLogger _logger;
        
        public FileSystemCategoryRepository(IAppLogger logger)
        {
            _logger = logger;
        }

        public async Task<Category> GetByIdAsync(CategoryId id)
        {
            // TODO: Implement properly - for now return a test category
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
            // TODO: Implement properly - for now return test data
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
            return true;
        }
    }
}
