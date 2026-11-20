@echo off
setlocal
echo.
echo This will restart Windows Explorer and reset ALL notification-area icon
echo customizations for the current user. Other tray icons will return to their
echo default positions/visibility. Save your work before continuing.
echo.
pause
echo Closing Explorer...
taskkill /f /im explorer.exe >nul 2>&1
timeout /t 2 /nobreak >nul
echo Resetting tray icon cache...
reg delete "HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\TrayNotify" /v IconStreams /f >nul 2>&1
reg delete "HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\TrayNotify" /v PastIconsStream /f >nul 2>&1
echo Restarting Explorer...
start "" explorer.exe
timeout /t 3 /nobreak >nul
echo.
echo Done. Start WirelessBatteryMoniter again, then look in the ^ overflow area.
pause
