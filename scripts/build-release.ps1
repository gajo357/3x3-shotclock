[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$artifactsDirectory = Join-Path $repositoryRoot 'artifacts'
$publishDirectory = Join-Path $artifactsDirectory 'publish\win-x64'
$portableZip = Join-Path $artifactsDirectory "3x3Centar.Scoreboard-$Version-win-x64.zip"

if (-not $artifactsDirectory.StartsWith(
        $repositoryRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The artifacts directory resolved outside the repository.'
}

if (Test-Path -LiteralPath $artifactsDirectory) {
    Remove-Item -LiteralPath $artifactsDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

dotnet restore (Join-Path $repositoryRoot 'ThreeByThree.Centar.Scoreboard.slnx') --locked-mode
if ($LASTEXITCODE -ne 0) {
    throw "Restore failed with exit code $LASTEXITCODE."
}

dotnet test --solution (Join-Path $repositoryRoot 'ThreeByThree.Centar.Scoreboard.slnx') `
    --configuration Release `
    --no-restore `
    --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE."
}

dotnet publish (Join-Path $repositoryRoot 'src\ThreeByThree.Centar.Scoreboard.Wpf\ThreeByThree.Centar.Scoreboard.Wpf.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    --output $publishDirectory `
    -p:Version=$Version
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

Compress-Archive -Path (Join-Path $publishDirectory '*') `
    -DestinationPath $portableZip `
    -CompressionLevel Optimal

dotnet build (Join-Path $repositoryRoot 'installer\ThreeByThree.Centar.Scoreboard.Installer\ThreeByThree.Centar.Scoreboard.Installer.wixproj') `
    --configuration Release `
    -p:Version=$Version `
    --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    throw "Installer build failed with exit code $LASTEXITCODE."
}

Write-Host "Portable package: $portableZip"
Write-Host "MSI package:      $(Join-Path $artifactsDirectory 'installer\3x3Centar.Scoreboard.Setup.msi')"
