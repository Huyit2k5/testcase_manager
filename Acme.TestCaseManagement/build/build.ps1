<#
.SYNOPSIS
    Restores, builds, tests and packs Acme.TestCaseManagement.

.DESCRIPTION
    Runs against Acme.TestCaseManagement.sln. Every project under src/ is packed (test/ and host/ projects are not
    packable). Works in Windows PowerShell 5.1 and PowerShell 7.

.PARAMETER Configuration
    Debug or Release (default Release).

.PARAMETER VersionSuffix
    Appended to the version prefix from Directory.Build.props, e.g. 'preview.1' gives 1.0.0-preview.1.

.PARAMETER OutputDirectory
    Where .nupkg and .snupkg files are written (default artifacts/packages next to the solution).

.PARAMETER SkipTests
    Build and pack without running the test suites.

.PARAMETER SkipPack
    Build and test without creating packages.

.EXAMPLE
    ./build/build.ps1

.EXAMPLE
    ./build/build.ps1 -VersionSuffix preview.1 -SkipTests
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $VersionSuffix,

    [string] $OutputDirectory,

    [switch] $SkipTests,

    [switch] $SkipPack
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $root 'Acme.TestCaseManagement.sln'

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root 'artifacts/packages'
}

# Deliberately a basic function (no param block): in an advanced function PowerShell would try to bind
# dotnet's own switches such as -o to its common parameters (-OutVariable / -OutBuffer) and fail.
function Invoke-DotNet {
    Write-Host "> dotnet $($args -join ' ')" -ForegroundColor Cyan
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($args[0]) failed with exit code $LASTEXITCODE."
    }
}

$versionArguments = @()
if ($VersionSuffix) {
    $versionArguments = @("-p:VersionSuffix=$VersionSuffix")
}

Invoke-DotNet restore $solution
Invoke-DotNet build $solution --no-restore -c $Configuration @versionArguments

if (-not $SkipTests) {
    Invoke-DotNet test $solution --no-build -c $Configuration @versionArguments
}

if (-not $SkipPack) {
    if (Test-Path $OutputDirectory) {
        # Stale packages of an older version must not be mistaken for the output of this run.
        Get-ChildItem $OutputDirectory -File |
            Where-Object { $_.Extension -in '.nupkg', '.snupkg' } |
            Remove-Item -Force
    }

    # Project by project: packing the solution would also warn about the host and test projects that are not packable.
    $projects = @(Get-ChildItem (Join-Path $root 'src') -Filter '*.csproj' -Recurse)
    foreach ($project in $projects) {
        Invoke-DotNet pack $project.FullName --no-build -c $Configuration -o $OutputDirectory @versionArguments
    }

    $expected = $projects.Count
    $packages = @(Get-ChildItem $OutputDirectory -File | Where-Object { $_.Extension -eq '.nupkg' })
    if ($packages.Count -ne $expected) {
        throw "Expected $expected packages (one per project under src/) but found $($packages.Count) in $OutputDirectory."
    }

    Write-Host ''
    Write-Host "Packages written to ${OutputDirectory}:" -ForegroundColor Green
    $packages | ForEach-Object { Write-Host "  $($_.Name)" }
}
