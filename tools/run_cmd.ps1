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
    # DO NOT let $ErrorActionPreference stay "Stop" across this call.
    # PowerShell 5.1 wraps every stderr line from a NATIVE program in an
    # ErrorRecord when its streams are redirected, and under "Stop" that is a
    # TERMINATING error: the wrapper dies the instant python, yt-dlp, ffmpeg or
    # whisper writes anything to stderr, killing the run mid-flight. Nothing
    # explains why afterwards, because the log only ever holds python's stdout.
    # This is what silently killed produce at 12:31 on 2026-07-22 (exit 1, no
    # traceback): yt-dlp hit a members-only video during source picking and
    # wrote one ERROR line. Reproduced deliberately before changing this.
    $ErrorActionPreference = "Continue"
    & "$proj\.venv\Scripts\python.exe" run.py $Cmd *>> $log
    $code = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($code -ne 0) {
        # record the death IN the log, so the next failure leaves evidence
        "=== run.py '$Cmd' EXITED $code at $(Get-Date -Format 'HH:mm:ss') ===" |
            Out-File -FilePath $log -Append -Encoding utf8
        "FAILED ($Cmd) exit $code. Log: $log"
        exit $code
    }
    "Done ($Cmd). Log: $log"
}
finally {
    # release the sleep block so the PC can idle-sleep normally again
    [void][Win32.Power]::SetThreadExecutionState($ES_CONTINUOUS)
}
