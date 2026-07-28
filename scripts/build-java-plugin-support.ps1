param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repoRoot "src\WosmJavaPluginBridge\src\main\java"
$mainClass = "org.wosm.javabridge.WosmJavaPluginBridge"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\java-support"
}

$classesDirectory = Join-Path $OutputDirectory "classes"
$jarPath = Join-Path $OutputDirectory "wosm-java-plugin-bridge.jar"
$appImagePath = Join-Path $OutputDirectory "wosm-java-plugin-bridge"
$sourcesFile = Join-Path $OutputDirectory "sources.txt"

New-Item -ItemType Directory -Force -Path $classesDirectory | Out-Null
$sources = Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter "*.java" |
    Sort-Object FullName |
    ForEach-Object { $_.FullName }
$sources | Set-Content -LiteralPath $sourcesFile -Encoding ASCII

javac -encoding UTF-8 -d $classesDirectory "@$sourcesFile"
if ($LASTEXITCODE -ne 0) {
    throw "javac failed with exit code $LASTEXITCODE."
}
jar --create --file $jarPath --main-class $mainClass -C $classesDirectory .
if ($LASTEXITCODE -ne 0) {
    throw "jar failed with exit code $LASTEXITCODE."
}

if (Test-Path -LiteralPath $appImagePath) {
    Remove-Item -LiteralPath $appImagePath -Recurse -Force
}

jpackage `
    --type app-image `
    --dest $OutputDirectory `
    --name "wosm-java-plugin-bridge" `
    --input $OutputDirectory `
    --main-jar "wosm-java-plugin-bridge.jar" `
    --main-class $mainClass `
    --java-options "-Dfile.encoding=UTF-8" `
    --win-console
if ($LASTEXITCODE -ne 0) {
    throw "jpackage failed with exit code $LASTEXITCODE."
}

Write-Host "Built Java/JOSM bridge: $appImagePath"
