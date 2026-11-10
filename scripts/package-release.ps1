[CmdletBinding()]
param(
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\STORY OF SEASONS Grand Bazaar',
    [string]$Version = '0.15.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\RoamingFishPreviews\RoamingFishPreviews.csproj'
$config = Join-Path $repoRoot 'config\com.vic.sosgb.roamingfishpreviews.cfg'
$releaseReadme = Join-Path $repoRoot 'release\README.txt'
$distRoot = Join-Path $repoRoot 'dist'
$stageRoot = Join-Path $distRoot "RoamingFishPreviews-v$Version"
$pluginRoot = Join-Path $stageRoot 'BepInEx\plugins\RoamingFishPreviews'
$configRoot = Join-Path $stageRoot 'BepInEx\config'
$archive = Join-Path $distRoot "RoamingFishPreviews-v$Version.zip"

dotnet build $project -c Release -p:GameDirectory="$GameDirectory"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

$dll = Join-Path $repoRoot 'src\RoamingFishPreviews\bin\Release\net6.0\RoamingFishPreviews.dll'
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
    throw "Built plugin was not found: $dll"
}

if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $archive) {
    Remove-Item -LiteralPath $archive -Force
}
New-Item -ItemType Directory -Path $pluginRoot -Force | Out-Null
New-Item -ItemType Directory -Path $configRoot -Force | Out-Null
Copy-Item -LiteralPath $dll -Destination $pluginRoot
Copy-Item -LiteralPath $config -Destination $configRoot
Copy-Item -LiteralPath $releaseReadme -Destination $stageRoot

Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $distRoot 'checksums.sha256') -Value "$hash  $([IO.Path]::GetFileName($archive))"
Write-Host "Created $archive"
Write-Host "SHA-256: $hash"
