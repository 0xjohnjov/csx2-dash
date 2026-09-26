@echo off
rem Copies CSX2Dash.dll into SimHub. Close SimHub first; run as administrator (right-click > Run as administrator).
rem SimHub elsewhere?  set SIMHUB=D:\Games\SimHub   before running this.
rem To uninstall: delete CSX2Dash.dll from the SimHub folder and restart SimHub.
setlocal
if defined SIMHUB (set "SH=%SIMHUB%") else (set "SH=C:\Program Files (x86)\SimHub")
if not exist "%SH%\SimHubWPF.exe" (echo SimHub not found in "%SH%". Set SIMHUB to your SimHub folder. & pause & exit /b 1)
tasklist /fi "imagename eq SimHubWPF.exe" | find /i "SimHubWPF.exe" >nul && (echo Close SimHub first, then run this again. & pause & exit /b 1)
copy /Y "%~dp0CSX2Dash.dll" "%SH%\CSX2Dash.dll" || (echo Copy failed - run this as administrator. & pause & exit /b 1)
echo Installed to "%SH%\CSX2Dash.dll". Start SimHub.
