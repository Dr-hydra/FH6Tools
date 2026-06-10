[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\artifacts\publish\win-x64"),
    [string]$RuntimeChannel = "10.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repoRoot "src\FH6Tools\FH6Tools.vbproj"
$outputPath = [System.IO.Path]::GetFullPath($OutputPath)
$dotnetRoot = Join-Path $outputPath "dotnet"
$cacheRoot = Join-Path $repoRoot "artifacts\cache"
$installScript = Join-Path $cacheRoot "dotnet-install.ps1"

$repoPrefix = $repoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $outputPath.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must be inside the FH6Tools repository so it can be cleaned safely."
}
if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $outputPath, $dotnetRoot, $cacheRoot | Out-Null

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $outputPath
if ($LASTEXITCODE -ne 0) {
    throw "FH6Tools publish failed with exit code $LASTEXITCODE."
}

Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript

& $installScript `
    -Channel $RuntimeChannel `
    -Runtime windowsdesktop `
    -Architecture x64 `
    -InstallDir $dotnetRoot `
    -NoPath
if ($LASTEXITCODE -ne 0) {
    throw ".NET Desktop Runtime installation failed with exit code $LASTEXITCODE."
}

& $installScript `
    -Channel $RuntimeChannel `
    -Runtime aspnetcore `
    -Architecture x64 `
    -InstallDir $dotnetRoot `
    -NoPath
if ($LASTEXITCODE -ne 0) {
    throw "ASP.NET Core Runtime installation failed with exit code $LASTEXITCODE."
}

$requiredPaths = @(
    (Join-Path $dotnetRoot "dotnet.exe"),
    (Join-Path $dotnetRoot "shared\Microsoft.NETCore.App"),
    (Join-Path $dotnetRoot "shared\Microsoft.WindowsDesktop.App"),
    (Join-Path $dotnetRoot "shared\Microsoft.AspNetCore.App")
)
foreach ($requiredPath in $requiredPaths) {
    if (-not (Test-Path $requiredPath)) {
        throw "Shared runtime publish is incomplete. Missing: $requiredPath"
    }
}

Write-Host "Published FH6Tools with shared .NET runtime to $outputPath"
