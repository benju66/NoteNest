@echo off
echo ========================================
echo NoteNest - Clean Rebuild and Launch
echo ========================================
echo.

echo Step 1: Closing any running instances...
taskkill /F /IM NoteNest.UI.exe 2>nul
timeout /t 2 /nobreak >nul

echo Step 2: Cleaning solution...
dotnet clean NoteNest.sln --configuration Debug >nul 2>&1

echo Step 3: Building solution...
dotnet build NoteNest.sln --configuration Debug --no-incremental

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ========================================
    echo BUILD FAILED - Check errors above
    echo ========================================
    pause
    exit /b 1
)

echo.
echo ========================================
echo BUILD SUCCESS - Launching NoteNest...
echo ========================================
echo.
timeout /t 2 /nobreak >nul

start "" "NoteNest.UI\bin\Debug\net9.0-windows\NoteNest.UI.exe"

echo.
echo ========================================
echo TEST STEPS:
echo 1. Press Ctrl+B to open Todo panel
echo 2. Right-click any folder
echo 3. Click "Add to Todo Categories"
echo 4. You should see a YELLOW box and BLUE text
echo ========================================

