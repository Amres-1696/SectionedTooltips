@echo off
rem Headless self-test: compiles only the game-independent sources with the tests
rem (TextRules.cs + TextUnits.cs) and runs them. No game start, no .NET SDK.
rem Exit code = number of failed cases.
rem ASCII only, and no if-blocks: the game path contains "(x86)".
setlocal
set HERE=%~dp0
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe

"%CSC%" -nologo -target:exe -optimize+ -warn:4 -nowarn:1701,1702 ^
  -out:"%HERE%headless.exe" ^
  "%HERE%..\src\TextRules.cs" "%HERE%..\src\TextUnits.cs" "%HERE%PureShim.cs" "%HERE%HeadlessTests.cs"
if errorlevel 1 exit /b 1

"%HERE%headless.exe"
set RC=%ERRORLEVEL%
del "%HERE%headless.exe" >nul 2>nul
exit /b %RC%

