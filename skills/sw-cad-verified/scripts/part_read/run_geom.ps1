param(
    [Parameter(Mandatory = $true)][string]$Part,
    [string]$OutDir = ''
)
$ErrorActionPreference = 'Stop'
$pia = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
if (-not (Test-Path -LiteralPath $pia)) { throw "PIA not found: $pia" }
if ([string]::IsNullOrEmpty($OutDir)) { $OutDir = Join-Path $PSScriptRoot 'out' }
if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
Add-Type -Path $pia
$src = @(
    (Join-Path (Split-Path $PSScriptRoot -Parent) 'SwKit.cs'),
    (Join-Path $PSScriptRoot 'SlideGeom.cs')
)
Add-Type -Path $src -ReferencedAssemblies @($pia)
$log = [SlideGeom]::Run($Part, $OutDir)
Write-Output $log
