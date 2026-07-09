# Copyright (c) marcschier. Licensed under the MIT License.

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Harness = 'topics',

    [Parameter(Position = 1)]
    [int]$MaxTotalTime = 30
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $ScriptDir
$ToolsDir = Join-Path $ProjectDir '.tools'
$Corpus = Join-Path (Join-Path $ProjectDir 'corpus') $Harness
$Findings = Join-Path (Join-Path $ProjectDir 'findings') $Harness
New-Item -ItemType Directory -Force -Path $ToolsDir, $Corpus, $Findings | Out-Null

Write-Host '==> Restoring + building IoTHubby.FuzzTests (Release)...'
dotnet build (Join-Path $ProjectDir 'IoTHubby.FuzzTests.csproj') -c Release -nologo -v quiet | Out-Null
$OutDir = Join-Path $ProjectDir 'bin\Release\net10.0'
$TargetDll = Join-Path $OutDir 'IoTHubby.dll'

$SharpFuzz = Get-Command sharpfuzz -ErrorAction SilentlyContinue
if ($null -eq $SharpFuzz)
{
    Write-Host '==> Installing SharpFuzz.CommandLine global tool...'
    dotnet tool install --global SharpFuzz.CommandLine | Out-Null
    $env:PATH = "$env:PATH;$env:USERPROFILE\.dotnet\tools"
}

Write-Host '==> Instrumenting IoTHubby.dll...'
sharpfuzz $TargetDll | Out-Null

$LibFuzzer = Join-Path $ToolsDir 'libfuzzer-dotnet.exe'
if (-not (Test-Path $LibFuzzer))
{
    Write-Host '==> Downloading libfuzzer-dotnet driver (pinned)...'
    $Url = 'https://github.com/Metalnem/libfuzzer-dotnet/releases/download/v2024.08.10.0821/libfuzzer-dotnet-windows.exe'
    Invoke-WebRequest -Uri $Url -OutFile $LibFuzzer
}

Write-Host "==> Running $Harness for $MaxTotalTime seconds..."
$env:FUZZ_HARNESS = $Harness
& $LibFuzzer `
    "--target_path=$OutDir\IoTHubby.FuzzTests.exe" `
    "--target_arg=$Harness" `
    "-max_total_time=$MaxTotalTime" `
    "-artifact_prefix=$Findings\" `
    $Corpus
