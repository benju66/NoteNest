# PowerShell script to check todo plugin logs after testing

Write-Host "==================================" -ForegroundColor Cyan
Write-Host " Todo Plugin Log Checker" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

# Check if debug log exists
$logPath = "$env:LOCALAPPDATA\NoteNest\debug.log"
if (Test-Path $logPath) {
    Write-Host "✅ Found debug.log at: $logPath" -ForegroundColor Green
    Write-Host ""
    
    Write-Host "Checking for Todo Plugin logs..." -ForegroundColor Yellow
    Write-Host ""
    
    # Check for ViewModel creation
    Write-Host "=== ViewModel Initialization ===" -ForegroundColor Cyan
    Get-Content $logPath | Select-String "TodoListViewModel constructor|TodoListViewModel initialized" | Select-Object -Last 5
    Write-Host ""
    
    # Check for QuickAdd execution
    Write-Host "=== QuickAdd Execution ===" -ForegroundColor Cyan
    $quickAddLogs = Get-Content $logPath | Select-String "ExecuteQuickAdd|QuickAddText changed" | Select-Object -Last 10
    if ($quickAddLogs) {
        $quickAddLogs
    } else {
        Write-Host "❌ NO ExecuteQuickAdd logs found!" -ForegroundColor Red
        Write-Host "   This means the command never executed." -ForegroundColor Red
    }
    Write-Host ""
    
    # Check for database operations
    Write-Host "=== Database Operations ===" -ForegroundColor Cyan
    $dbLogs = Get-Content $logPath | Select-String "TodoStore.*AddAsync|Todo saved to database|Todo added to UI" | Select-Object -Last 10
    if ($dbLogs) {
        $dbLogs
    } else {
        Write-Host "❌ NO database operation logs found!" -ForegroundColor Red
    }
    Write-Host ""
    
    # Check for errors
    Write-Host "=== Errors/Exceptions ===" -ForegroundColor Cyan
    $errors = Get-Content $logPath | Select-String "ERROR|EXCEPTION|FATAL|❌" | Select-Object -Last 10
    if ($errors) {
        Write-Host "⚠️ FOUND ERRORS:" -ForegroundColor Red
        $errors
    } else {
        Write-Host "✅ No errors found" -ForegroundColor Green
    }
    Write-Host ""
    
} else {
    Write-Host "❌ Debug log not found at: $logPath" -ForegroundColor Red
    Write-Host "   App might not be writing logs, or hasn't run yet." -ForegroundColor Yellow
}

# Check database
Write-Host "=== Database Status ===" -ForegroundColor Cyan
$dbPath = "$env:LOCALAPPDATA\NoteNest\.plugins\NoteNest.TodoPlugin\todos.db"
if (Test-Path $dbPath) {
    $dbFile = Get-Item $dbPath
    Write-Host "✅ Database exists: $($dbFile.Length) bytes" -ForegroundColor Green
    Write-Host "   Last modified: $($dbFile.LastWriteTime)" -ForegroundColor Gray
    
    if ($dbFile.Length -gt 10000) {
        Write-Host "✅ Database has data (size > 10KB)" -ForegroundColor Green
    } else {
        Write-Host "⚠️ Database might be empty (size < 10KB)" -ForegroundColor Yellow
    }
} else {
    Write-Host "❌ Database doesn't exist!" -ForegroundColor Red
    Write-Host "   Plugin might not have initialized" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "NEXT STEPS:" -ForegroundColor Yellow
Write-Host "1. If you see 'ExecuteQuickAdd CALLED' → Command works, check later steps" -ForegroundColor White
Write-Host "2. If you DON'T see it → Command binding issue" -ForegroundColor White
Write-Host "3. Share this output with developer" -ForegroundColor White
Write-Host ""

