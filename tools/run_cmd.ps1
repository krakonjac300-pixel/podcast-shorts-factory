# Generic runner for scheduled tasks. Runs `python run.py <Cmd>` with logging.
# Resolves the project root relative to this script, so moving the folder is fine.
param([Parameter(Mandatory = $true)][string]$Cmd)
$ErrorActionPreference = "Stop"
$proj = Split-Path -Parent $PSScriptRoot      # tools\.. = project root
Set-Location $proj

# Ensure ffmpeg/yt-dlp resolve even in a bare scheduled-task session
$env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
            [System.Environment]::GetEnvironmentVariable("Path", "User")

# KEEP THE PC AWAKE FOR THE DURATION OF THIS RUN, then release. The #1 failure
# was the machine sleeping mid-whisper (~5-10 min CPU) and killing produce
# (result=1). SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED |
# ES_AWAYMODE_REQUIRED) blocks system sleep while this process lives; clearing
# it (ES_CONTINUOUS only) lets normal idle-sleep resume the moment we finish.
# No admin required, no cost, applies to every scheduled agent automatically.
Add-Type -Namespace Win32 -Name Power -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll")]
public static extern uint SetThreadExecutionState(uint esFlags);
'@
$ES_CONTINUOUS = [uint32]"0x80000000"
$ES_SYSTEM_REQUIRED = [uint32]"0x00000001"
$ES_AWAYMODE_REQUIRED = [uint32]"0x00000040"
[void][Win32.Power]::SetThreadExecutionState(
    $ES_CONTINUOUS -bor $ES_SYSTEM_REQUIRED -bor $ES_AWAYMODE_REQUIRED)

New-Item -ItemType Directory -Force "$proj\logs" | Out-Null
$tag = ($Cmd -replace '[^a-zA-Z0-9]', '_')
$log = "$proj\logs\${tag}_$(Get-Date -Format 'yyyy-MM-dd_HHmmss').log"

try {
    & "$proj\.venv\Scripts\python.exe" run.py $Cmd *>> $log
    "Done ($Cmd). Log: $log"
}
finally {
    # release the sleep block so the PC can idle-sleep normally again
    [void][Win32.Power]::SetThreadExecutionState($ES_CONTINUOUS)
}
