@echo off
rem Compile MIDI Sound Controller et son installateur avec le compilateur C# fourni par Windows (.NET Framework 4.8).
rem Aucune installation necessaire.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll
if not exist bin mkdir bin
if not exist dist mkdir dist

rem 1. Icone de l'application (generee une seule fois)
if not exist src\app.ico (
  "%CSC%" /nologo /out:bin\makeicon.exe /r:System.Drawing.dll tools\MakeIcon.cs || goto :fail
  bin\makeicon.exe src\app.ico || goto :fail
)

rem 2. Application
"%CSC%" /nologo /unsafe /codepage:65001 /target:winexe /optimize+ /platform:anycpu ^
  /win32manifest:src\app.manifest /win32icon:src\app.ico ^
  /out:bin\MidiSoundController.exe %REFS% /recurse:src\*.cs || goto :fail
echo OK : bin\MidiSoundController.exe

rem 3. Installateur (l'application y est embarquee)
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu ^
  /win32manifest:setup\setup.manifest /win32icon:src\app.ico ^
  /resource:bin\MidiSoundController.exe,payload.exe ^
  /out:dist\MidiSoundController-Setup.exe %REFS% /r:Microsoft.CSharp.dll setup\Setup.cs src\Version.cs || goto :fail
echo OK : dist\MidiSoundController-Setup.exe
exit /b 0

:fail
echo.
echo *** Echec de la compilation ***
exit /b 1
