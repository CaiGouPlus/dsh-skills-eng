param(
    [string]$Part = '%DSH_WORKSPACE%\cae\part_read\part.SLDPRT'
)

$ErrorActionPreference = 'Stop'
$pia = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
if (-not (Test-Path $pia)) { throw "PIA not found: $pia" }

Add-Type -Path $pia
Add-Type -Path (Join-Path $PSScriptRoot 'Reader.cs') -ReferencedAssemblies $pia

$log = [Reader]::Run($Part)
Write-Output $log
Write-Output ""
Write-Output "=== LOG: $([System.IO.Path]::ChangeExtension($Part, '.read.log')) ==="
