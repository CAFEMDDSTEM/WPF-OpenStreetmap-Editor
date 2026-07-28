param(
    [string]$Configuration = "Release",
    [string]$PluginSourceRoot = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "WPF-OpenStreetmap-Editor.slnx"
if ([string]::IsNullOrWhiteSpace($PluginSourceRoot)) {
    $PluginSourceRoot = Join-Path $repoRoot "external-plugins"
}
$pluginOutputRoot = Join-Path $repoRoot "artifacts\plugins"

dotnet restore $solution

if (Get-Command jpackage -ErrorAction SilentlyContinue) {
    & (Join-Path $PSScriptRoot "build-java-plugin-support.ps1") -Configuration $Configuration
}

dotnet build $solution --configuration $Configuration --no-restore

if (!(Test-Path -LiteralPath $PluginSourceRoot)) {
    return
}

$pluginDirectories = Get-ChildItem -LiteralPath $PluginSourceRoot -Directory
foreach ($pluginDirectory in $pluginDirectories) {
    $solutionFile = Get-ChildItem -LiteralPath $pluginDirectory.FullName -File |
        Where-Object { $_.Extension -in ".slnx", ".sln" } |
        Sort-Object FullName |
        Select-Object -First 1
    $buildFiles = @()
    if ($solutionFile) {
        $buildFiles = @($solutionFile)
    } else {
        $buildFiles = Get-ChildItem -LiteralPath $pluginDirectory.FullName -Recurse -File |
            Where-Object { $_.Extension -eq ".csproj" } |
            Where-Object { $_.FullName -notmatch '\\(bin|obj|artifacts)\\' } |
            Sort-Object FullName
    }

    foreach ($buildFile in $buildFiles) {
        $pluginOutput = Join-Path $pluginOutputRoot $pluginDirectory.Name
        dotnet build $buildFile.FullName `
            --configuration $Configuration `
            -p:OutDir="$pluginOutput\"
    }
}
