# Registers the "3 posts a day" automation as Windows scheduled tasks:
#   PSF-Produce  (morning)   -> make the day's clips into the queue
#   PSF-Post-1/2/3 (AM/PM/EVE) -> post one queued clip each
# Usage:   .\tools\schedule_posts.ps1
#          .\tools\schedule_posts.ps1 -ProduceAt 5:30AM -Post1At 9AM -Post2At 1PM -Post3At 6PM
# Remove:  "PSF-Produce","PSF-Post-1","PSF-Post-2","PSF-Post-3" | % { Unregister-ScheduledTask -TaskName $_ -Confirm:$false }
param(
    [string]$ProduceAt = "6:00AM",
    [string]$Post1At   = "9:00AM",
    [string]$Post2At   = "2:00PM",
    [string]$Post3At   = "7:00PM"
)
$runner = Join-Path $PSScriptRoot "run_cmd.ps1"

function Register-PSFTask($name, $cmd, $at) {
    $arg = "-NoProfile -ExecutionPolicy Bypass -File `"$runner`" -Cmd $cmd"
    $action  = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arg
    $trigger = New-ScheduledTaskTrigger -Daily -At $at
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable
    Register-ScheduledTask -TaskName $name -Action $action -Trigger $trigger `
        -Settings $settings -Force -Description "Podcast Shorts Factory ($cmd)" | Out-Null
    Write-Host "  $name  ->  $cmd  @ $at"
}

# Replace the old single-run task if it exists
Unregister-ScheduledTask -TaskName "PodcastShortsFactory" -Confirm:$false -ErrorAction SilentlyContinue

Register-PSFTask "PSF-Produce" "produce"   $ProduceAt
Register-PSFTask "PSF-Post-1"  "post-next" $Post1At
Register-PSFTask "PSF-Post-2"  "post-next" $Post2At
Register-PSFTask "PSF-Post-3"  "post-next" $Post3At

Write-Host ""
Write-Host "Done: makes the day's clips each morning, then posts 3x/day (AM/PM/EVE)."
Write-Host "Needs config.yaml -> scheduler.source_url set, and YouTube connected."
