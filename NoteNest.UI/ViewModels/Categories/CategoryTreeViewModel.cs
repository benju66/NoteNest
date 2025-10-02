using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Categories;
using NoteNest.UI.ViewModels.Common;
using NoteNest.Core.Services.Logging;

namespace NoteNest.UI.ViewModels.Categories
{
    public class CategoryTreeViewModel : ViewModelBase
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly INoteRepository _noteRepository;
        private readonly IAppLogger _logger;
        private CategoryViewModel _selectedCategory;
        private bool _isLoading;

        public CategoryTreeViewModel(
            ICategoryRepository categoryRepository, 
            INoteRepository noteRepository,
            IAppLogger logger)
        {
            _categoryRepository = categoryRepository;
            _noteRepository = noteRepository;
            _logger = logger;
            
            Categories = new ObservableCollection<CategoryViewModel>();
            
            _ = LoadCategoriesAsync();
        }

        public ObservableCollection<CategoryViewModel> Categories { get; }
        
        // Alias for XAML binding compatibility
        public ObservableCollection<CategoryViewModel> RootCategories => Categories;

        public CategoryViewModel SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    CategorySelected?.Invoke(value);
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public event Action<CategoryViewModel> CategorySelected;
        public event Action<NoteItemViewModel> NoteSelected;
        public event Action<NoteItemViewModel> NoteOpenRequested;

        public async Task RefreshAsync()
        {
            // Save expanded state and selection before refresh
            var expandedIds = new HashSet<string>();
            SaveExpandedState(Categories, expandedIds);
            var selectedId = SelectedCategory?.Id;
            
            await LoadCategoriesAsync();
            
            // Restore expanded state after refresh
            RestoreExpandedState(Categories, expandedIds);
            
            // Restore selection
            if (!string.IsNullOrEmpty(selectedId))
            {
                var categoryToSelect = FindCategoryById(Categories, selectedId);
                if (categoryToSelect != null)
                {
                    SelectedCategory = categoryToSelect;
                }
            }
        }
        
        private void SaveExpandedState(ObservableCollection<CategoryViewModel> categories, HashSet<string> expandedIds)
        {
            foreach (var category in categories)
            {
                if (category.IsExpanded)
                {
                    expandedIds.Add(category.Id);
                }
                
                if (category.Children.Any())
                {
                    SaveExpandedState(category.Children, expandedIds);
                }
            }
        }
        
        private void RestoreExpandedState(ObservableCollection<CategoryViewModel> categories, HashSet<string> expandedIds)
        {
            foreach (var category in categories)
            {
                if (expandedIds.Contains(category.Id))
                {
                    category.IsExpanded = true;
                }
                
                if (category.Children.Any())
                {
                    RestoreExpandedState(category.Children, expandedIds);
                }
            }
        }
        
        private CategoryViewModel FindCategoryById(ObservableCollection<CategoryViewModel> categories, string id)
        {
            foreach (var category in categories)
            {
                if (category.Id == id)
                    return category;
                
                var found = FindCategoryById(category.Children, id);
                if (found != null)
                    return found;
            }
            return null;
        }

        public void SelectNote(NoteItemViewModel note)
        {
            if (note != null)
            {
                NoteSelected?.Invoke(note);
                _logger.Debug($"Note selected: {note.Title}");
            }
        }

        public void OpenNote(NoteItemViewModel note)
        {
            if (note != null)
            {
                NoteOpenRequested?.Invoke(note);
                _logger.Info($"Note open requested: {note.Title}");
            }
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                IsLoading = true;
                _logger.Info("Loading categories from repository...");
                
                // Retry mechanism for database initialization timing issues
                var maxRetries = 3;
                var retryDelay = TimeSpan.FromSeconds(2);
                Exception lastException = null;
                
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        var allCategories = await _categoryRepository.GetAllAsync();
                        var rootCategories = await _categoryRepository.GetRootCategoriesAsync();
                        
                        // If we get here successfully, continue with normal processing
                        await ProcessLoadedCategories(allCategories, rootCategories);
                        return; // Success - exit retry loop
                    }
                    catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("no such table"))
                    {
                        lastException = ex;
                        _logger.Warning($"Database not ready on attempt {attempt}/{maxRetries}, retrying in {retryDelay.TotalSeconds}s...");
                        
                        if (attempt < maxRetries)
                        {
                            await Task.Delay(retryDelay);
                        }
                    }
                }
                
                // If all retries failed, throw the last exception
                throw lastException;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load categories");
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        private async Task ProcessLoadedCategories(IReadOnlyList<Domain.Categories.Category> allCategories, IReadOnlyList<Domain.Categories.Category> rootCategories)
        {
            Categories.Clear();
            
            // Create CategoryViewModels for root categories with dependency injection
            foreach (var category in rootCategories)
            {
                var categoryViewModel = new CategoryViewModel(category, _noteRepository, _logger);
                
                // Wire up note events to bubble up
                categoryViewModel.NoteOpenRequested += OnNoteOpenRequested;
                categoryViewModel.NoteSelectionRequested += OnNoteSelectionRequested;
                
                await LoadChildrenAsync(categoryViewModel, allCategories);
                Categories.Add(categoryViewModel);
            }
            
            _logger.Info($"Loaded {Categories.Count} root categories");
        }
        
        private async Task LoadChildrenAsync(CategoryViewModel parentViewModel, IReadOnlyList<Category> allCategories)
        {
            var children = allCategories.Where(c => c.ParentId?.Value == parentViewModel.Id).ToList();
            
            foreach (var child in children)
            {
                var childViewModel = new CategoryViewModel(child, _noteRepository, _logger);
                
                // Wire up note events for child categories too
                childViewModel.NoteOpenRequested += OnNoteOpenRequested;
                childViewModel.NoteSelectionRequested += OnNoteSelectionRequested;
                
                await LoadChildrenAsync(childViewModel, allCategories);
                parentViewModel.Children.Add(childViewModel);
            }
        }

        // =============================================================================
        // NOTE EVENT HANDLERS - Forward to MainShellViewModel
        // =============================================================================

        private void OnNoteOpenRequested(NoteItemViewModel note)
        {
            NoteOpenRequested?.Invoke(note);
        }

        private void OnNoteSelectionRequested(NoteItemViewModel note)
        {
            NoteSelected?.Invoke(note);
        }
    }
}
