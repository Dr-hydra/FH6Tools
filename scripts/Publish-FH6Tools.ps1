[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\artifacts\publish\win-x64"),
    [string]$RuntimeChannel = "10.0",
    [string]$RuntimeCachePath = (Join-Path $PSScriptRoot "..\runtime\dotnet-win-x64")
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repoRoot "src\FH6Tools\FH6Tools.vbproj"
$outputPath = [System.IO.Path]::GetFullPath($OutputPath)
$dotnetRoot = Join-Path $outputPath "dotnet"
$runtimeCachePath = [System.IO.Path]::GetFullPath($RuntimeCachePath)
$cacheRoot = Join-Path $repoRoot "artifacts\cache"
$installScript = Join-Path $cacheRoot "dotnet-install.ps1"

function Test-SharedRuntime([string]$Path) {
    $requiredPaths = @(
        (Join-Path $Path "dotnet.exe"),
        (Join-Path $Path "shared\Microsoft.NETCore.App"),
        (Join-Path $Path "shared\Microsoft.WindowsDesktop.App"),
        (Join-Path $Path "shared\Microsoft.AspNetCore.App")
    )
    foreach ($requiredPath in $requiredPaths) {
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            return $false
        }
    }
    return $true
}

$repoPrefix = $repoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $outputPath.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must be inside the FH6Tools repository so it can be cleaned safely."
}
if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $outputPath, $cacheRoot | Out-Null

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $outputPath
if ($LASTEXITCODE -ne 0) {
    throw "FH6Tools publish failed with exit code $LASTEXITCODE."
}

if (Test-SharedRuntime $runtimeCachePath) {
    New-Item -ItemType Directory -Force -Path $dotnetRoot | Out-Null
    Copy-Item -Path (Join-Path $runtimeCachePath "*") -Destination $dotnetRoot -Recurse -Force
    Write-Host "Copied shared .NET runtime from $runtimeCachePath"
} else {
    New-Item -ItemType Directory -Force -Path $dotnetRoot | Out-Null
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

    if (Test-SharedRuntime $dotnetRoot) {
        if (Test-Path -LiteralPath $runtimeCachePath) {
            Remove-Item -LiteralPath $runtimeCachePath -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $runtimeCachePath | Out-Null
        Copy-Item -Path (Join-Path $dotnetRoot "*") -Destination $runtimeCachePath -Recurse -Force
        Write-Host "Cached shared .NET runtime to $runtimeCachePath"
    }
}

if (-not (Test-SharedRuntime $dotnetRoot)) {
    throw "Shared runtime publish is incomplete. Missing files under $dotnetRoot"
}

Write-Host "Published FH6Tools with shared .NET runtime to $outputPath"
