@echo off
rem Builds the diagnostic tools. Build ..\..\src\CSX2Dash first (they use CSX2Dash.dll's wheel code).
rem Run them with SimHub and UGT Manager CLOSED - only one program may talk to the wheel at a time.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
copy /Y ..\..\src\CSX2Dash\CSX2Dash.dll . >nul || (echo Build src\CSX2Dash first. & exit /b 1)
"%CSC%" /nologo /out:LicenceTest.exe    /r:CSX2Dash.dll LicenceTest.cs || exit /b 1
"%CSC%" /nologo /out:PageStressTest.exe /r:CSX2Dash.dll /r:System.Xml.Linq.dll /r:System.Core.dll PageStressTest.cs || exit /b 1
"%CSC%" /nologo /out:LiveStressTest.exe /r:CSX2Dash.dll /r:System.Core.dll LiveStressTest.cs || exit /b 1
"%CSC%" /nologo /out:LcdOffTest.exe    /r:CSX2Dash.dll LcdOffTest.cs || exit /b 1
"%CSC%" /nologo /out:BiasPopupTest.exe /r:CSX2Dash.dll /r:System.Core.dll BiasPopupTest.cs || exit /b 1
"%CSC%" /nologo /out:probe.exe Probe.cs || exit /b 1
echo Built LicenceTest.exe, PageStressTest.exe, LiveStressTest.exe, LcdOffTest.exe, BiasPopupTest.exe, probe.exe
