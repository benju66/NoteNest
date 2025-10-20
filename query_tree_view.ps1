# Query projections.db tree_view table
param(
    [string]$SearchPattern = "%25-117%"
)

$dbPath = "C:\Users\Burness\MyNotes\Notes\.notenest\projections.db"
$dllPath = "NoteNest.UI\bin\Debug\net9.0-windows\Microsoft.Data.Sqlite.dll"

Write-Host "`n🔍 Querying tree_view in projections.db..." -ForegroundColor Cyan
Write-Host "Database: $dbPath" -ForegroundColor Gray
Write-Host "Search pattern: $SearchPattern`n" -ForegroundColor Gray

# Load the SQLite assembly from your build
Add-Type -Path $dllPath

$connectionString = "Data Source=$dbPath;Mode=ReadOnly"
$connection = New-Object Microsoft.Data.Sqlite.SqliteConnection($connectionString)
$connection.Open()

Write-Host "📊 ALL Categories in tree_view:" -ForegroundColor Yellow
Write-Host ("=" * 80) -ForegroundColor Gray

# Query all categories
$cmd = $connection.CreateCommand()
$cmd.CommandText = "SELECT id, path, name, node_type FROM tree_view WHERE node_type = 1 ORDER BY path COLLATE NOCASE"
$reader = $cmd.ExecuteReader()

$count = 0
while ($reader.Read()) {
    $id = $reader["id"]
    $path = $reader["path"]
    $name = $reader["name"]
    
    if ($path -like $SearchPattern) {
        Write-Host "✅ MATCH: " -NoNewline -ForegroundColor Green
    } else {
        Write-Host "   " -NoNewline
    }
    
    Write-Host "$name" -ForegroundColor White -NoNewline
    Write-Host " | Path: " -NoNewline -ForegroundColor Gray
    Write-Host "$path" -ForegroundColor Cyan -NoNewline
    Write-Host " | ID: $id" -ForegroundColor DarkGray
    
    $count++
}
$reader.Close()

Write-Host "`n📈 Total categories found: $count`n" -ForegroundColor Green

# Now specifically search for the pattern
Write-Host "`n🎯 Searching for '$SearchPattern':" -ForegroundColor Yellow
Write-Host ("=" * 80) -ForegroundColor Gray

$cmd2 = $connection.CreateCommand()
$cmd2.CommandText = "SELECT id, path, name FROM tree_view WHERE path LIKE @pattern COLLATE NOCASE"
$cmd2.Parameters.AddWithValue("@pattern", $SearchPattern) | Out-Null
$reader2 = $cmd2.ExecuteReader()

$matchCount = 0
while ($reader2.Read()) {
    $matchCount++
    Write-Host "Match $matchCount`: " -NoNewline -ForegroundColor Green
    Write-Host "$($reader2['name'])" -ForegroundColor White -NoNewline
    Write-Host " | Path: " -NoNewline -ForegroundColor Gray
    Write-Host "$($reader2['path'])" -ForegroundColor Cyan -NoNewline
    Write-Host " | ID: $($reader2['id'])" -ForegroundColor DarkGray
}
$reader2.Close()

if ($matchCount -eq 0) {
    Write-Host "❌ NO MATCHES FOUND for pattern '$SearchPattern'" -ForegroundColor Red
    Write-Host "`nThis explains why hierarchical lookup fails!" -ForegroundColor Yellow
} else {
    Write-Host "`n✅ Found $matchCount match(es)" -ForegroundColor Green
}

# Check what the lookup is actually trying
Write-Host "`n🔎 Exact paths being looked up by hierarchical code:" -ForegroundColor Yellow
Write-Host ("=" * 80) -ForegroundColor Gray

$lookups = @(
    "projects/25-117 - op iii/daily notes",
    "projects/25-117 - op iii",
    "projects"
)

foreach ($lookup in $lookups) {
    $cmd3 = $connection.CreateCommand()
    $cmd3.CommandText = "SELECT id, path, name FROM tree_view WHERE path = @path COLLATE NOCASE"
    $cmd3.Parameters.AddWithValue("@path", $lookup) | Out-Null
    $reader3 = $cmd3.ExecuteReader()
    
    if ($reader3.Read()) {
        Write-Host "✅ FOUND: " -NoNewline -ForegroundColor Green
        Write-Host "'$lookup' → " -NoNewline -ForegroundColor White
        Write-Host "$($reader3['name'])" -ForegroundColor Cyan -NoNewline
        Write-Host " (ID: $($reader3['id']))" -ForegroundColor DarkGray
    } else {
        Write-Host "❌ NOT FOUND: " -NoNewline -ForegroundColor Red
        Write-Host "'$lookup'" -ForegroundColor White
    }
    $reader3.Close()
}

$connection.Close()

Write-Host "`n✅ Query complete!`n" -ForegroundColor Green
