@echo off
REM Shortcut so you never have to activate the venv:
REM   .\factory setup | .\factory auto "<url>" | .\factory daily | .\factory review ...
cd /d "%~dp0"
if not exist .venv\Scripts\python.exe (
    echo [!] Not installed yet - double-click install.bat first.
    exit /b 1
)
.venv\Scripts\python.exe run.py %*
