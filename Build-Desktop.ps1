param([string]$DotNet = 'dotnet')
$ErrorActionPreference = 'Stop'
$packageRoot = Join-Path $PSScriptRoot 'Release/Alpha'
& $DotNet publish (Join-Path $PSScriptRoot 'MajdataEdit/MajdataEdit.csproj') -c Release -o (Join-Path $packageRoot 'App/MajdataEdit')
if ($LASTEXITCODE -ne 0) { throw 'MajdataEdit publish failed' }
& $DotNet publish (Join-Path $PSScriptRoot 'MajdataLauncher/MajdataLauncher.csproj') -c Release -o $packageRoot
if ($LASTEXITCODE -ne 0) { throw 'MajdataLauncher publish failed' }
$viewDirectory = Join-Path $packageRoot 'App/MajdataView'
New-Item -ItemType Directory -Force -Path $viewDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/PACKAGE.md') -Destination (Join-Path $packageRoot 'BUILD-NOTES.md') -Force
foreach ($guide in @('COLLABORATION.md', 'MULTI_INSTANCE.md', 'MULTI_CONTROL.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "docs/$guide") -Destination (Join-Path $packageRoot $guide) -Force
}
Write-Output "Desktop package: $packageRoot"
Write-Output "Build THIS Alpha Unity project to: $viewDirectory/MajdataView.exe"
