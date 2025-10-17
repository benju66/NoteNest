using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using NoteNest.Core.Services.Logging;
using NoteNest.Infrastructure.EventStore;
using NoteNest.Infrastructure.Projections;
using NoteNest.Infrastructure.Migrations;
using NoteNest.Application.Projections;

namespace NoteNest.Console
{
    /// <summary>
    /// Runs the legacy data migration to event store.
    /// Execute this once to import existing data before using the event-sourced system.
    /// </summary>
    public class MigrationRunner
    {
        public static async Task<int> RunMigrationAsync()
        {
            try
            {
                System.Console.WriteLine("═══════════════════════════════════════════════════════");
                System.Console.WriteLine("   EVENT SOURCING MIGRATION");
                System.Console.WriteLine("   Importing legacy data to event store...");
                System.Console.WriteLine("═══════════════════════════════════════════════════════");
                System.Console.WriteLine("");
                
                var logger = AppLogger.Instance;
                System.Console.WriteLine("✅ Logger initialized");
            
                try
                {
                
                // Database paths
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var databasePath = Path.Combine(localAppData, "NoteNest");
                
                var treeDbPath = Path.Combine(databasePath, "tree.db");
                var todosDbPath = Path.Combine(databasePath, ".plugins", "NoteNest.TodoPlugin", "todos.db");
                var eventsDbPath = Path.Combine(databasePath, "events.db");
                var projectionsDbPath = Path.Combine(databasePath, "projections.db");
                
                // Use actual notes path for this user (the Notes subfolder has the actual structure)
                var notesRootPath = @"C:\Users\Burness\MyNotes\Notes";
                
                System.Console.WriteLine($"📂 Database Paths:");
                System.Console.WriteLine($"   Tree DB: {treeDbPath}");
                System.Console.WriteLine($"   Todos DB: {todosDbPath}");
                System.Console.WriteLine($"   Events DB: {eventsDbPath}");
                System.Console.WriteLine($"   Projections DB: {projectionsDbPath}");
                System.Console.WriteLine($"   Notes Root: {notesRootPath}");
                System.Console.WriteLine("");
                
                // Verify source databases exist
                if (!File.Exists(treeDbPath))
                {
                    System.Console.WriteLine($"❌ Source database not found: {treeDbPath}");
                    System.Console.WriteLine("💡 The tree.db file doesn't exist yet.");
                    System.Console.WriteLine("   This is normal for a fresh installation.");
                    System.Console.WriteLine("   The event-sourced system will start fresh.");
                    return 0;
                }
                
                System.Console.WriteLine($"✅ Found tree.db at: {treeDbPath}");
                
                if (!File.Exists(todosDbPath))
                {
                    logger.Warning($"⚠️ Todos database not found: {todosDbPath}");
                    logger.Info("   Continuing with notes/categories/tags only...");
                }
                
                // Initialize event store
                System.Console.WriteLine("🔧 Initializing event store...");
                var eventsConnection = $"Data Source={eventsDbPath};";
                var eventSerializer = new JsonEventSerializer(logger);
                var eventStoreInit = new EventStoreInitializer(eventsConnection, logger);
                
                System.Console.WriteLine("   Created EventStoreInitializer");
                
                if (!await eventStoreInit.InitializeAsync())
                {
                    System.Console.WriteLine("❌ Failed to initialize event store");
                    return 1;
                }
                
                System.Console.WriteLine("   EventStore initialized successfully");
                
                SqliteEventStore eventStore;
                try
                {
                    eventStore = new SqliteEventStore(eventsConnection, logger, eventSerializer);
                    System.Console.WriteLine("   Created SqliteEventStore instance");
                }
                catch (Exception esEx)
                {
                    System.Console.WriteLine($"❌ Failed to create SqliteEventStore: {esEx.Message}");
                    return 1;
                }
                
                System.Console.WriteLine("✅ Event store initialized");
                System.Console.WriteLine("");
                
                // Initialize projections
                System.Console.WriteLine("🔧 Initializing projections database...");
                var projectionsConnection = $"Data Source={projectionsDbPath};";
                var projectionsInit = new ProjectionsInitializer(projectionsConnection, logger);
                
                System.Console.WriteLine("   Created ProjectionsInitializer");
                
                if (!await projectionsInit.InitializeAsync())
                {
                    System.Console.WriteLine("❌ Failed to initialize projections database");
                    return 1;
                }
                
                System.Console.WriteLine("✅ Projections database initialized");
                System.Console.WriteLine("");
                
                // Check if migration already run
                System.Console.WriteLine("Checking if migration already ran...");
                var currentEventCount = await eventStoreInit.GetEventCountAsync();
                System.Console.WriteLine($"   Current event count: {currentEventCount}");
                
                if (currentEventCount > 0)
                {
                    System.Console.WriteLine($"⚠️ Event store already has {currentEventCount} events");
                    System.Console.WriteLine("   Migration may have already been run.");
                    System.Console.WriteLine("   To re-migrate, delete events.db and projections.db first.");
                    System.Console.WriteLine("");
                    System.Console.WriteLine("   Skipping migration (events already exist)");
                    return 0;
                }
                
                System.Console.WriteLine("✅ No existing events, proceeding with migration...");
                
                // Set up projections
                System.Console.WriteLine("🔧 Setting up projections...");
                
                System.Collections.Generic.List<IProjection> projections;
                try
                {
                    projections = new System.Collections.Generic.List<IProjection>
                    {
                        new TreeViewProjection(projectionsConnection, logger),
                        new TagProjection(projectionsConnection, logger)
                        // TodoProjection would be here but it's in UI layer
                    };
                    System.Console.WriteLine($"   Created {projections.Count} projections");
                }
                catch (Exception projEx)
                {
                    System.Console.WriteLine($"❌ Failed to create projections: {projEx.Message}");
                    return 1;
                }
                
                ProjectionOrchestrator orchestrator;
                try
                {
                    orchestrator = new ProjectionOrchestrator(eventStore, projections, eventSerializer, logger);
                    System.Console.WriteLine("   Created ProjectionOrchestrator");
                }
                catch (Exception orchEx)
                {
                    System.Console.WriteLine($"❌ Failed to create orchestrator: {orchEx.Message}");
                    return 1;
                }
                
                System.Console.WriteLine("✅ Projection orchestrator ready");
                System.Console.WriteLine("");
                
                // Run migration
                System.Console.WriteLine("🚀 Starting migration...");
                System.Console.WriteLine("");
                
                var treeConnection = $"Data Source={treeDbPath};";
                var todosConnection = $"Data Source={todosDbPath};";
                
                System.Console.WriteLine("Creating FileSystemMigrator (scans actual RTF files)...");
                FileSystemMigrator migrator;
                try
                {
                    migrator = new FileSystemMigrator(
                        notesRootPath,
                        eventStore,
                        orchestrator,
                        logger);
                    System.Console.WriteLine("   Migrator created successfully");
                }
                catch (Exception migEx)
                {
                    System.Console.WriteLine($"❌ Failed to create migrator: {migEx.Message}");
                    return 1;
                }
                
                System.Console.WriteLine("Calling migrator.MigrateAsync()...");
                MigrationResult result;
                try
                {
                    result = await migrator.MigrateAsync();
                    System.Console.WriteLine("   Migration method returned");
                }
                catch (Exception migrateEx)
                {
                    System.Console.WriteLine($"❌ Migration threw exception: {migrateEx.Message}");
                    System.Console.WriteLine($"   Stack: {migrateEx.StackTrace}");
                    return 1;
                }
                
                logger.Info("");
                logger.Info("═══════════════════════════════════════════════════════");
                
                if (result.Success)
                {
                    logger.Info("✅ MIGRATION SUCCESSFUL");
                    logger.Info("");
                    logger.Info($"📊 Migration Results:");
                    logger.Info($"   Categories Migrated: {result.CategoriesFound}");
                    logger.Info($"   Notes Migrated: {result.NotesFound}");
                    logger.Info($"   Tags Migrated: {result.TagsFound}");
                    logger.Info($"   Todos Migrated: {result.TodosFound}");
                    logger.Info($"   Total Events Generated: {result.EventsGenerated}");
                    logger.Info($"   Validation: {(result.ValidationPassed ? "✅ PASSED" : "⚠️ WARNINGS")}");
                    logger.Info("");
                    
                    var duration = (result.CompletedAt.Value - result.StartedAt).TotalMinutes;
                    logger.Info($"   Duration: {duration:F1} minutes");
                    logger.Info("");
                    
                    // Show projection stats
                    var stats = await projectionsInit.GetStatsAsync();
                    logger.Info($"📊 Projection Statistics:");
                    logger.Info($"   Tree View: {stats.TreeViewCount} nodes");
                    logger.Info($"   Tags: {stats.TagVocabularyCount} unique tags");
                    logger.Info($"   Tag Associations: {stats.EntityTagsCount}");
                    logger.Info($"   Todos: {stats.TodoViewCount} items");
                    logger.Info("");
                    
                    logger.Info("🎉 Event sourcing is now active!");
                    logger.Info("   Your data has been migrated to the event store.");
                    logger.Info("   All future changes will be tracked as events.");
                    logger.Info("");
                    logger.Info("✅ You can now run the main application.");
                }
                else
                {
                    logger.Error("❌ MIGRATION FAILED");
                    logger.Error($"   Error: {result.Error}");
                    logger.Info("");
                    logger.Info("💡 Check the logs above for details.");
                    logger.Info("   Your original databases (tree.db, todos.db) are unchanged.");
                }
                
                logger.Info("═══════════════════════════════════════════════════════");
                
                return result.Success ? 0 : 1;
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine("❌ MIGRATION EXCEPTION (Inner)");
                    System.Console.WriteLine($"   Error: {ex.Message}");
                    System.Console.WriteLine($"   Stack: {ex.StackTrace}");
                    return 1;
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("❌ MIGRATION EXCEPTION (Outer)");
                System.Console.WriteLine($"   Error: {ex.Message}");
                System.Console.WriteLine($"   Stack: {ex.StackTrace}");
                return 1;
            }
        }
    }
}

