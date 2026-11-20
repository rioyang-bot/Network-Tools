@echo off
rem Builds NetworkTools.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /codepage:65001 /optimize+ /platform:anycpu ^
  /win32manifest:src\app.manifest /out:NetworkTools.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll ^
  src\*.cs
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo Built NetworkTools.exe
