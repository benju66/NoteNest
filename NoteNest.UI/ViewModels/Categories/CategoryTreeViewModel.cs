using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using MediatR;
using NoteNest.Application.Common.Interfaces;
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
                var rootCategories = await _categoryRepository.GetRootCategoriesAsync();
                
                Categories.Clear();
                foreach (var category in rootCategories)
                {
                    Categories.Add(new CategoryViewModel(category));
                }
            }
            catch (Exception ex)
            {
                // TODO: Better error handling
                System.Diagnostics.Debug.WriteLine($"Failed to load categories: {ex.Message}");
            }
        }
    }
}
