# Generic runner for scheduled tasks. Runs `python run.py <Cmd>` with logging.
# Resolves the project root relative to this script, so moving the folder is fine.
param([Parameter(Mandatory = $true)][string]$Cmd)
$ErrorActionPreference = "Stop"
$proj = Split-Path -Parent $PSScriptRoot      # tools\.. = project root
Set-Location $proj

# Ensure ffmpeg/yt-dlp resolve even in a bare scheduled-task session
$env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
            [System.Environment]::GetEnvironmentVariable("Path", "User")

New-Item -ItemType Directory -Force "$proj\logs" | Out-Null
$tag = ($Cmd -replace '[^a-zA-Z0-9]', '_')
$log = "$proj\logs\${tag}_$(Get-Date -Format 'yyyy-MM-dd_HHmmss').log"

& "$proj\.venv\Scripts\python.exe" run.py $Cmd *>> $log
"Done ($Cmd). Log: $log"
