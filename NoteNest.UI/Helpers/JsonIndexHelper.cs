using System;
using System.Collections.Generic; // ✅ Needed for List<T>
using System.IO;
using System.Text.Json;
using NoteNest.Core.Models;

namespace NoteNest.UI.Helpers
{
    public static class JsonIndexHelper
    {
        public static List<CategoryModel> LoadCategories(string filePath)
        {
            if (!File.Exists(filePath))
                return new List<CategoryModel>();

            var json = File.ReadAllText(filePath);
            var wrapper = JsonSerializer.Deserialize<CategoryWrapper>(json);
            return wrapper?.Categories ?? new List<CategoryModel>();
        }

        private class CategoryWrapper
        {
            public List<CategoryModel> Categories { get; set; } = new();
        }
    }
}
