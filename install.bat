@echo off
REM ── Podcast Shorts Factory — one-click installer (Windows) ──────────────
REM Double-click me. I check Python + ffmpeg, build the environment, and
REM launch the setup wizard.
setlocal
cd /d "%~dp0"

echo.
echo  ============================================
echo   PODCAST SHORTS FACTORY - Installer
echo  ============================================
echo.

where python >nul 2>nul
if errorlevel 1 (
    echo [!] Python not found. Install Python 3.11+ first:
    echo     winget install -e --id Python.Python.3.11
    echo     ^(or https://www.python.org/downloads/ - tick "Add to PATH"^)
    pause & exit /b 1
)
for /f "tokens=2 delims= " %%v in ('python --version') do echo [ok] Python %%v

where ffmpeg >nul 2>nul
if errorlevel 1 (
    echo [!] ffmpeg not found on PATH. Videos can't render without it.
    echo     Install now with:  winget install -e --id Gyan.FFmpeg
    echo     ^(you can finish this installer and add ffmpeg after^)
) else (
    echo [ok] ffmpeg found
)

if not exist .venv (
    echo [..] Creating virtual environment...
    python -m venv .venv || (echo [!] venv creation failed & pause & exit /b 1)
)
echo [..] Installing dependencies (2-5 minutes, one time only)...
.venv\Scripts\python.exe -m pip install --disable-pip-version-check -q --upgrade pip
.venv\Scripts\python.exe -m pip install --disable-pip-version-check -q -r requirements.txt || (
    echo [!] dependency install failed - check your internet connection & pause & exit /b 1
)
echo [ok] Dependencies installed.
echo.
echo  Launching the setup wizard...
echo.
.venv\Scripts\python.exe run.py setup
echo.
echo  ============================================
echo   Done! From now on, run commands with:
echo     .\factory auto "https://youtube.com/watch?v=..."
echo   (or see QUICKSTART.md)
echo  ============================================
pause
