# Clear Old Todo Events from Event Store
# This removes old TodoCreatedEvent entries that are causing deserialization errors

$ErrorActionPreference = "Stop"

# Find events.db location
$eventsDbPath = "$env:LOCALAPPDATA\NoteNest\events.db"

if (-not (Test-Path $eventsDbPath)) {
    Write-Host "❌ events.db not found at: $eventsDbPath" -ForegroundColor Red
    exit 1
}

Write-Host "📁 Found events.db at: $eventsDbPath" -ForegroundColor Green

# Load SQLite
Add-Type -Path "C:\NoteNest\packages\Microsoft.Data.Sqlite.Core.9.0.0\lib\net9.0\Microsoft.Data.Sqlite.dll"

try {
    $connectionString = "Data Source=$eventsDbPath"
    $connection = New-Object Microsoft.Data.Sqlite.SqliteConnection($connectionString)
    $connection.Open()
    
    Write-Host "📊 Connected to events.db" -ForegroundColor Green
    
    # Count todo events before deletion
    $countCmd = $connection.CreateCommand()
    $countCmd.CommandText = "SELECT COUNT(*) FROM events WHERE event_type LIKE 'Todo%'"
    $beforeCount = $countCmd.ExecuteScalar()
    
    Write-Host "📦 Found $beforeCount old todo events" -ForegroundColor Yellow
    
    if ($beforeCount -eq 0) {
        Write-Host "✅ No old todo events to delete" -ForegroundColor Green
        $connection.Close()
        exit 0
    }
    
    # Show event types
    $typesCmd = $connection.CreateCommand()
    $typesCmd.CommandText = "SELECT DISTINCT event_type FROM events WHERE event_type LIKE 'Todo%'"
    $reader = $typesCmd.ExecuteReader()
    
    Write-Host "`n🔍 Todo event types found:" -ForegroundColor Cyan
    while ($reader.Read()) {
        Write-Host "  - $($reader.GetString(0))" -ForegroundColor White
    }
    $reader.Close()
    
    # Ask for confirmation
    Write-Host "`n⚠️  This will DELETE $beforeCount todo events from the event store!" -ForegroundColor Yellow
    $confirm = Read-Host "Continue? (yes/no)"
    
    if ($confirm -ne "yes") {
        Write-Host "❌ Cancelled by user" -ForegroundColor Red
        $connection.Close()
        exit 0
    }
    
    # Delete todo events
    Write-Host "`n🗑️  Deleting old todo events..." -ForegroundColor Yellow
    $deleteCmd = $connection.CreateCommand()
    $deleteCmd.CommandText = "DELETE FROM events WHERE event_type LIKE 'Todo%'"
    $deletedCount = $deleteCmd.ExecuteNonQuery()
    
    Write-Host "✅ Deleted $deletedCount todo events" -ForegroundColor Green
    
    # Reset projection checkpoints to max stream position
    Write-Host "🔄 Resetting projection checkpoints..." -ForegroundColor Yellow
    
    $maxPosCmd = $connection.CreateCommand()
    $maxPosCmd.CommandText = "SELECT MAX(stream_position) FROM events"
    $maxPosition = $maxPosCmd.ExecuteScalar()
    
    if ($maxPosition -is [DBNull]) {
        $maxPosition = 0
    }
    
    Write-Host "📍 Max stream position: $maxPosition" -ForegroundColor Cyan
    
    # Update all projections to this position
    $updateCheckpointCmd = $connection.CreateCommand()
    $updateCheckpointCmd.CommandText = "UPDATE projection_metadata SET last_processed_position = @MaxPos"
    $updateCheckpointCmd.Parameters.AddWithValue("@MaxPos", $maxPosition) | Out-Null
    $updatedRows = $updateCheckpointCmd.ExecuteNonQuery()
    
    Write-Host "✅ Updated $updatedRows projection checkpoints to position $maxPosition" -ForegroundColor Green
    
    $connection.Close()
    
    Write-Host "`n✅ CLEANUP COMPLETE!" -ForegroundColor Green
    Write-Host "📝 Next steps:" -ForegroundColor Cyan
    Write-Host "  1. Restart NoteNest app" -ForegroundColor White
    Write-Host "  2. Create a note with [bracket task]" -ForegroundColor White
    Write-Host "  3. Save the note (Ctrl+S)" -ForegroundColor White
    Write-Host "  4. Todo should appear in TodoPlugin panel!" -ForegroundColor White
    
} catch {
    Write-Host "❌ Error: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    if ($connection) {
        $connection.Close()
    }
    exit 1
}

