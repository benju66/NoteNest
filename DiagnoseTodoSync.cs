// Diagnostic tool to see what's in tree_view
using System;
using System.IO;
using Microsoft.Data.Sqlite;

var dbPath = @"C:\Users\Burness\MyNotes\Notes\.notenest\projections.db";
var notesRoot = @"C:\Users\Burness\MyNotes\Notes";
var testNotePath = @"C:\Users\Burness\MyNotes\Notes\Projects\25-117 - OP III\Daily Notes\Note 2025.10.20 - 10.24.rtf";

Console.WriteLine("=== DIAGNOSTIC: Why TodoSync Can't Find Categories ===\n");

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

// 1. Show ALL categories in tree_view
Console.WriteLine("📊 ALL CATEGORIES IN tree_view:");
Console.WriteLine(new string('-', 80));

using var cmd1 = conn.CreateCommand();
cmd1.CommandText = "SELECT id, canonical_path, display_path, name FROM tree_view WHERE node_type = 'category' ORDER BY canonical_path";
using var reader1 = cmd1.ExecuteReader();

int count = 0;
while (reader1.Read())
{
    count++;
    var canonical = reader1.GetString(1);
    var display = reader1.GetString(2);
    var name = reader1.GetString(3);
    
    var highlight = canonical.Contains("25-117") ? " <<<< TARGET" : "";
    Console.WriteLine($"{count}. {name}");
    Console.WriteLine($"   Canonical: '{canonical}'{highlight}");
    Console.WriteLine($"   Display:   '{display}'");
    Console.WriteLine();
}

Console.WriteLine($"Total categories: {count}\n");

// 2. Show what hierarchical lookup is trying
Console.WriteLine("🔍 WHAT HIERARCHICAL LOOKUP IS TRYING:");
Console.WriteLine(new string('-', 80));

var parentFolder = Path.GetDirectoryName(testNotePath);
int level = 0;

while (!string.IsNullOrEmpty(parentFolder) && level < 10)
{
    if (parentFolder.Length <= notesRoot.Length) break;
    
    var relPath = Path.GetRelativePath(notesRoot, parentFolder);
    var canonical = relPath.Replace('\\', '/').ToLowerInvariant();
    
    Console.WriteLine($"Level {level + 1}: Trying '{canonical}'");
    
    // Check if it exists
    using var cmd2 = conn.CreateCommand();
    cmd2.CommandText = "SELECT id, name FROM tree_view WHERE canonical_path = @path";
    cmd2.Parameters.AddWithValue("@path", canonical);
    using var reader2 = cmd2.ExecuteReader();
    
    if (reader2.Read())
    {
        Console.WriteLine($"   ✅ FOUND: {reader2.GetString(1)} (ID: {reader2.GetString(0)})");
    }
    else
    {
        Console.WriteLine($"   ❌ NOT FOUND");
        
        // Try to find similar paths
        using var cmd3 = conn.CreateCommand();
        cmd3.CommandText = "SELECT canonical_path, name FROM tree_view WHERE canonical_path LIKE @pattern COLLATE NOCASE LIMIT 3";
        cmd3.Parameters.AddWithValue("@pattern", $"%{Path.GetFileName(parentFolder).ToLowerInvariant()}%");
        using var reader3 = cmd3.ExecuteReader();
        
        if (reader3.Read())
        {
            Console.WriteLine($"   💡 Similar: '{reader3.GetString(0)}' ({reader3.GetString(1)})");
        }
    }
    
    Console.WriteLine();
    parentFolder = Path.GetDirectoryName(parentFolder);
    level++;
}

Console.WriteLine("\n=== DIAGNOSIS COMPLETE ===");
Console.WriteLine("\nPress any key to exit...");

