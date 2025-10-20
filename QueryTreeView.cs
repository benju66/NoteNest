// Quick database query tool
using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using Dapper;

var dbPath = @"C:\Users\Burness\MyNotes\Notes\.notenest\projections.db";
var connectionString = $"Data Source={dbPath};Mode=ReadOnly";

Console.WriteLine("\n🔍 Querying tree_view in projections.db...");
Console.WriteLine($"Database: {dbPath}\n");

using var connection = new SqliteConnection(connectionString);
connection.Open();

// Query all categories
Console.WriteLine("📊 ALL Categories in tree_view:");
Console.WriteLine(new string('=', 80));

var allCategories = connection.Query(
    "SELECT id, path, name FROM tree_view WHERE node_type = 1 ORDER BY path COLLATE NOCASE"
);

int count = 0;
foreach (var cat in allCategories)
{
    count++;
    var prefix = cat.path.ToString().ToLower().Contains("25-117") ? "✅ MATCH: " : "   ";
    Console.WriteLine($"{prefix}{cat.name} | Path: {cat.path} | ID: {cat.id}");
}

Console.WriteLine($"\n📈 Total categories: {count}\n");

// Check specific paths
Console.WriteLine("🔎 Exact paths being looked up by hierarchical code:");
Console.WriteLine(new string('=', 80));

var lookups = new[] {
    "projects/25-117 - op iii/daily notes",
    "projects/25-117 - op iii",
    "projects"
};

foreach (var lookup in lookups)
{
    var result = connection.QueryFirstOrDefault(
        "SELECT id, path, name FROM tree_view WHERE path = @path COLLATE NOCASE",
        new { path = lookup }
    );
    
    if (result != null)
    {
        Console.WriteLine($"✅ FOUND: '{lookup}' → {result.name} (ID: {result.id})");
    }
    else
    {
        Console.WriteLine($"❌ NOT FOUND: '{lookup}'");
    }
}

Console.WriteLine("\n✅ Query complete!\n");

