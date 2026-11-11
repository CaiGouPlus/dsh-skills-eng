$ErrorActionPreference = 'Stop'
$pia = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
Add-Type -Path $pia
$refs = @($pia)
$src = @(
    'D:\DSH\DSH\cae\SwKit.cs',
    'D:\DSH\DSH\cae\part_read\PartAnalyze.cs'
)
Add-Type -Path $src -ReferencedAssemblies $refs
Write-Output "COMPILE OK"
