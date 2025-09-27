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

namespace NoteNest.UI.Composition
{
    public static class ServiceConfiguration
    {
        public static IServiceCollection ConfigureServices(this IServiceCollection services, IConfiguration configuration)
        {
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
            
            // Repositories
            services.AddScoped<INoteRepository, NoteNest.Infrastructure.Persistence.Repositories.FileSystemNoteRepository>();
            services.AddScoped<ICategoryRepository, NoteNest.Infrastructure.Repositories.FileSystemCategoryRepository>();
            
            // Infrastructure services
            services.AddSingleton<NoteNest.Application.Common.Interfaces.IEventBus, InMemoryEventBus>();
            services.AddScoped<IFileService, FileService>();
            
            // Core services - use our simple console logger for Clean Architecture testing  
            services.AddSingleton<IAppLogger, ConsoleAppLogger>();
            services.AddSingleton<IFileSystemProvider, NoteNest.Infrastructure.Services.DefaultFileSystemProvider>();
            
            // UI services (reuse existing ones)
            services.AddScoped<IDialogService, DialogService>();
            
            // ViewModels - Focused and Single Responsibility
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
