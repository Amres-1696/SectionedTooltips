@echo off
rem Accept the row-grouping corpus the dev build just dumped (rows-ticket.txt / rows-classic.txt in the
rem mod data folder) as the baseline for tests\test.bat. Run this after checking the dump is what you want.
rem The corpus is game text, so tests\corpus is not in the repository.
setlocal
set HERE=%~dp0
set DATA=%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\ModData\amres_sectioned_tooltips
if not exist "%HERE%corpus" mkdir "%HERE%corpus"
copy /y "%DATA%\rows-*.txt" "%HERE%corpus\" >nul
if errorlevel 1 echo No rows-*.txt in "%DATA%": run the dev build with dumpall.flag first.
if errorlevel 1 exit /b 1
dir /b "%HERE%corpus\rows-*.txt"
