# Runs the unattended daily pipeline. Invoked by the scheduled task (or run by hand).
# Resolves the project root relative to this script, so moving the folder is fine.
$ErrorActionPreference = "Stop"
$proj = Split-Path -Parent $PSScriptRoot      # tools\.. = project root
Set-Location $proj

# Make sure ffmpeg/yt-dlp are on PATH even in a bare scheduled-task session
$env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
            [System.Environment]::GetEnvironmentVariable("Path", "User")

New-Item -ItemType Directory -Force "$proj\logs" | Out-Null
$log = "$proj\logs\daily_$(Get-Date -Format 'yyyy-MM-dd_HHmmss').log"

& "$proj\.venv\Scripts\python.exe" run.py daily *>> $log
"Done. Log: $log"
