using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Dapper;

var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var projectionsDbPath = Path.Combine(localAppData, "NoteNest", "projections.db");

Console.WriteLine($"Checking: {projectionsDbPath}");
Console.WriteLine($"File exists: {File.Exists(projectionsDbPath)}");

if (!File.Exists(projectionsDbPath))
{
    Console.WriteLine("ERROR: projections.db does not exist!");
    return;
}

var connectionString = $"Data Source={projectionsDbPath};";

using var connection = new SqliteConnection(connectionString);
connection.Open();

// Check tree_view table
var categoryCount = connection.QuerySingle<int>("SELECT COUNT(*) FROM tree_view WHERE node_type = 'category'");
var noteCount = connection.QuerySingle<int>("SELECT COUNT(*) FROM tree_view WHERE node_type = 'note'");
var total = connection.QuerySingle<int>("SELECT COUNT(*) FROM tree_view");

Console.WriteLine($"\nTree View Stats:");
Console.WriteLine($"  Categories: {categoryCount}");
Console.WriteLine($"  Notes: {noteCount}");
Console.WriteLine($"  Total: {total}");

// Show first 5 categories
Console.WriteLine($"\nFirst 5 categories:");
var categories = connection.Query("SELECT id, parent_id, name, canonical_path FROM tree_view WHERE node_type = 'category' LIMIT 5");
foreach (var cat in categories)
{
    Console.WriteLine($"  - {cat.name} (id: {cat.id}, parent: {cat.parent_id}, path: {cat.canonical_path})");
}

// Check for root categories (parent_id IS NULL)
var rootCount = connection.QuerySingle<int>("SELECT COUNT(*) FROM tree_view WHERE node_type = 'category' AND parent_id IS NULL");
Console.WriteLine($"\nRoot categories (parent_id IS NULL): {rootCount}");

if (rootCount > 0)
{
    var roots = connection.Query("SELECT id, name, canonical_path FROM tree_view WHERE node_type = 'category' AND parent_id IS NULL");
    foreach (var root in roots)
    {
        Console.WriteLine($"  - {root.name} (path: {root.canonical_path})");
    }
}

// Done
