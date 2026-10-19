@echo off
rem Instala GoPilot: pide permiso de administrador y ejecuta install.ps1 (sin cambiar la directiva de scripts de Windows).
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0install.ps1\"'"
