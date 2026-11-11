param([string]$Part = 'D:\DSH\DSH\cae\part_read\seat.SLDPRT')
$ErrorActionPreference = 'Stop'
$pia = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
Add-Type -Path $pia
Add-Type -Path (Join-Path $PSScriptRoot 'TesselDump.cs') -ReferencedAssemblies $pia
$out = [TesselDump]::Run($Part)
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'seat.tess.txt'), $out, (New-Object System.Text.UTF8Encoding($true)))
Write-Output ("lines=" + ($out -split "`n").Count)
