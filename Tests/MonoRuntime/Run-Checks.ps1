param([string]$GameDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..')))
$ErrorActionPreference = 'Stop'
$plugin = Join-Path $PSScriptRoot '../../ComfyImageGenerator.csproj'
$checks = Join-Path $PSScriptRoot 'Comfy.MonoTests.csproj'
$hostProject = Join-Path $PSScriptRoot 'Host/Comfy.MonoHost.csproj'
foreach ($project in @($plugin, $checks, $hostProject)) {
    dotnet build $project -c Release --nologo "-p:SilverpineGameDir=$GameDirectory"
    if ($LASTEXITCODE -ne 0) { throw 'Mono regression build failed.' }
}
dotnet (Join-Path $PSScriptRoot 'Host/bin/Release/net9.0/Comfy.MonoHost.dll') $GameDirectory (Join-Path $PSScriptRoot 'bin/Release/net48/Comfy.MonoTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Unity Mono regression failed.' }
