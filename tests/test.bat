@echo off
rem Headless self-test: compiles only the game-independent sources with the tests
rem (text rules, text units, row grouping) and runs them. No game start, no .NET SDK.
rem If tests\corpus\rows-*.txt exist (dumped by the dev build, copied there with
rem tests\accept.bat), row grouping is also replayed on them and compared.
rem Exit code = number of failed cases.
rem ASCII only, and no if-blocks: the game path contains "(x86)".
setlocal
set HERE=%~dp0
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe

"%CSC%" -nologo -target:exe -optimize+ -warn:4 -nowarn:1701,1702,0649 -define:HEADLESS ^
  -out:"%HERE%headless.exe" ^
  "%HERE%..\src\TextRules.cs" "%HERE%..\src\TextUnits.cs" ^
  "%HERE%..\src\LayoutModel.cs" "%HERE%..\src\LayoutRows.cs" "%HERE%..\src\Sections.cs" "%HERE%..\src\RowCorpus.cs" ^
  "%HERE%PureShim.cs" "%HERE%HeadlessTests.cs"
if errorlevel 1 exit /b 1

"%HERE%headless.exe" "%HERE%corpus"
set RC=%ERRORLEVEL%
del "%HERE%headless.exe" >nul 2>nul
exit /b %RC%

