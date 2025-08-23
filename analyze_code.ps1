# Find large files
Write-Host "=== FILES OVER 500 LINES ===" -ForegroundColor Yellow
Get-ChildItem -Path . -Filter *.cs -Recurse | ForEach-Object {
    $lines = (Get-Content $_.FullName | Measure-Object -Line).Lines
    if ($lines -gt 500) {
        [PSCustomObject]@{
            File = $_.FullName.Replace((Get-Location).Path + "\", "")
            Lines = $lines
        }
    }
} | Sort-Object Lines -Descending | Format-Table

# Find large methods (basic regex)
Write-Host "`n=== POTENTIALLY LARGE METHODS ===" -ForegroundColor Yellow
Get-ChildItem -Path . -Filter *.cs -Recurse | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $methods = [regex]::Matches($content, '(public|private|protected|internal).*?\{')
    foreach ($method in $methods) {
        $startIndex = $method.Index
        $bracketCount = 1
        $endIndex = $startIndex + $method.Length
        while ($bracketCount -gt 0 -and $endIndex -lt $content.Length) {
            if ($content[$endIndex] -eq '{') { $bracketCount++ }
            if ($content[$endIndex] -eq '}') { $bracketCount-- }
            $endIndex++
        }
        $methodContent = $content.Substring($startIndex, $endIndex - $startIndex)
        $lines = ($methodContent -split "`n").Count
        if ($lines -gt 50) {
            Write-Host "$($_.Name): ~$lines lines - $($method.Value)" -ForegroundColor Red
        }
    }
}


