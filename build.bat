@echo off
rem Builds SectionedTooltips.dll with the C# compiler that ships with Windows (.NET Framework 4.x, C# 5)
rem and installs it into ..\..\Mods\SectionedTooltips. No .NET SDK needed. The source sticks to C# 5.
setlocal
set HERE=%~dp0
set MANAGED=%HERE%..\..\Probably Stolen_Data\Managed
set OUT=%HERE%..\..\Mods\SectionedTooltips
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%HERE%bin" mkdir "%HERE%bin"
if not exist "%OUT%" mkdir "%OUT%"
if not exist "%OUT%\Localization" mkdir "%OUT%\Localization"

"%CSC%" -nologo -target:library -optimize+ -nostdlib+ -noconfig -warn:4 -nowarn:1701,1702 ^
  -out:"%HERE%bin\SectionedTooltips.dll" ^
  -r:"%MANAGED%\mscorlib.dll" -r:"%MANAGED%\netstandard.dll" -r:"%MANAGED%\System.dll" -r:"%MANAGED%\System.Core.dll" ^
  -r:"%MANAGED%\Assembly-CSharp.dll" -r:"%MANAGED%\0Harmony.dll" ^
  -r:"%MANAGED%\UnityEngine.dll" -r:"%MANAGED%\UnityEngine.CoreModule.dll" ^
  -r:"%MANAGED%\UnityEngine.UI.dll" -r:"%MANAGED%\UnityEngine.UIModule.dll" -r:"%MANAGED%\Unity.TextMeshPro.dll" ^
  -r:"%MANAGED%\UnityEngine.TextRenderingModule.dll" -r:"%MANAGED%\UnityEngine.TextCoreFontEngineModule.dll" ^
  "%HERE%src\*.cs"
if errorlevel 1 exit /b 1

copy /y "%HERE%bin\SectionedTooltips.dll" "%OUT%\" >nul
if errorlevel 1 (
  echo Built bin\SectionedTooltips.dll but could not install it: close the game and run this again.
  exit /b 1
)
copy /y "%HERE%manifest.xml" "%OUT%\" >nul
if exist "%HERE%preview.png" copy /y "%HERE%preview.png" "%OUT%\" >nul
copy /y "%HERE%Localization\*.csv" "%OUT%\Localization\" >nul
echo Installed to %OUT%
