@echo off
rem Builds CSX2Recovery.exe with the C# compiler that ships with Windows (.NET Framework 4).
setlocal
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /platform:x64 /optimize+ /win32manifest:app.manifest /out:CSX2Recovery.exe CSX2Recovery.cs
