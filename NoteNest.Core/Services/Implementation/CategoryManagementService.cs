using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NoteNest.Core.Interfaces.Services;
using NoteNest.Core.Models;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Core.Services.Implementation
{
    public class CategoryManagementService : ICategoryManagementService
    {
        private readonly NoteService _noteService;
        private readonly ConfigurationService _configService;
        private readonly IServiceErrorHandler _errorHandler;
        private readonly IAppLogger _logger;
        
        public CategoryManagementService(
            NoteService noteService,
            ConfigurationService configService,
            IServiceErrorHandler errorHandler,
            IAppLogger logger)
        {
            _noteService = noteService ?? throw new ArgumentNullException(nameof(noteService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _logger.Debug("CategoryManagementService initialized");
        }
        
        public async Task<CategoryModel> CreateCategoryAsync(string name, string? parentId = null)
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
        
        public async Task<CategoryModel> CreateSubCategoryAsync(CategoryModel parent, string name)
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
        
        public async Task<bool> DeleteCategoryAsync(CategoryModel category)
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
        
        public async Task<bool> RenameCategoryAsync(CategoryModel category, string newName)
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
        
        public async Task<bool> ToggleCategoryPinAsync(CategoryModel category)
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
        
        public async Task<List<CategoryModel>> LoadCategoriesAsync()
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
        
        public async Task SaveCategoriesAsync(List<CategoryModel> categories)
        {
            // TODO: Implement in Phase 2
            throw new NotImplementedException("Will be implemented in Phase 2");
        }
    }
}