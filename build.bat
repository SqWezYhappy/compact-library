@echo off
cd /d "%~dp0"
set "W=%windir%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%W%\csc.exe" set "W=%windir%\Microsoft.NET\Framework\v4.0.30319"
if not exist bin mkdir bin
"%W%\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /out:bin\PurpleLibrary.exe /win32icon:assets\icon.ico /r:"%W%\WPF\PresentationFramework.dll" /r:"%W%\WPF\PresentationCore.dll" /r:"%W%\WPF\WindowsBase.dll" /r:System.Xaml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Core.dll /r:Microsoft.CSharp.dll src\PurpleLibrary.cs
if errorlevel 1 goto fail
echo Done: bin\PurpleLibrary.exe
pause
exit /b 0
:fail
echo Build failed.
pause
exit /b 1
