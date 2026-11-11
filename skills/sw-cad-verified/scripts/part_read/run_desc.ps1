param([string]$List = 'D:\DSH\DSH\cae\part_read\parts.txt')
$ErrorActionPreference = 'Stop'
$pia = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
Add-Type -Path $pia
Add-Type -Path (Join-Path $PSScriptRoot 'Desc.cs') -ReferencedAssemblies $pia
$out = [Desc]::Run($List)
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'desc.txt'), $out, (New-Object System.Text.UTF8Encoding($true)))
Write-Output $out
