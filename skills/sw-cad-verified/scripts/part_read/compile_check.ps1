$ErrorActionPreference = 'Stop'
$pia = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
Add-Type -Path $pia
$refs = @($pia)
$src = @(
    '%DSH_WORKSPACE%\cae\SwKit.cs',
    '%DSH_WORKSPACE%\cae\part_read\PartAnalyze.cs'
)
Add-Type -Path $src -ReferencedAssemblies $refs
Write-Output "COMPILE OK"
