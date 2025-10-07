# PowerShell script to capture debug output from NoteNest
# Run this before starting the detached window drop test

$outputFile = "detached_drop_debug_$(Get-Date -Format 'yyyyMMdd_HHmmss').log"

Write-Host "Starting debug output capture to: $outputFile" -ForegroundColor Green
Write-Host "Press Ctrl+C to stop capturing" -ForegroundColor Yellow
Write-Host ""

# Use DebugView-like functionality if available
if (Get-Command "Get-WinEvent" -ErrorAction SilentlyContinue) {
    Write-Host "Attempting to capture Windows debug output..." -ForegroundColor Cyan
    
    # Start a job to monitor debug output
    $job = Start-Job -ScriptBlock {
        # This would require additional setup for true debug output capture
        # For now, we'll just monitor the console
        while ($true) {
            Start-Sleep -Milliseconds 100
        }
    }
    
    Write-Host "Note: For best results, run NoteNest from Visual Studio with Debug Output window open" -ForegroundColor Magenta
} else {
    Write-Host "Alternative: Use DebugView++ from SysInternals for better debug capture" -ForegroundColor Magenta
}

# Simple file watcher for any log files NoteNest might create
$logPath = "$env:APPDATA\NoteNest\Logs"
if (Test-Path $logPath) {
    Write-Host "Monitoring NoteNest log directory: $logPath" -ForegroundColor Green
    
    Get-ChildItem $logPath -Filter "*.log" | ForEach-Object {
        Write-Host "Found log file: $($_.Name)" -ForegroundColor Cyan
    }
    
    # Monitor the latest log file
    $latestLog = Get-ChildItem $logPath -Filter "*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latestLog) {
        Write-Host "Tailing: $($latestLog.FullName)" -ForegroundColor Green
        Get-Content $latestLog.FullName -Wait | Where-Object { $_ -match "DIAGNOSTIC|TabDragHandler|detached|main window" } | Tee-Object -FilePath $outputFile
    }
} else {
    Write-Host "Log directory not found. Please ensure NoteNest is configured for file logging." -ForegroundColor Red
    Write-Host "Run NoteNest from Visual Studio and check the Debug Output window instead." -ForegroundColor Yellow
}
