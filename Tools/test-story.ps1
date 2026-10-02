# Plays the story by itself in the built game and says whether it got through (see Assets/_Game/Dev/StoryAutoplay.cs).
#
#   powershell -File Tools\test-story.ps1                  chapter 1 (beats 1.1 .. 1.10), headless, twice normal speed
#   powershell -File Tools\test-story.ps1 -Last 2.7        both chapters
#   powershell -File Tools\test-story.ps1 -Client          a second copy joins as a client and its log is checked too
#   powershell -File Tools\test-story.ps1 -Shots           also saves pictures to Builds\Win64\Screenshots\Autoplay
#
# Needs a player build (GameSceneBuilder.BuildPlayerBatch). Uses port 7790, never touches the story save.
# Exit code 0 = the story was played to the end of -Last with no errors in the log.
param(
    [string]$Last = "1.10",
    [double]$Speed = 2,
    [switch]$Client,
    [switch]$Shots,
    [int]$Minutes = 12
)

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "Builds\Win64\PleaseDontDrown.exe"
if (-not (Test-Path $exe)) { Write-Error "No player build at $exe"; exit 2 }
$logs = Join-Path $root "Logs"
New-Item -ItemType Directory -Force $logs | Out-Null
$hostLog = Join-Path $logs "story-test-host.log"
$clientLog = Join-Path $logs "story-test-client.log"
Remove-Item $hostLog, $clientLog -ErrorAction SilentlyContinue

$hostArgs = @("-batchmode", "-pdd-nosteam", "-pdd-port", "7790", "-pdd-host-offline", "-pdd-nosave",
    "-pdd-autoplay", $Last, "-pdd-npcwatch", "-pdd-timescale", "$Speed", "-pdd-quit-after", "$($Minutes * 60)", "-logFile", $hostLog)
if ($Shots) { $hostArgs += "-pdd-autoshots" } else { $hostArgs += "-nographics" }
$hostGame = Start-Process $exe -ArgumentList $hostArgs -PassThru

$clientGame = $null
if ($Client) {
    Start-Sleep -Seconds 8   # the host has to be up first
    $clientArgs = @("-batchmode", "-nographics", "-pdd-nosteam", "-pdd-port", "7790", "-pdd-join", "localhost", "-pdd-nosave",
        "-pdd-quit-after", "$($Minutes * 60)", "-pdd-exec", "`"wait 30; story; players`"", "-logFile", $clientLog)
    $clientGame = Start-Process $exe -ArgumentList $clientArgs -PassThru
}

$hostGame.WaitForExit()
if ($clientGame -and -not $clientGame.HasExited) { $clientGame.Kill() }

Select-String -Path $hostLog -Pattern "^\[Autoplay\] (beat|beats|warning|error|RESULT)", "^\[Story\] CHAPTER", "^\[NpcWatch\] PROBLEM" | ForEach-Object { $_.Line }
$passed = [bool](Select-String -Path $hostLog -Pattern "^\[Autoplay\] RESULT PASS" -Quiet)
if ($Client) {
    $clientErrors = @(Select-String -Path $clientLog -Pattern "Exception|NullReference").Count
    $joined = [bool](Select-String -Path $clientLog -Pattern "^\[Console\] beat " -Quiet)
    Write-Output "[Client] joined and saw the story: $joined; exceptions in its log: $clientErrors"
    if (-not $joined -or $clientErrors -gt 0) { $passed = $false }
}
if ($passed) { Write-Output "STORY TEST PASSED"; exit 0 }
Write-Output "STORY TEST FAILED (logs: $hostLog)"
exit 1
