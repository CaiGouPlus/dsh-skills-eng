# 编译并运行 Starter.cs。用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File run_starter.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File run_starter.ps1 -SavePath "D:\out\demo.SLDPRT"
param(
    [string]$Template = '',
    [string]$SavePath = "$env:TEMP\sw_starter_demo.SLDPRT"
)

$ErrorActionPreference = 'Continue'
$pia = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
if (-not (Test-Path $pia)) { throw "找不到 PIA: $pia" }

# 两个 .cs 必须一起编译：Add-Type 按单文件编译时互相看不见
Add-Type -Path $pia
Add-Type -Path @(
    (Join-Path $PSScriptRoot 'SketchKit.cs'),
    (Join-Path $PSScriptRoot 'Starter.cs')
) -ReferencedAssemblies $pia

$log = [Starter]::Run($Template, $SavePath)
Write-Output $log
Write-Output ""
Write-Output "=== LOG FILE ==="
Write-Output ([System.IO.Path]::ChangeExtension($SavePath, '.log'))
