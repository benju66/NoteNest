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

        public async Task RefreshAsync()
        {
            await LoadCategoriesAsync();
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                IsLoading = true;
                _logger.Info("Loading categories from repository...");
                
                var allCategories = await _categoryRepository.GetAllAsync();
                var rootCategories = await _categoryRepository.GetRootCategoriesAsync();
                
                Categories.Clear();
                
                // Create CategoryViewModels for root categories with dependency injection
                foreach (var category in rootCategories)
                {
                    var categoryViewModel = new CategoryViewModel(category, _noteRepository, _logger);
                    await LoadChildrenAsync(categoryViewModel, allCategories);
                    Categories.Add(categoryViewModel);
                }
                
                _logger.Info($"Loaded {Categories.Count} root categories");
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
        
        private async Task LoadChildrenAsync(CategoryViewModel parentViewModel, IReadOnlyList<Category> allCategories)
        {
            var children = allCategories.Where(c => c.ParentId?.Value == parentViewModel.Id).ToList();
            
            foreach (var child in children)
            {
                var childViewModel = new CategoryViewModel(child, _noteRepository, _logger);
                await LoadChildrenAsync(childViewModel, allCategories);
                parentViewModel.Children.Add(childViewModel);
            }
        }
    }
}
