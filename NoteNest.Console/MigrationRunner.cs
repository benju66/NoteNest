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
            var logger = AppLogger.Instance;
            
            try
            {
                logger.Info("═══════════════════════════════════════════════════════");
                logger.Info("   EVENT SOURCING MIGRATION");
                logger.Info("   Importing legacy data to event store...");
                logger.Info("═══════════════════════════════════════════════════════");
                logger.Info("");
                
                // Database paths
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var databasePath = Path.Combine(localAppData, "NoteNest");
                
                var treeDbPath = Path.Combine(databasePath, "tree.db");
                var todosDbPath = Path.Combine(databasePath, ".plugins", "NoteNest.TodoPlugin", "todos.db");
                var eventsDbPath = Path.Combine(databasePath, "events.db");
                var projectionsDbPath = Path.Combine(databasePath, "projections.db");
                
                var notesRootPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NoteNest");
                
                logger.Info($"📂 Database Paths:");
                logger.Info($"   Tree DB: {treeDbPath}");
                logger.Info($"   Todos DB: {todosDbPath}");
                logger.Info($"   Events DB: {eventsDbPath}");
                logger.Info($"   Projections DB: {projectionsDbPath}");
                logger.Info($"   Notes Root: {notesRootPath}");
                logger.Info("");
                
                // Verify source databases exist
                if (!File.Exists(treeDbPath))
                {
                    logger.Error($"❌ Source database not found: {treeDbPath}");
                    logger.Info("💡 The tree.db file doesn't exist yet.");
                    logger.Info("   This is normal for a fresh installation.");
                    logger.Info("   The event-sourced system will start fresh.");
                    return 0;
                }
                
                if (!File.Exists(todosDbPath))
                {
                    logger.Warning($"⚠️ Todos database not found: {todosDbPath}");
                    logger.Info("   Continuing with notes/categories/tags only...");
                }
                
                // Initialize event store
                logger.Info("🔧 Initializing event store...");
                var eventsConnection = $"Data Source={eventsDbPath};";
                var eventSerializer = new JsonEventSerializer(logger);
                var eventStoreInit = new EventStoreInitializer(eventsConnection, logger);
                
                if (!await eventStoreInit.InitializeAsync())
                {
                    logger.Error("❌ Failed to initialize event store");
                    return 1;
                }
                
                var eventStore = new SqliteEventStore(eventsConnection, logger, eventSerializer);
                logger.Info("✅ Event store initialized");
                logger.Info("");
                
                // Initialize projections
                logger.Info("🔧 Initializing projections database...");
                var projectionsConnection = $"Data Source={projectionsDbPath};";
                var projectionsInit = new ProjectionsInitializer(projectionsConnection, logger);
                
                if (!await projectionsInit.InitializeAsync())
                {
                    logger.Error("❌ Failed to initialize projections database");
                    return 1;
                }
                
                logger.Info("✅ Projections database initialized");
                logger.Info("");
                
                // Check if migration already run
                var currentEventCount = await eventStoreInit.GetEventCountAsync();
                if (currentEventCount > 0)
                {
                    logger.Warning($"⚠️ Event store already has {currentEventCount} events");
                    logger.Info("   Migration may have already been run.");
                    logger.Info("   To re-migrate, delete events.db and projections.db first.");
                    logger.Info("");
                    logger.Info("❓ Continue anyway? (will append to existing events)");
                    
                    // For hands-off mode, skip if events exist
                    logger.Info("   Skipping migration (events already exist)");
                    return 0;
                }
                
                // Set up projections
                logger.Info("🔧 Setting up projections...");
                var projections = new System.Collections.Generic.List<IProjection>
                {
                    new TreeViewProjection(projectionsConnection, logger),
                    new TagProjection(projectionsConnection, logger)
                    // TodoProjection would be here but it's in UI layer
                };
                
                var orchestrator = new ProjectionOrchestrator(eventStore, projections, logger);
                logger.Info("✅ Projection orchestrator ready");
                logger.Info("");
                
                // Run migration
                logger.Info("🚀 Starting migration...");
                logger.Info("");
                
                var treeConnection = $"Data Source={treeDbPath};";
                var todosConnection = $"Data Source={todosDbPath};";
                
                var migrator = new LegacyDataMigrator(
                    treeConnection,
                    todosConnection,
                    notesRootPath,
                    eventStore,
                    orchestrator,
                    logger);
                
                var result = await migrator.MigrateAsync();
                
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
                logger.Error("❌ MIGRATION EXCEPTION", ex);
                logger.Info("");
                logger.Info("💡 Your original databases are unchanged.");
                logger.Info("   You can safely try again after fixing the issue.");
                return 1;
            }
        }
    }
}

