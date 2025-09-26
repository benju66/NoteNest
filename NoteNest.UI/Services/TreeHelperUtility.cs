using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NoteNest.Core.Models;
using NoteNest.UI.ViewModels;

namespace NoteNest.UI.Services
{
    /// <summary>
    /// Static utility class containing tree navigation and operation helper methods.
    /// Extracted from MainViewModel to provide reusable tree operations.
    /// </summary>
    public static class TreeHelperUtility
    {
        /// <summary>
        /// Recursively counts all categories in a collection, including subcategories
        /// </summary>
        public static int CountAllCategories(ObservableCollection<CategoryTreeItem> nodes)
        {
            if (nodes == null) return 0;
            
            int count = nodes.Count;
            foreach (var node in nodes)
            {
                count += CountAllCategories(node.SubCategories);
            }
            return count;
        }

        /// <summary>
        /// Recursively counts all notes in a category and its subcategories
        /// </summary>
        public static int CountAllNotes(CategoryTreeItem category)
        {
            if (category == null) return 0;
            
            int count = category.Notes?.Count ?? 0;
            foreach (var sub in category.SubCategories ?? Enumerable.Empty<CategoryTreeItem>())
            {
                count += CountAllNotes(sub);
            }
            return count;
        }

        /// <summary>
        /// Recursively finds a category by ID in a collection
        /// </summary>
        public static CategoryTreeItem FindCategoryById(ObservableCollection<CategoryTreeItem> categories, string id)
        {
            if (string.IsNullOrEmpty(id) || categories == null) return null;
            
            foreach (var category in categories)
            {
                if (string.Equals(category.Model?.Id, id, StringComparison.OrdinalIgnoreCase))
                    return category;
                
                var found = FindCategoryById(category.SubCategories, id);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Recursively finds the category that contains a specific note
        /// </summary>
        public static CategoryTreeItem FindCategoryContainingNote(CategoryTreeItem category, NoteTreeItem note)
        {
            if (category == null || note == null) return null;
            
            if (category.Notes?.Contains(note) == true) return category;
            
            foreach (var subCategory in category.SubCategories ?? Enumerable.Empty<CategoryTreeItem>())
            {
                var found = FindCategoryContainingNote(subCategory, note);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Finds a note by ID across all categories in a collection
        /// </summary>
        public static NoteTreeItem FindNoteById(ObservableCollection<CategoryTreeItem> categories, string noteId)
        {
            if (string.IsNullOrEmpty(noteId) || categories == null) return null;
            
            foreach (var category in categories)
            {
                var found = FindNoteInCategory(category, noteId);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Recursively finds a note by ID within a specific category
        /// </summary>
        public static NoteTreeItem FindNoteInCategory(CategoryTreeItem category, string noteId)
        {
            if (category == null || string.IsNullOrEmpty(noteId)) return null;
            
            var note = category.Notes?.FirstOrDefault(n => n.Model.Id == noteId);
            if (note != null) return note;

            foreach (var subCategory in category.SubCategories ?? Enumerable.Empty<CategoryTreeItem>())
            {
                var found = FindNoteInCategory(subCategory, noteId);
                if (found != null) return found;
            }
            
            return null;
        }

        /// <summary>
        /// Recursively finds a note by file path within a specific category
        /// </summary>
        public static string FindNoteIdInCategory(CategoryTreeItem category, string filePath)
        {
            if (category == null || string.IsNullOrEmpty(filePath)) return null;
            
            foreach (var note in category.Notes ?? Enumerable.Empty<NoteTreeItem>())
            {
                if (string.Equals(note.Model.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    return note.Model.Id;
                }
            }
            
            foreach (var sub in category.SubCategories ?? Enumerable.Empty<CategoryTreeItem>())
            {
                var noteId = FindNoteIdInCategory(sub, filePath);
                if (!string.IsNullOrEmpty(noteId))
                    return noteId;
            }
            
            return null;
        }

        /// <summary>
        /// Recursively collects all note models from a category and its subcategories
        /// </summary>
        public static void CollectAllNotes(CategoryTreeItem category, List<NoteModel> allNotes)
        {
            if (category == null || allNotes == null) return;
            
            foreach (var note in category.Notes ?? Enumerable.Empty<NoteTreeItem>())
            {
                allNotes.Add(note.Model);
            }
            
            foreach (var subCategory in category.SubCategories ?? Enumerable.Empty<CategoryTreeItem>())
            {
                CollectAllNotes(subCategory, allNotes);
            }
        }

        /// <summary>
        /// Gets sibling category names for validation purposes
        /// </summary>
        public static IEnumerable<string> GetSiblingCategoryNames(
            CategoryTreeItem categoryItem, 
            ObservableCollection<CategoryTreeItem> rootCategories)
        {
            if (categoryItem?.Model == null || rootCategories == null) 
                return Enumerable.Empty<string>();
            
            if (categoryItem.Model.ParentId == null)
            {
                // Root level - check against other root categories
                return rootCategories
                    .Where(c => c != categoryItem)
                    .Select(c => c.Name)
                    .Where(name => !string.IsNullOrEmpty(name));
            }
            else
            {
                // Find parent and get siblings
                var parent = FindCategoryById(rootCategories, categoryItem.Model.ParentId);
                return parent?.SubCategories?
                    .Where(c => c != categoryItem)
                    .Select(c => c.Name)
                    .Where(name => !string.IsNullOrEmpty(name)) ?? Enumerable.Empty<string>();
            }
        }
    }
}
