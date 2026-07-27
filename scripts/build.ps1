param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "WPF-OpenStreetmap-Editor.slnx"

dotnet restore $solution
dotnet build $solution --configuration $Configuration --no-restore
