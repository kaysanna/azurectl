@echo off
setlocal
if not exist "%~dp0publish\AzureCTL.exe" call "%~dp0build.cmd"
if errorlevel 1 exit /b %errorlevel%
start "" "%~dp0publish\AzureCTL.exe" %*
