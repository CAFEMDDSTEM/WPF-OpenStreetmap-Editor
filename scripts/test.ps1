param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "WPF-OpenStreetmap-Editor.slnx"
$resultsDirectory = Join-Path $repoRoot "TestResults"

dotnet test $solution `
    --configuration $Configuration `
    --logger "trx" `
    --results-directory $resultsDirectory
