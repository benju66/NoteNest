@echo off
REM Launch NoteNest with console window visible for debugging
echo ========================================
echo  NoteNest - Debug Launch (Console Visible)
echo ========================================
echo.
echo This window will show any unhandled exceptions
echo.
echo Press Ctrl+B to toggle Todo panel
echo ========================================
echo.

cd /d "%~dp0"
"NoteNest.UI\bin\Debug\net9.0-windows\NoteNest.UI.exe"

echo.
echo ========================================
echo  App closed. Press any key to exit...
echo ========================================
pause

