param([string]$DotNet = 'dotnet', [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64', [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $repoRoot "artifacts/$Runtime" }
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
foreach ($app in @('Collector')) {
    & $DotNet publish (Join-Path $repoRoot "src/NetInspector.$app") -c Release -r $Runtime --self-contained true -o $outputPath
    if ($LASTEXITCODE -ne 0) { throw "Falha na publicação: $app" }
    $files = @(Get-ChildItem -LiteralPath $outputPath -File)
    if ($files.Count -ne 1 -or $files[0].Extension -ne '.exe') { throw "A publicação de $app não produziu um único EXE" }
    Get-FileHash -LiteralPath $files[0].FullName -Algorithm SHA256
}
