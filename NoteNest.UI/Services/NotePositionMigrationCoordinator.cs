using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Coordinates note position migration for existing notes during application startup.
    /// Extracted from MainViewModel to separate data migration concerns.
    /// </summary>
    public class NotePositionMigrationCoordinator : IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IAppLogger _logger;
        private readonly Func<ObservableCollection<CategoryTreeItem>> _getCategories;
        private readonly Action<string> _setStatusMessage;
        private bool _disposed;

        public NotePositionMigrationCoordinator(
            IServiceProvider serviceProvider,
            IAppLogger logger,
            Func<ObservableCollection<CategoryTreeItem>> getCategories,
            Action<string> setStatusMessage)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _getCategories = getCategories ?? throw new ArgumentNullException(nameof(getCategories));
            _setStatusMessage = setStatusMessage ?? throw new ArgumentNullException(nameof(setStatusMessage));
        }

        /// <summary>
        /// Migrates note positions for existing notes that don't have positions assigned
        /// Safe to call multiple times - only assigns positions to notes without them
        /// </summary>
        public async Task MigrateNotePositionsIfNeededAsync()
        {
            try
            {
                _setStatusMessage("Checking note positions...");
                
                var positionService = _serviceProvider.GetService<NoteNest.Core.Utils.NotePositionService>();
                if (positionService == null)
                {
                    _logger.Warning("NotePositionService not available, skipping position migration");
                    return;
                }
                
                int totalAssigned = 0;
                var categories = _getCategories();
                
                // Process each category to assign positions to notes without them
                foreach (var categoryItem in categories)
                {
                    var assignedInCategory = await ProcessCategoryForPositionMigrationAsync(categoryItem, positionService);
                    totalAssigned += assignedInCategory;
                }
                
                if (totalAssigned > 0)
                {
                    _logger.Info($"Position migration completed: assigned positions to {totalAssigned} notes");
                    _setStatusMessage($"Assigned positions to {totalAssigned} notes");
                    await Task.Delay(1500); // Show the message briefly
                }
                else
                {
                    _logger.Debug("Position migration completed: no notes needed position assignment");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to migrate note positions");
                // Don't throw - this is not critical for app functionality
            }
        }
        
        /// <summary>
        /// Recursively processes a category and its subcategories for position migration
        /// </summary>
        private async Task<int> ProcessCategoryForPositionMigrationAsync(
            CategoryTreeItem categoryItem, 
            NoteNest.Core.Utils.NotePositionService positionService)
        {
            int totalAssigned = 0;
            
            try
            {
                // Ensure the category's notes are loaded
                if (!categoryItem.IsLoaded)
                {
                    await categoryItem.LoadChildrenAsync();
                }
                
                // Process notes in this category
                if (categoryItem.Notes?.Count > 0)
                {
                    var noteModels = categoryItem.Notes.Select(ni => ni.Model).ToList();
                    var assignedCount = await positionService.AssignInitialPositionsAsync(noteModels);
                    totalAssigned += assignedCount;
                    
                    if (assignedCount > 0)
                    {
                        _logger.Debug($"Assigned {assignedCount} positions in category '{categoryItem.Name}'");
                    }
                }
                
                // Process subcategories recursively
                if (categoryItem.SubCategories?.Count > 0)
                {
                    foreach (var subCategory in categoryItem.SubCategories)
                    {
                        var subCategoryCount = await ProcessCategoryForPositionMigrationAsync(subCategory, positionService);
                        totalAssigned += subCategoryCount;
                    }
                }
                
                return totalAssigned;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to process category '{categoryItem.Name}' for position migration: {ex.Message}");
                // Return whatever we managed to assign before the error
                return totalAssigned;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // No specific resources to dispose currently
                _disposed = true;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Error disposing NotePositionMigrationCoordinator: {ex.Message}");
            }
        }
    }
}
