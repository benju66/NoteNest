using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Domain.Categories;
using NoteNest.UI.ViewModels.Common;

namespace NoteNest.UI.ViewModels.Categories
{
    public class CategoryTreeViewModel : ViewModelBase
    {
        private readonly ICategoryRepository _categoryRepository;
        private CategoryViewModel _selectedCategory;

        public CategoryTreeViewModel(ICategoryRepository categoryRepository)
        {
            _categoryRepository = categoryRepository;
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

        public event Action<CategoryViewModel> CategorySelected;

        public async Task RefreshAsync()
        {
            await LoadCategoriesAsync();
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                var allCategories = await _categoryRepository.GetAllAsync();
                var rootCategories = await _categoryRepository.GetRootCategoriesAsync();
                
                Categories.Clear();
                
                // Create CategoryViewModels for root categories
                foreach (var category in rootCategories)
                {
                    var categoryViewModel = new CategoryViewModel(category);
                    await LoadChildrenAsync(categoryViewModel, allCategories);
                    Categories.Add(categoryViewModel);
                }
            }
            catch (Exception ex)
            {
                // TODO: Better error handling
                System.Diagnostics.Debug.WriteLine($"Failed to load categories: {ex.Message}");
            }
        }
        
        private async Task LoadChildrenAsync(CategoryViewModel parentViewModel, IReadOnlyList<Category> allCategories)
        {
            var children = allCategories.Where(c => c.ParentId?.Value == parentViewModel.Id).ToList();
            
            foreach (var child in children)
            {
                var childViewModel = new CategoryViewModel(child);
                await LoadChildrenAsync(childViewModel, allCategories);
                parentViewModel.Children.Add(childViewModel);
            }
        }
    }
}
