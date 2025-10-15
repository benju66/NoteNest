# Script to safely delete tree.db for clean migration application
# Run this AFTER closing NoteNest

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "DELETE TREE.DB - MIGRATION RESET SCRIPT" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$dbPath = "$env:LOCALAPPDATA\NoteNest\tree.db"
$shmPath = "$dbPath-shm"
$walPath = "$dbPath-wal"

Write-Host "⚠️  WARNING: This will delete tree.db and force a fresh rebuild" -ForegroundColor Yellow
Write-Host ""
Write-Host "Database location:" -ForegroundColor White
Write-Host "  $dbPath" -ForegroundColor Gray
Write-Host ""
Write-Host "This is SAFE because:" -ForegroundColor Green
Write-Host "  ✅ Tree database is rebuilt from your RTF files" -ForegroundColor Green
Write-Host "  ✅ No data loss (files are source of truth)" -ForegroundColor Green
Write-Host "  ✅ Migrations will apply cleanly" -ForegroundColor Green
Write-Host ""

# Check if NoteNest is running
$process = Get-Process -Name "NoteNest" -ErrorAction SilentlyContinue
if ($process) {
    Write-Host "❌ ERROR: NoteNest is currently running!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please close NoteNest first, then run this script again." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

Write-Host "✅ NoteNest is not running (good!)" -ForegroundColor Green
Write-Host ""

# Check if files exist
$mainExists = Test-Path $dbPath
$shmExists = Test-Path $shmPath
$walExists = Test-Path $walPath

if (-not $mainExists) {
    Write-Host "ℹ️  tree.db does not exist (already deleted or first run)" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 0
}

Write-Host "Found database files:" -ForegroundColor White
Write-Host "  tree.db:     $mainExists" -ForegroundColor Gray
Write-Host "  tree.db-shm: $shmExists" -ForegroundColor Gray
Write-Host "  tree.db-wal: $walExists" -ForegroundColor Gray
Write-Host ""

Write-Host "Press ENTER to delete, or Ctrl+C to cancel..." -ForegroundColor Yellow
Read-Host

# Delete files
try {
    if ($mainExists) {
        Remove-Item $dbPath -Force
        Write-Host "✅ Deleted tree.db" -ForegroundColor Green
    }
    
    if ($shmExists) {
        Remove-Item $shmPath -Force
        Write-Host "✅ Deleted tree.db-shm" -ForegroundColor Green
    }
    
    if ($walExists) {
        Remove-Item $walPath -Force
        Write-Host "✅ Deleted tree.db-wal" -ForegroundColor Green
    }
    
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "✅ DATABASE RESET COMPLETE" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor White
    Write-Host "  1. Launch NoteNest" -ForegroundColor Gray
    Write-Host "  2. App will rebuild tree.db from files (automatic)" -ForegroundColor Gray
    Write-Host "  3. Migrations will apply cleanly" -ForegroundColor Gray
    Write-Host "  4. Create a note-linked todo: [TODO: Test]" -ForegroundColor Gray
    Write-Host "  5. Todo should appear immediately! 🎉" -ForegroundColor Gray
    Write-Host ""
    
} catch {
    Write-Host ""
    Write-Host "❌ ERROR: Failed to delete files" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host "Possible causes:" -ForegroundColor Yellow
    Write-Host "  - File is locked (NoteNest still running?)" -ForegroundColor Gray
    Write-Host "  - Insufficient permissions" -ForegroundColor Gray
    Write-Host "  - File is read-only" -ForegroundColor Gray
    Write-Host ""
}

Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

