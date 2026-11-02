@echo off & powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0MoonSweepersMods.ps1" %* & if errorlevel 1 (pause) else if "%~1"=="" pause
