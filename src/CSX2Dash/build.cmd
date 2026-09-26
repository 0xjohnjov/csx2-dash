@echo off
rem Builds CSX2Dash.dll against the installed SimHub (C# compiler that ships with Windows / .NET Framework 4).
rem SimHub elsewhere?  set SIMHUB=D:\Games\SimHub   before running this.
setlocal
cd /d "%~dp0"
if defined SIMHUB (set "SH=%SIMHUB%") else (set "SH=C:\Program Files (x86)\SimHub")
if not exist "%SH%\SimHub.Plugins.dll" (echo SimHub not found in "%SH%". Set SIMHUB to your SimHub folder. & exit /b 1)
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set WPF=%FW%\WPF
"%FW%\csc.exe" /nologo /target:library /platform:anycpu /optimize+ /out:CSX2Dash.dll ^
  /r:"%SH%\SimHub.Plugins.dll" /r:"%SH%\GameReaderCommon.dll" /r:"%SH%\SimHub.Logging.dll" /r:"%SH%\WoteverCommon.dll" /r:"%SH%\log4net.dll" ^
  /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\WindowsBase.dll" /r:System.Xaml.dll ^
  /r:System.Xml.dll /r:System.Xml.Linq.dll /r:System.Core.dll /r:System.Drawing.dll ^
  *.cs
