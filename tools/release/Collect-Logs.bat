@echo off
rem Collects the CMS21 Together logs into one zip on your Desktop for a bug report.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Collect-Logs.ps1" %*
pause
