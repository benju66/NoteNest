@echo off
REM Force clean rebuild and launch
echo ========================================
echo  NoteNest - Force Rebuild and Launch
echo ========================================
echo.

cd /d "%~dp0"

echo [1/4] Cleaning old build artifacts...
dotnet clean NoteNest.sln --configuration Debug
if errorlevel 1 (
    echo ERROR: Clean failed!
    pause
    exit /b 1
)
echo ✅ Clean complete
echo.

echo [2/4] Building NoteNest with latest code...
dotnet build NoteNest.sln --configuration Debug --no-incremental
if errorlevel 1 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)
echo ✅ Build complete
echo.

echo [3/4] Verifying output exists...
if not exist "NoteNest.UI\bin\Debug\net9.0-windows\NoteNest.UI.exe" (
    echo ERROR: NoteNest.UI.exe not found after build!
    pause
    exit /b 1
)
echo ✅ Output verified
echo.

echo [4/4] Launching NoteNest...
echo.
echo ========================================
echo  Starting NoteNest...
echo  Press Ctrl+B to toggle Todo panel
echo ========================================
echo.

start "" "NoteNest.UI\bin\Debug\net9.0-windows\NoteNest.UI.exe"

