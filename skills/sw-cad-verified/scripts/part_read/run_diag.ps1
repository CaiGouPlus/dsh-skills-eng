param([string]$Part = 'D:\DSH\DSH\cae\part_read\part.SLDPRT')
$ErrorActionPreference = 'Stop'
$pia = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
Add-Type -Path $pia
Add-Type -Path (Join-Path $PSScriptRoot 'Diag.cs') -ReferencedAssemblies $pia
[Diag]::Run($Part)
