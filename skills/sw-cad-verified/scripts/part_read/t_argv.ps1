param([string]$p)
Write-Output ("LEN=" + $p.Length)
if (Test-Path -LiteralPath $p) { "EXISTS" } else { "MISSING" }
