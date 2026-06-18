[CmdletBinding()]
param(
    [string]$OutputPath = "",
    [string]$UpdateOutputPath = "",
    [string]$PackageOutputPath = "",
    [string]$RuntimeChannel = "10.0",
    [string]$RuntimeCachePath = ""
)

$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptRoot)) {
    $scriptRoot = Join-Path (Get-Location) "scripts"
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot ".."))

if ([string]::IsNullOrEmpty($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "artifacts\publish\win-x64"
}
if ([string]::IsNullOrEmpty($UpdateOutputPath)) {
    $UpdateOutputPath = Join-Path $repoRoot "artifacts\publish\win-x64-update"
}
if ([string]::IsNullOrEmpty($PackageOutputPath)) {
    $PackageOutputPath = Join-Path $repoRoot "artifacts\publish\packages"
}
if ([string]::IsNullOrEmpty($RuntimeCachePath)) {
    $RuntimeCachePath = Join-Path $repoRoot "runtime\dotnet-win-x64"
}

$projectPath = Join-Path $repoRoot "src\FH6Tools\FH6Tools.vbproj"
$outputPath = [System.IO.Path]::GetFullPath($OutputPath)
$updateOutputPath = [System.IO.Path]::GetFullPath($UpdateOutputPath)
$packageOutputPath = [System.IO.Path]::GetFullPath($PackageOutputPath)
$dotnetRoot = Join-Path $outputPath "dotnet"
$runtimeCachePath = [System.IO.Path]::GetFullPath($RuntimeCachePath)
$cacheRoot = Join-Path $repoRoot "artifacts\cache"
$installScript = Join-Path $cacheRoot "dotnet-install.ps1"
$project = [xml](Get-Content -LiteralPath $projectPath -Raw)
$version = [string]($project.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "FH6Tools project version could not be determined."
}
$fullPackagePath = Join-Path $packageOutputPath "FH6Tools-$version-win-x64-with-runtime.zip"
$updatePackagePath = Join-Path $packageOutputPath "FH6Tools-$version-win-x64-update.zip"

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
foreach ($path in @($outputPath, $updateOutputPath, $packageOutputPath)) {
    if (-not $path.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Publish output paths must be inside the FH6Tools repository so they can be cleaned safely."
    }
}
if ($outputPath.Equals($updateOutputPath, [System.StringComparison]::OrdinalIgnoreCase) -or
    $outputPath.Equals($packageOutputPath, [System.StringComparison]::OrdinalIgnoreCase) -or
    $updateOutputPath.Equals($packageOutputPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath, UpdateOutputPath, and PackageOutputPath must be different directories."
}
foreach ($path in @($outputPath, $updateOutputPath, $packageOutputPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
New-Item -ItemType Directory -Force -Path $outputPath, $updateOutputPath, $packageOutputPath, $cacheRoot | Out-Null

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $updateOutputPath
if ($LASTEXITCODE -ne 0) {
    throw "FH6Tools publish failed with exit code $LASTEXITCODE."
}

Get-ChildItem -LiteralPath $updateOutputPath -Force | Copy-Item -Destination $outputPath -Recurse -Force

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

if (Test-Path -LiteralPath (Join-Path $updateOutputPath "dotnet")) {
    throw "Update package output unexpectedly contains the shared runtime."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $outputPath,
    $fullPackagePath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $updateOutputPath,
    $updatePackagePath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)

Write-Host "Published full FH6Tools package to $fullPackagePath"
Write-Host "Published update-only FH6Tools package to $updatePackagePath"
