# Simple database checker
$dbPath = "$env:LOCALAPPDATA\NoteNest\todos.db"
Add-Type -Path "C:\NoteNest\NoteNest.UI\bin\Debug\net9.0-windows\Microsoft.Data.Sqlite.dll"

$conn = New-Object Microsoft.Data.Sqlite.SqliteConnection("Data Source=$dbPath")
$conn.Open()

Write-Host "`n=== TODOS DATABASE STATE ===`n" -ForegroundColor Cyan

$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT id, text, category_id, is_orphaned, is_completed FROM todos WHERE is_completed = 0"
$reader = $cmd.ExecuteReader()

$nullCount = 0
$hasCategory = 0

while ($reader.Read()) {
    $text = $reader["text"]
    $catId = if ($reader["category_id"] -is [DBNull]) { "NULL" } else { $reader["category_id"].ToString().Substring(0,8) }
    $orphaned = $reader["is_orphaned"]
    
    Write-Host "Text: $text"
    Write-Host "  Category: $catId, Orphaned: $orphaned`n"
    
    if ($catId -eq "NULL") { $nullCount++ } else { $hasCategory++ }
}
$reader.Close()

Write-Host "=== SUMMARY ===" -ForegroundColor Yellow
Write-Host "Todos with NULL category_id: $nullCount"
Write-Host "Todos with valid category_id: $hasCategory"

$conn.Close()

