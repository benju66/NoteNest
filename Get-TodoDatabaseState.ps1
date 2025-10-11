# Simple Todo Database Diagnostic
# Uses .NET Data Provider (built-in to Windows)

$dbPath = "$env:LOCALAPPDATA\NoteNest\todos.db"

if (!(Test-Path $dbPath)) {
    Write-Host "Database not found: $dbPath" -ForegroundColor Red
    exit
}

Write-Host "`n=== TODO DATABASE STATE ===" -ForegroundColor Cyan
Write-Host "Database: $dbPath`n"

# Load SQLite provider
Add-Type -Path "C:\NoteNest\NoteNest.UI\bin\Debug\net9.0-windows\Microsoft.Data.Sqlite.dll"

$conn = New-Object Microsoft.Data.Sqlite.SqliteConnection("Data Source=$dbPath")

try {
    $conn.Open()
    
    # Query todos
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = @"
        SELECT 
            SUBSTR(id, 1, 8) as id,
            text,
            SUBSTR(COALESCE(category_id, 'NULL'), 1, 8) as cat_id,
            is_orphaned,
            is_completed,
            source_type
        FROM todos 
        ORDER BY created_at DESC
        LIMIT 20
    "@
    
    $reader = $cmd.ExecuteReader()
    
    Write-Host ("{0,-10} {1,-35} {2,-10} {3,-8} {4,-10} {5}" -f "ID", "Text", "Category", "Orphaned", "Completed", "Source")
    Write-Host ("-" * 90)
    
    $totalActive = 0
    $totalOrphaned = 0
    $totalNullCategory = 0
    
    while ($reader.Read()) {
        $id = $reader["id"]
        $text = $reader["text"].ToString()
        if ($text.Length > 35) { $text = $text.Substring(0, 32) + "..." }
        $catId = $reader["cat_id"]
        $orphaned = $reader["is_orphaned"]
        $completed = $reader["is_completed"]
        $source = $reader["source_type"]
        
        Write-Host ("{0,-10} {1,-35} {2,-10} {3,-8} {4,-10} {5}" -f $id, $text, $catId, $orphaned, $completed, $source)
        
        if ($completed -eq 0) { $totalActive++ }
        if ($orphaned -eq 1) { $totalOrphaned++ }
        if ($catId -eq "NULL") { $totalNullCategory++ }
    }
    $reader.Close()
    
    Write-Host "`n--- SUMMARY ---" -ForegroundColor Yellow
    Write-Host "Active todos (not completed): $totalActive"
    Write-Host "Orphaned todos (is_orphaned=1): $totalOrphaned"
    Write-Host "NULL category (category_id=NULL): $totalNullCategory"
    
    # Check saved categories
    Write-Host "`n=== SAVED CATEGORIES ===" -ForegroundColor Cyan
    $cmd2 = $conn.CreateCommand()
    $cmd2.CommandText = "SELECT value FROM user_preferences WHERE key = 'selected_categories'"
    $categoriesJson = $cmd2.ExecuteScalar()
    
    if ($categoriesJson) {
        Write-Host $categoriesJson -ForegroundColor Green
    } else {
        Write-Host "No saved categories" -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message
} finally {
    $conn.Close()
}

Write-Host ""

