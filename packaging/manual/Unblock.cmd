@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%~dp0' -Recurse -File | Unblock-File"
echo.
echo Mark-of-the-Web removed from this folder. You can now run the EXE directly.
pause
