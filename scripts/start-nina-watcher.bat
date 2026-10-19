@echo off
start "NINA watcher" /min powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0nina-watcher.ps1"
