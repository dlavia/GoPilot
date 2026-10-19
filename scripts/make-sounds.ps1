# Prepara los sonidos de navegación del widget (src\GoSPanel.Widget\Assets\Sounds): Focus, Invoke, Show, Hide y GoBack.
# Preferencia: los de la app de Xbox (Microsoft.GamingApp\Assets\Sounds), para que suene igual que ella (mismo tono y
# volumen). Si no está instalada, los de Windows que usa ElementSoundPlayer (C:\Windows\Media\*_48000Hz.raw, PCM 16
# bits mono 48 kHz) con +12 dB, porque vienen con el pico a unos -22 dBFS.
# Son archivos de Microsoft: se generan al compilar y no se suben al repositorio (Assets\Sounds está en .gitignore).
param([double]$GainDb = 12)

$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot '..\src\GoSPanel.Widget\Assets\Sounds'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out | Out-Null

$xbox = Get-AppxPackage Microsoft.GamingApp | Select-Object -First 1
$xboxSounds = if ($xbox) { Join-Path $xbox.InstallLocation 'Assets\Sounds' }
$map = [ordered]@{ Focus = 'Focus'; Invoke = 'Invoke'; Show = 'OverlayIn'; Hide = 'OverlayOut'; GoBack = 'Back' }
if ($xboxSounds -and (Test-Path (Join-Path $xboxSounds 'Focus.wav'))) {
    foreach ($name in $map.Keys) { Copy-Item (Join-Path $xboxSounds "$($map[$name]).wav") (Join-Path $out "$name.wav") }
    Write-Host "Sonidos: los de la app de Xbox $($xbox.Version)"
    return
}

$gain = [Math]::Pow(10, $GainDb / 20)
$windows = @{ Focus = 'Focus0'; Invoke = 'Invoke'; Show = 'Show'; Hide = 'Hide'; GoBack = 'GoBack' }
foreach ($name in $map.Keys) {
    $raw = [IO.File]::ReadAllBytes("$env:windir\Media\$($windows[$name])_48000Hz.raw")
    $samples = $raw.Length / 2
    $data = New-Object byte[] ($samples * 2)
    for ($i = 0; $i -lt $samples; $i++) {
        $v = [int]([BitConverter]::ToInt16($raw, $i * 2) * $gain)
        $v = [Math]::Max(-32768, [Math]::Min(32767, $v))
        $b = [BitConverter]::GetBytes([int16]$v)
        $data[$i * 2] = $b[0]; $data[$i * 2 + 1] = $b[1]
    }
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $w.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([int](36 + $data.Length)); $w.Write([Text.Encoding]::ASCII.GetBytes('WAVE'))
    $w.Write([Text.Encoding]::ASCII.GetBytes('fmt ')); $w.Write([int]16); $w.Write([int16]1); $w.Write([int16]1)
    $w.Write([int]48000); $w.Write([int]96000); $w.Write([int16]2); $w.Write([int16]16)
    $w.Write([Text.Encoding]::ASCII.GetBytes('data')); $w.Write([int]$data.Length); $w.Write($data)
    [IO.File]::WriteAllBytes((Join-Path $out "$name.wav"), $ms.ToArray())
}
Write-Host "Sonidos: los de Windows (+$GainDb dB); la app de Xbox no está instalada"
