# Todo Database Diagnostic Tool
# Checks the actual state of todos in the database

$dbPath = "$env:LOCALAPPDATA\NoteNest\todos.db"

if (!(Test-Path $dbPath)) {
    Write-Host "Database not found at: $dbPath" -ForegroundColor Red
    exit
}

Add-Type -AssemblyName System.Data

$connectionString = "Data Source=$dbPath;Version=3;"
$connection = New-Object System.Data.SQLite.SQLiteConnection($connectionString)

try {
    $connection.Open()
    
    Write-Host "`n=== ACTIVE TODOS IN DATABASE ===`n" -ForegroundColor Cyan
    
    $query = @"
SELECT 
    SUBSTR(id, 1, 8) as id,
    text,
    SUBSTR(COALESCE(category_id, 'NULL'), 1, 8) as category,
    is_orphaned as orphaned,
    is_completed as completed,
    source_type as source
FROM todos 
WHERE is_completed = 0
ORDER BY created_at DESC
LIMIT 20;
"@
    
    $command = $connection.CreateCommand()
    $command.CommandText = $query
    $reader = $command.ExecuteReader()
    
    $count = 0
    while ($reader.Read()) {
        $count++
        Write-Host ("ID: {0,-10} Text: {1,-30} Category: {2,-10} Orphaned: {3} Source: {4}" -f `
            $reader["id"], `
            $reader["text"].ToString().Substring(0, [Math]::Min(30, $reader["text"].ToString().Length)), `
            $reader["category"], `
            $reader["orphaned"], `
            $reader["source"])
    }
    $reader.Close()
    
    Write-Host "`nTotal active todos: $count`n" -ForegroundColor Yellow
    
    # Check category store
    Write-Host "=== SAVED CATEGORIES (user_preferences) ===`n" -ForegroundColor Cyan
    
    $query2 = "SELECT value FROM user_preferences WHERE key = 'selected_categories';"
    $command2 = $connection.CreateCommand()
    $command2.CommandText = $query2
    $result = $command2.ExecuteScalar()
    
    if ($result) {
        Write-Host $result -ForegroundColor Green
    } else {
        Write-Host "No saved categories found" -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
} finally {
    $connection.Close()
}

Write-Host "`n=== DIAGNOSIS ===`n" -ForegroundColor Cyan
Write-Host "If todos show category='NULL' → They were orphaned by category deletion"
Write-Host "If todos show orphaned=1 → They were soft-deleted or bracket removed"
Write-Host "If category IDs don't match saved categories → Category ID mismatch issue"

