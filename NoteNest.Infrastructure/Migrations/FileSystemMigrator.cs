using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NoteNest.Core.Services.Logging;
using NoteNest.Application.Common.Interfaces;
using NoteNest.Infrastructure.Projections;
using NoteNest.Domain.Categories;
using NoteNest.Domain.Categories.Events;
using NoteNest.Domain.Notes;
using NoteNest.Domain.Notes.Events;

namespace NoteNest.Infrastructure.Migrations
{
    /// <summary>
    /// Migrates data by scanning the actual file system (RTF files).
    /// Simpler and more reliable than reading from tree.db.
    /// </summary>
    public class FileSystemMigrator
    {
        private readonly string _notesRootPath;
        private readonly IEventStore _eventStore;
        private readonly ProjectionOrchestrator _projectionOrchestrator;
        private readonly IAppLogger _logger;

        public FileSystemMigrator(
            string notesRootPath,
            IEventStore eventStore,
            ProjectionOrchestrator projectionOrchestrator,
            IAppLogger logger)
        {
            _notesRootPath = notesRootPath ?? throw new ArgumentNullException(nameof(notesRootPath));
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _projectionOrchestrator = projectionOrchestrator ?? throw new ArgumentNullException(nameof(projectionOrchestrator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<MigrationResult> MigrateAsync()
        {
            System.Console.WriteLine("📍 FileSystemMigrator.MigrateAsync() called");
            var result = new MigrationResult { StartedAt = DateTime.UtcNow };
            
            try
            {
                System.Console.WriteLine($"🔄 Scanning file system: {_notesRootPath}");
                
                if (!Directory.Exists(_notesRootPath))
                {
                    System.Console.WriteLine($"❌ Notes directory not found: {_notesRootPath}");
                    result.Success = false;
                    result.Error = "Notes directory not found";
                    return result;
                }

                var categories = new List<CategoryData>();
                var notes = new List<NoteData>();
                
                // Scan directory recursively
                ScanDirectory(_notesRootPath, null, categories, notes);
                
                System.Console.WriteLine($"   Found {categories.Count} folders");
                System.Console.WriteLine($"   Found {notes.Count} RTF files");
                
                result.CategoriesFound = categories.Count;
                result.NotesFound = notes.Count;
                
                // Generate events
                System.Console.WriteLine("⚡ Generating events...");
                var eventCount = 0;
                
                // Create category events (parent folders first)
                foreach (var category in categories.OrderBy(c => c.Path.Count(ch => ch == '\\')))
                {
                    var catAggregate = CategoryAggregate.Create(
                        category.ParentId,
                        category.Name,
                        category.Path);
                    
                    await _eventStore.SaveAsync(catAggregate);
                    eventCount++;
                }
                
                System.Console.WriteLine($"   ✅ Created {categories.Count} category events");
                
                // Create note events
                foreach (var note in notes)
                {
                    var noteId = NoteId.Create();
                    var categoryId = note.CategoryId != null 
                        ? CategoryId.From(note.CategoryId.ToString())
                        : CategoryId.Create();
                    
                    var noteAggregate = new Note(categoryId, note.Title, string.Empty);
                    noteAggregate.SetFilePath(note.FilePath);
                    
                    await _eventStore.SaveAsync(noteAggregate);
                    eventCount++;
                }
                
                System.Console.WriteLine($"   ✅ Created {notes.Count} note events");
                
                result.EventsGenerated = eventCount;

                // Rebuild projections
                System.Console.WriteLine("🔨 Rebuilding projections...");
                await _projectionOrchestrator.RebuildAllAsync();
                System.Console.WriteLine("✅ Projections rebuilt");

                result.Success = true;
                result.CompletedAt = DateTime.UtcNow;
                
                var duration = (result.CompletedAt.Value - result.StartedAt).TotalMinutes;
                System.Console.WriteLine($"🎉 Migration complete in {duration:F1} minutes!");
                System.Console.WriteLine($"   Categories: {result.CategoriesFound}");
                System.Console.WriteLine($"   Notes: {result.NotesFound}");
                System.Console.WriteLine($"   Events: {result.EventsGenerated}");

                return result;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"❌ EXCEPTION in FileSystemMigrator");
                System.Console.WriteLine($"   Error: {ex.Message}");
                System.Console.WriteLine($"   Stack: {ex.StackTrace}");
                
                result.Success = false;
                result.Error = ex.Message;
                result.CompletedAt = DateTime.UtcNow;
                return result;
            }
        }

        private void ScanDirectory(string path, Guid? parentId, List<CategoryData> categories, List<NoteData> notes)
        {
            try
            {
                // Create category for this directory
                var categoryId = Guid.NewGuid();
                var dirInfo = new DirectoryInfo(path);
                
                // Skip hidden directories (start with .)
                if (dirInfo.Name.StartsWith("."))
                    return;
                
                categories.Add(new CategoryData
                {
                    Id = categoryId,
                    ParentId = parentId,
                    Name = dirInfo.Name,
                    Path = path
                });
                
                // Scan files
                var files = Directory.GetFiles(path, "*.rtf", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    
                    // Skip hidden files
                    if (fileInfo.Name.StartsWith("."))
                        continue;
                    
                    notes.Add(new NoteData
                    {
                        CategoryId = categoryId,
                        Title = Path.GetFileNameWithoutExtension(file),
                        FilePath = file
                    });
                }
                
                // Recursively scan subdirectories
                var subdirs = Directory.GetDirectories(path);
                foreach (var subdir in subdirs)
                {
                    ScanDirectory(subdir, categoryId, categories, notes);
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"   ⚠️ Error scanning {path}: {ex.Message}");
            }
        }

        private class CategoryData
        {
            public Guid Id { get; set; }
            public Guid? ParentId { get; set; }
            public string Name { get; set; }
            public string Path { get; set; }
        }

        private class NoteData
        {
            public Guid CategoryId { get; set; }
            public string Title { get; set; }
            public string FilePath { get; set; }
        }
    }
}

