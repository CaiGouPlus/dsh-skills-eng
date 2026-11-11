param([string]$Part = '%DSH_WORKSPACE%\cae\part_read\part.SLDPRT')
$ErrorActionPreference = 'Stop'
$pia = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
Add-Type -Path $pia
Add-Type -Path (Join-Path $PSScriptRoot 'Export.cs') -ReferencedAssemblies $pia
[Export]::Run($Part)
