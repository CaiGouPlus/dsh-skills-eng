param(
    [string]$Filter = '',
    [string]$OutFile = ''
)

$ErrorActionPreference = 'Stop'
$pia = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
$swc = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.swconst.dll'
if (-not (Test-Path -LiteralPath $pia)) { throw "PIA not found: $pia" }
if ([string]::IsNullOrEmpty($OutFile)) { $OutFile = Join-Path $PSScriptRoot 'out\open.docs.log' }

Add-Type -Path $pia
Add-Type -Path $swc

$refs = @($pia, $swc)
$src = @(
    (Join-Path (Split-Path $PSScriptRoot -Parent) 'SwKit.cs'),
    (Join-Path $PSScriptRoot 'ProbeDocs.cs')
)
Add-Type -Path $src -ReferencedAssemblies $refs

$log = [ProbeDocs]::Run($Filter, $OutFile)
Write-Output $log
