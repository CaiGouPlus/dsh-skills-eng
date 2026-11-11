param([string]$Part = '%DSH_WORKSPACE%\cae\part_read\seat.SLDPRT')
$ErrorActionPreference = 'Stop'
$pia = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
Add-Type -Path $pia
Add-Type -Path (Join-Path $PSScriptRoot 'Diag3.cs') -ReferencedAssemblies $pia
[Diag3]::Run($Part)
