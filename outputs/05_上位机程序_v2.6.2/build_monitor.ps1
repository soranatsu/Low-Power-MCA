$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'NuclearMcaMonitor.cs'
$output = Join-Path $PSScriptRoot 'NuclearMcaMonitor_FinalAdaptive_v2.6.2.exe'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$chart = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Windows.Forms.DataVisualization.dll'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Windows .NET Framework C# compiler was not found.'
}

& $compiler /nologo /target:winexe /optimize+ /platform:anycpu `
    /out:$output `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:$chart `
    $source

if ($LASTEXITCODE -ne 0) { throw "Monitor build failed with exit code $LASTEXITCODE" }
Write-Host "Built $output"
