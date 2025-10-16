using System;
using System.IO;
using System.Threading.Tasks;
using NoteNest.Infrastructure.Migrations;
using NoteNest.Core.Services.Logging;

namespace NoteNest.Console
{
    /// <summary>
    /// Quick utility to verify tag migration status
    /// </summary>
    public class CheckTagMigration
    {
        public static async Task RunAsync()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var notesRoot = Path.Combine(localAppData, "NoteNest", "Notes");
            var treeDbPath = Path.Combine(notesRoot, "tree.db");
            var todosDbPath = Path.Combine(localAppData, "NoteNest", ".plugins", "NoteNest.TodoPlugin", "todos.db");
            
            var logger = new ConsoleLogger();
            var migrationUtil = new UnifiedTagMigrationUtility(treeDbPath, todosDbPath, logger);
            
            System.Console.WriteLine("🔍 Checking Tag Migration Status...\n");
            
            // Ensure migration history table exists
            await migrationUtil.EnsureMigrationHistoryTableAsync();
            
            // Run verification
            var result = await migrationUtil.VerifyMigrationAsync();
            
            if (result.IsSuccessful)
            {
                System.Console.WriteLine($"✅ Migration Status: SUCCESS");
                System.Console.WriteLine($"📊 Total Unique Tags: {result.TotalUniqueTags}");
                System.Console.WriteLine($"\n📁 Tags by Entity Type:");
                foreach (var entity in result.EntityTypeCounts)
                {
                    System.Console.WriteLine($"   - {entity.Key}: {entity.Value.UniqueTags} unique tags, {entity.Value.TotalAssociations} associations");
                }
                System.Console.WriteLine($"\n🏷️ Tags by Category:");
                foreach (var cat in result.CategoryCounts)
                {
                    System.Console.WriteLine($"   - {cat.Key}: {cat.Value} tags");
                }
            }
            else
            {
                System.Console.WriteLine($"❌ Migration Status: FAILED");
                System.Console.WriteLine($"Error: {result.ErrorMessage}");
            }
            
            System.Console.WriteLine("\nPress any key to exit...");
            System.Console.ReadKey();
        }
    }
    
    public class ConsoleLogger : IAppLogger
    {
        public void Debug(string message, params object[] args) => System.Console.WriteLine($"[DEBUG] {string.Format(message, args)}");
        public void Info(string message, params object[] args) => System.Console.WriteLine($"[INFO] {string.Format(message, args)}");
        public void Warning(string message, params object[] args) => System.Console.WriteLine($"[WARN] {string.Format(message, args)}");
        public void Error(string message, params object[] args) => System.Console.WriteLine($"[ERROR] {string.Format(message, args)}");
        public void Error(Exception exception, string message, params object[] args) 
        {
            System.Console.WriteLine($"[ERROR] {string.Format(message, args)}");
            System.Console.WriteLine($"[ERROR] {exception}");
        }
        public void Fatal(string message, params object[] args) => System.Console.WriteLine($"[FATAL] {string.Format(message, args)}");
        public void Fatal(Exception exception, string message, params object[] args)
        {
            System.Console.WriteLine($"[FATAL] {string.Format(message, args)}");
            System.Console.WriteLine($"[FATAL] {exception}");
        }
    }
}
