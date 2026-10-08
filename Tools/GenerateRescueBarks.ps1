# Generate local intelligible rescue calls. Requires Windows System.Speech and FFmpeg.
# Microsoft Zira Desktop is the source voice. "Low" files are slowed/pitched down
# versions for variety; they are not a separate actor or a male voice performance.
param(
    [string]$Ffmpeg = 'ffmpeg'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$voice = [System.Speech.Synthesis.SpeechSynthesizer]::new()
$voice.SelectVoice('Microsoft Zira Desktop')
$format = [System.Speech.AudioFormat.SpeechAudioFormatInfo]::new(
    22050,
    [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
    [System.Speech.AudioFormat.AudioChannel]::Mono)
$outDir = Join-Path $PSScriptRoot '../Assets/_Game/Resources/Audio/Rescue'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$lines = [ordered]@{
    Help = 'Help!'
    OverHere = 'Over here!'
    CantSwim = "I can't swim!"
    MySkis = 'My skis!'
    ThrowRing = 'A ring! Throw me a ring!'
    HaveKeys = 'You can have the keys!'
    RescueMe = 'Rescue me first!'
    ImOverHere = "I'm over here!"
}
foreach ($name in $lines.Keys) {
    $high = Join-Path $outDir ($name + '_high.wav')
    $low = Join-Path $outDir ($name + '_low.wav')
    $voice.Rate = if ($name -in @('Help', 'OverHere')) { 1 } else { 0 }
    $voice.SetOutputToWaveFile($high, $format)
    $voice.Speak($lines[$name])
    $voice.SetOutputToNull()
    & $Ffmpeg -hide_banner -loglevel error -y -i $high -af 'asetrate=19404,aresample=22050' -ac 1 -c:a pcm_s16le $low
    if ($LASTEXITCODE -ne 0) { throw "FFmpeg failed for $name" }
}
$voice.Dispose()
Write-Host "Generated $($lines.Count * 2) rescue calls in $outDir"
