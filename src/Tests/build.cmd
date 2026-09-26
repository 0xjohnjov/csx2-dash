@echo off
rem Builds and runs the formatter tests against ..\CSX2Dash\CSX2Dash.dll (build that first).
setlocal
cd /d "%~dp0"
copy /Y ..\CSX2Dash\CSX2Dash.dll . >nul || exit /b 1
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /out:FormatTests.exe /r:CSX2Dash.dll FormatTests.cs || exit /b 1
"%~dp0FormatTests.exe"
