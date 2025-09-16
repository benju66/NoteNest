# Simple script to monitor NoteNest memory usage
# Run this alongside the app to monitor memory improvements

Write-Host "=== NoteNest Memory Usage Monitor ===" -ForegroundColor Green
Write-Host "Instructions:" -ForegroundColor Yellow
Write-Host "1. Run this script"
Write-Host "2. Start NoteNest in another window"
Write-Host "3. Create multiple tabs (try 10-20)"
Write-Host "4. Watch memory usage patterns"
Write-Host "5. Close tabs and verify memory cleanup"
Write-Host ""

$processName = "NoteNest.UI"
$previousMemoryMB = 0
$startTime = Get-Date

while ($true) {
    $process = Get-Process -Name $processName -ErrorAction SilentlyContinue
    
    if ($process) {
        $currentMemoryMB = [math]::Round($process.WorkingSet64 / 1MB, 1)
        $deltaMemoryMB = $currentMemoryMB - $previousMemoryMB
        $runTimeMinutes = [math]::Round(((Get-Date) - $startTime).TotalMinutes, 1)
        
        $statusColor = "White"
        $status = "NORMAL"
        
        if ($currentMemoryMB -gt 200) {
            $statusColor = "Red"
            $status = "HIGH"
        } elseif ($currentMemoryMB -gt 100) {
            $statusColor = "Yellow" 
            $status = "MODERATE"
        }
        
        $deltaString = if ($deltaMemoryMB -gt 0) { "+$deltaMemoryMB" } else { "$deltaMemoryMB" }
        
        Write-Host "[$runTimeMinutes min] Memory: $currentMemoryMB MB ($deltaString MB) - $status" -ForegroundColor $statusColor
        
        $previousMemoryMB = $currentMemoryMB
        
        # Log significant memory events
        if ($deltaMemoryMB -gt 20) {
            Write-Host "  🔴 ALERT: Large memory increase (+$deltaMemoryMB MB)" -ForegroundColor Red
        } elseif ($deltaMemoryMB -lt -20) {
            Write-Host "  ✅ GOOD: Large memory reduction ($deltaMemoryMB MB)" -ForegroundColor Green
        }
        
        # Memory usage guidance
        if ($currentMemoryMB -gt 300) {
            Write-Host "  💡 Recommendation: Close some tabs to reduce memory usage" -ForegroundColor Cyan
        }
    } else {
        Write-Host "Waiting for NoteNest.UI process to start..." -ForegroundColor Gray
        $previousMemoryMB = 0
    }
    
    Start-Sleep -Seconds 5
}
