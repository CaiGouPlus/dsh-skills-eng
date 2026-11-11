param(
    [Parameter(Mandatory = $true)][string]$Part,
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
$pia = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
if (-not (Test-Path -LiteralPath $pia)) { throw "PIA not found: $pia" }
if (-not (Test-Path -LiteralPath $Part)) { throw "part not found: $Part" }
if ([string]::IsNullOrEmpty($OutDir)) { $OutDir = Join-Path $PSScriptRoot 'out' }
if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$md5 = (Get-FileHash -LiteralPath $Part -Algorithm MD5).Hash.ToLower()
Write-Output ("[an] part   = " + $Part)
Write-Output ("[an] md5    = " + $md5)
Write-Output ("[an] outdir = " + $OutDir)

Add-Type -Path $pia
$refs = @($pia)
$src = @(
    (Join-Path (Split-Path $PSScriptRoot -Parent) 'SwKit.cs'),
    (Join-Path $PSScriptRoot 'PartAnalyze.cs')
)
Add-Type -Path $src -ReferencedAssemblies $refs

$log = [PartAnalyze]::Run($Part, $OutDir, $md5)
Write-Output $log
