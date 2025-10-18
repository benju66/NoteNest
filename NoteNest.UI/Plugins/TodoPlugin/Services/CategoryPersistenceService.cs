using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using NoteNest.Core.Services.Logging;
using NoteNest.UI.Plugins.TodoPlugin.Models;

namespace NoteNest.UI.Plugins.TodoPlugin.Services
{
    /// <summary>
    /// Persists user-selected categories to todos.db user_preferences table.
    /// Categories are stored as JSON for flexibility and easy querying.
    /// </summary>
    public interface ICategoryPersistenceService
    {
        Task SaveCategoriesAsync(IEnumerable<Category> categories);
        Task<List<Category>> LoadCategoriesAsync();
    }
    
    public class CategoryPersistenceService : ICategoryPersistenceService
    {
        private readonly string _connectionString;
        private readonly IAppLogger _logger;
        private const string PREFERENCES_KEY = "selected_categories";
        
        public CategoryPersistenceService(string connectionString, IAppLogger logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task SaveCategoriesAsync(IEnumerable<Category> categories)
        {
            try
            {
                _logger.Info("[CategoryPersistence] ========== SAVING TO user_preferences ==========");
                
                var categoryList = categories.Select(c => new
                {
                    Id = c.Id.ToString(),
                    ParentId = c.ParentId?.ToString(),
                    OriginalParentId = c.OriginalParentId?.ToString(),
                    Name = c.Name,
                    DisplayPath = c.DisplayPath,
                    Order = c.Order
                }).ToList();
                
                _logger.Info($"[CategoryPersistence] Serializing {categoryList.Count} categories to JSON...");
                
                foreach (var cat in categoryList)
                {
                    _logger.Info($"[CategoryPersistence]   - {cat.Name} (ID: {cat.Id})");
                }
                
                var json = JsonSerializer.Serialize(categoryList);
                _logger.Info($"[CategoryPersistence] JSON length: {json.Length} characters");
                _logger.Debug($"[CategoryPersistence] JSON content: {json}");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = @"
                    INSERT OR REPLACE INTO user_preferences (key, value, updated_at)
                    VALUES (@Key, @Value, @UpdatedAt)";
                
                await connection.ExecuteAsync(sql, new
                {
                    Key = PREFERENCES_KEY,
                    Value = json,
                    UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
                
                _logger.Info($"[CategoryPersistence] ✅ Successfully saved {categoryList.Count} categories to database");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryPersistence] Failed to save categories");
                // Don't throw - persistence failure shouldn't crash app
            }
        }
        
        public async Task<List<Category>> LoadCategoriesAsync()
        {
            try
            {
                _logger.Info("[CategoryPersistence] ========== LOADING FROM user_preferences ==========");
                
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                var sql = "SELECT value FROM user_preferences WHERE key = @Key";
                var json = await connection.ExecuteScalarAsync<string>(sql, new { Key = PREFERENCES_KEY });
                
                if (string.IsNullOrEmpty(json))
                {
                    _logger.Info("[CategoryPersistence] ❌ No saved categories found in database (NULL or empty)");
                    return new List<Category>();
                }
                
                _logger.Info($"[CategoryPersistence] ✅ Found JSON in database (length: {json.Length} characters)");
                _logger.Debug($"[CategoryPersistence] JSON content: {json}");
                
                var dtos = JsonSerializer.Deserialize<List<CategoryDto>>(json);
                if (dtos == null || dtos.Count == 0)
                {
                    _logger.Warning("[CategoryPersistence] ❌ Deserialization returned null or empty list");
                    return new List<Category>();
                }
                
                _logger.Info($"[CategoryPersistence] ✅ Deserialized {dtos.Count} category DTOs");
                
                var categories = dtos.Select(dto => new Category
                {
                    Id = Guid.Parse(dto.Id),
                    ParentId = string.IsNullOrEmpty(dto.ParentId) ? null : Guid.Parse(dto.ParentId),
                    OriginalParentId = string.IsNullOrEmpty(dto.OriginalParentId) ? null : Guid.Parse(dto.OriginalParentId),
                    Name = dto.Name,
                    DisplayPath = dto.DisplayPath,
                    Order = dto.Order
                }).ToList();
                
                _logger.Info($"[CategoryPersistence] ✅ Loaded {categories.Count} categories from database");
                
                foreach (var cat in categories)
                {
                    _logger.Info($"[CategoryPersistence]   - {cat.Name} (ID: {cat.Id})");
                }
                
                return categories;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[CategoryPersistence] Failed to load categories");
                return new List<Category>(); // Graceful degradation
            }
        }
        
        private class CategoryDto
        {
            public string Id { get; set; }
            public string ParentId { get; set; }
            public string OriginalParentId { get; set; }
            public string Name { get; set; }
            public string DisplayPath { get; set; }
            public int Order { get; set; }
        }
    }
}

