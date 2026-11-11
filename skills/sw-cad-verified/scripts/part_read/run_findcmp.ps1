param(
    [Parameter(Mandatory = $true)][string]$Asm,
    [string]$OutFile = ''
)

$ErrorActionPreference = 'Stop'
$pia = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
if (-not (Test-Path -LiteralPath $pia)) { throw "PIA not found: $pia" }
if (-not (Test-Path -LiteralPath $Asm)) { throw "asm not found: $Asm" }
if ([string]::IsNullOrEmpty($OutFile)) { $OutFile = Join-Path $PSScriptRoot 'out\asm.comps.log' }

$swc = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.swconst.dll'
if (-not (Test-Path -LiteralPath $swc)) { throw "swconst not found: $swc" }
Add-Type -Path $pia
Add-Type -Path $swc

$refs = @($pia, $swc)
$src = @(
    (Join-Path (Split-Path $PSScriptRoot -Parent) 'SwKit.cs'),
    (Join-Path $PSScriptRoot 'FindCmp.cs')
)
Add-Type -Path $src -ReferencedAssemblies $refs

$log = [FindCmp]::Run($Asm, $OutFile)
Write-Output $log
