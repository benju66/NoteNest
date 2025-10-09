@echo off
echo ========================================
echo  NoteNest - Todo Plugin Diagnostic Test
echo ========================================
echo.
echo Starting NoteNest with console window...
echo Watch for log messages as you test!
echo.
echo Actions to perform:
echo 1. Press Ctrl+B to open panel
echo 2. Type "test" in textbox  
echo 3. Press Enter
echo.
echo Watch console for:
echo - "ExecuteQuickAdd CALLED"
echo - "Todo saved to database"
echo - "Todo added to UI"
echo.
echo.

cd /d "%~dp0"
start "" "bin\Debug\net9.0-windows\NoteNest.UI.exe"

echo.
echo App launched! Check the application window.
echo Console logs will appear in the app's debug output.
echo.
pause

