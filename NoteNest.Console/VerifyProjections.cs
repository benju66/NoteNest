using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;

namespace NoteNest.Console
{
    public class VerifyProjections
    {
        public static async Task RunAsync()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var projectionsDbPath = Path.Combine(localAppData, "NoteNest", "projections.db");
            
            System.Console.WriteLine("═══════════════════════════════════════");
            System.Console.WriteLine("   VERIFY PROJECTIONS DATA");
            System.Console.WriteLine("═══════════════════════════════════════");
            System.Console.WriteLine($"Database: {projectionsDbPath}");
            System.Console.WriteLine("");
            
            if (!File.Exists(projectionsDbPath))
            {
                System.Console.WriteLine("❌ projections.db not found!");
                return;
            }
            
            var connectionString = $"Data Source={projectionsDbPath};";
            
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            
            // Check tree_view
            var treeCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tree_view");
            System.Console.WriteLine($"Tree View: {treeCount} nodes");
            
            var categoryCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tree_view WHERE node_type = 'category'");
            System.Console.WriteLine($"  - Categories: {categoryCount}");
            
            var noteCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tree_view WHERE node_type = 'note'");
            System.Console.WriteLine($"  - Notes: {noteCount}");
            
            // Check tags
            var tagCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tag_vocabulary");
            System.Console.WriteLine($"Tag Vocabulary: {tagCount} unique tags");
            
            var tagAssocCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM entity_tags");
            System.Console.WriteLine($"Tag Associations: {tagAssocCount}");
            
            // Check todos
            var todoCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM todo_view");
            System.Console.WriteLine($"Todos: {todoCount}");
            
            System.Console.WriteLine("");
            
            // Show sample nodes
            if (treeCount > 0)
            {
                System.Console.WriteLine("Sample nodes:");
                var samples = await connection.QueryAsync<dynamic>(
                    "SELECT id, name, node_type, parent_id FROM tree_view LIMIT 5");
                
                foreach (var sample in samples)
                {
                    System.Console.WriteLine($"  {sample.node_type}: {sample.name} (ID: {sample.id})");
                }
            }
        }
    }
}

