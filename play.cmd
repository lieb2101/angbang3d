@echo off
rem Double-click to play. Bypasses the PowerShell execution policy for this
rem one script only, so no system-wide policy change is needed.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0play.ps1" %*
if errorlevel 1 pause
