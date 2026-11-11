param([string]$Part = '%DSH_WORKSPACE%\cae\part_read\seat.SLDPRT')
$ErrorActionPreference = 'Stop'
$pia = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
Add-Type -Path $pia
Add-Type -Path (Join-Path $PSScriptRoot 'TopoDump.cs') -ReferencedAssemblies $pia
$out = [TopoDump]::Run($Part)
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'seat.topo.txt'), $out, (New-Object System.Text.UTF8Encoding($true)))
Write-Output ("lines=" + ($out -split "`n").Count)
