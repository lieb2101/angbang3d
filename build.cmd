@echo off
rem Build the engine. Bypasses the PowerShell execution policy for this one
rem script only, so no system-wide policy change is needed.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\build.ps1" %*
if errorlevel 1 pause
