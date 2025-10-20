# Check what's in todo_view
$dbPath = "C:\Users\Burness\AppData\Local\NoteNest\projections.db"

Add-Type -LiteralPath "NoteNest.UI\bin\Debug\net9.0-windows\Microsoft.Data.Sqlite.dll"

$conn = New-Object Microsoft.Data.Sqlite.SqliteConnection("Data Source=$dbPath")
$conn.Open()

Write-Output "=== TODOS IN todo_view ==="
Write-Output ""

$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT id, text, category_id, category_name, source_file_path FROM todo_view ORDER BY created_at DESC LIMIT 10"
$reader = $cmd.ExecuteReader()

while ($reader.Read()) {
    $id = $reader["id"]
    $text = $reader["text"]
    $catId = if ($reader["category_id"] -is [DBNull]) { "NULL" } else { $reader["category_id"] }
    $catName = if ($reader["category_name"] -is [DBNull]) { "NULL" } else { $reader["category_name"] }
    $source = if ($reader["source_file_path"] -is [DBNull]) { "manual" } else { $reader["source_file_path"] }
    
    Write-Output "Todo: $text"
    Write-Output "  ID: $id"
    Write-Output "  CategoryId: $catId"
    Write-Output "  CategoryName: $catName"
    Write-Output "  Source: $source"
    Write-Output ""
}

$reader.Close()
$conn.Close()

Write-Output "=== END ===" 

