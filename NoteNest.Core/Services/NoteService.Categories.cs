using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NoteNest.Core.Models;

namespace NoteNest.Core.Services
{
	public partial class NoteService
	{
		public async Task<List<CategoryModel>> LoadCategoriesAsync(string metadataPath)
		{
			try
			{
				var categoriesFile = PathService.CategoriesPath;
				
				if (!await _fileSystem.ExistsAsync(categoriesFile))
				{
					_logger.Info("Categories file not found, returning empty list");
					return new List<CategoryModel>();
				}

				var json = await ReadFileTextAsync(categoriesFile);
				var wrapper = JsonSerializer.Deserialize<CategoryWrapper>(json, _jsonOptions);
				
				if (wrapper?.Categories != null)
				{
					// Convert stored relative paths to absolute paths
					foreach (var category in wrapper.Categories)
					{
						var originalPath = category.Path;
						category.Path = PathService.ToAbsolutePath(category.Path);
						_logger.Debug($"Loaded category '{category.Name}': {originalPath} -> {category.Path}");
					}
					
					_logger.Info($"Loaded {wrapper.Categories.Count} categories from disk");
					return wrapper.Categories;
				}
				
				_logger.Warning("Categories file exists but contains no categories");
				return new List<CategoryModel>();
			}
			catch (JsonException jex)
			{
				_logger.Error(jex, "Failed to parse categories JSON file");
				throw new InvalidOperationException("Categories file is corrupted or invalid format", jex);
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to load categories from disk");
				throw new InvalidOperationException("Failed to load categories. Check log for details.", ex);
			}
		}

		public async Task SaveCategoriesAsync(string metadataPath, List<CategoryModel> categories)
		{
			try
			{
				var categoriesFile = PathService.CategoriesPath;
				
				// Ensure directory exists
				var dir = Path.GetDirectoryName(categoriesFile) ?? string.Empty;
				if (!await _fileSystem.ExistsAsync(dir))
				{
					await _fileSystem.CreateDirectoryAsync(dir);
					_logger.Debug($"Created metadata directory: {dir}");
				}

				// Convert absolute paths to relative paths for storage
				var categoriesForStorage = categories.Select(c => new CategoryModel
				{
					Id = c.Id,
					ParentId = c.ParentId,
					Name = c.Name,
					Path = PathService.ToRelativePath(c.Path), // Store as relative
					Pinned = c.Pinned,
					Tags = c.Tags ?? new List<string>(),
					Level = c.Level
				}).ToList();

				var wrapper = new CategoryWrapper 
				{ 
					Categories = categoriesForStorage, 
					Version = "2.0" 
				};
				
				var json = JsonSerializer.Serialize(wrapper, _jsonOptions);
				await WriteFileTextAsync(categoriesFile, json);
				
				_logger.Info($"Saved {categories.Count} categories to disk");
				
				// Log each category for debugging
				foreach (var cat in categoriesForStorage)
				{
					_logger.Debug($"Saved category '{cat.Name}' with path: {cat.Path}");
				}
			}
			catch (Exception ex)
			{
				_logger.Error(ex, "Failed to save categories to disk");
				throw new InvalidOperationException("Failed to save categories. Check log for details.", ex);
			}
		}

		private class CategoryWrapper
		{
			public List<CategoryModel> Categories { get; set; } = new();
			public string Version { get; set; } = "2.0";
			public AppSettings? Settings { get; set; }
		}
	}
}


