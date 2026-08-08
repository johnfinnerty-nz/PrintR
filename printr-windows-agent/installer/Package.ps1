[CmdletBinding()]
param(
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$installerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$agentDir = Split-Path -Parent $installerDir
$artifactDir = Join-Path $agentDir "artifacts"
$publishDir = Join-Path $artifactDir "publish"
$packageDir = Join-Path $artifactDir "PrintR-Agent-$Runtime"
$zipPath = Join-Path $artifactDir "PrintR-Agent-$Runtime.zip"

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
if (Test-Path -LiteralPath $packageDir) { Remove-Item -LiteralPath $packageDir -Recurse -Force }
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

$project = Join-Path $agentDir "src\PrintR.Agent\PrintR.Agent.csproj"
[xml]$projectXml = Get-Content -Raw $project
$version = @($projectXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir

New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
Copy-Item (Join-Path $publishDir "*") $packageDir -Recurse -Force
Set-Content -LiteralPath (Join-Path $packageDir "README.txt") -Value @"
PrintR Agent

Version: $version

Launch PrintR.Agent.exe. It runs in the Windows system tray and listens only on private/local interfaces.
Use the agent window or tray menu to pair Android, enable Start with Windows, configure LibreOffice, and test printing.
Allow the app through Windows Defender Firewall on Private networks when prompted.
"@
Set-Content -LiteralPath (Join-Path $packageDir "VERSION.txt") -Value $version
Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Created $zipPath"
