using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Dapper;

class Program
{
    static void Main()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbPath = Path.Combine(localAppData, "NoteNest", "projections.db");
        var connectionString = $"Data Source={dbPath}";
        
        Console.WriteLine($"Checking: {dbPath}");
        Console.WriteLine();
        
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        
        // Check notes
        var notes = connection.Query<dynamic>(
            "SELECT id, name, display_path, node_type FROM tree_view WHERE node_type = 'note' LIMIT 10");
        
        Console.WriteLine("=== SAMPLE NOTES ===");
        foreach (var note in notes)
        {
            Console.WriteLine($"ID: {note.id}");
            Console.WriteLine($"Name: {note.name}");
            Console.WriteLine($"DisplayPath: {note.display_path}");
            Console.WriteLine($"Is Null/Empty: {string.IsNullOrEmpty((string)note.display_path)}");
            Console.WriteLine();
        }
        
        // Check categories
        var categories = connection.Query<dynamic>(
            "SELECT id, name, display_path, node_type FROM tree_view WHERE node_type = 'category' LIMIT 5");
        
        Console.WriteLine("=== SAMPLE CATEGORIES ===");
        foreach (var cat in categories)
        {
            Console.WriteLine($"ID: {cat.id}");
            Console.WriteLine($"Name: {cat.name}");
            Console.WriteLine($"DisplayPath: {cat.display_path}");
            Console.WriteLine();
        }
    }
}
