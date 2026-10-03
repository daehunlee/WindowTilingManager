@echo off
rem Double-click to build release\WindowTilingManager\WindowTilingManager.exe
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1"
pause
