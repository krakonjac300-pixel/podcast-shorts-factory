# Registers a Windows Scheduled Task that runs the pipeline once a day.
# Usage:   .\tools\schedule_daily.ps1            (defaults to 9:00 AM)
#          .\tools\schedule_daily.ps1 -At 7am
# Remove:  Unregister-ScheduledTask -TaskName "PodcastShortsFactory" -Confirm:$false
param([string]$At = "9:00AM")

$runner = Join-Path $PSScriptRoot "run_daily.ps1"
$action  = New-ScheduledTaskAction -Execute "powershell.exe" `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$runner`""
$trigger = New-ScheduledTaskTrigger -Daily -At $At
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable

Register-ScheduledTask -TaskName "PodcastShortsFactory" -Action $action `
    -Trigger $trigger -Settings $settings -Force `
    -Description "Daily podcast -> shorts pipeline (Podcast Shorts Factory)" | Out-Null

Write-Host "Registered 'PodcastShortsFactory' to run daily at $At."
Write-Host "First, set scheduler.source_url in config.yaml to your channel/playlist URL."
Write-Host "Test it now without waiting:  .\tools\run_daily.ps1"
