[CmdletBinding()]
param(
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$installerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$agentDir = Split-Path -Parent $installerDir
$artifactDir = Join-Path $agentDir "artifacts"
$buildId = Get-Date -Format 'yyyyMMdd-HHmmss'
$publishDir = Join-Path $artifactDir "publish-$buildId"
$packageDir = Join-Path $artifactDir "PrintR-Agent-$Runtime-$buildId"
$zipPath = Join-Path $artifactDir "PrintR-Agent-$Runtime.zip"

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

$project = Join-Path $agentDir "src\PrintR.Agent\PrintR.Agent.csproj"
[xml]$projectXml = Get-Content -Raw $project
$version = @($projectXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed. No package was created.' }
if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'PrintR.Agent.exe'))) { throw 'Published executable is missing.' }

New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
Copy-Item -LiteralPath (Join-Path $publishDir 'PrintR.Agent.exe') -Destination $packageDir
Set-Content -LiteralPath (Join-Path $packageDir "README.txt") -Value @"
PrintR Agent

Version: $version

Launch PrintR.Agent.exe. It runs in the Windows system tray and listens only on private/local interfaces.
Use Settings to configure the computer name, port, tools, upload limit, timeout and startup.
Install SumatraPDF for PDF printing and LibreOffice for Office document conversion.
CSV files print as plain text. This package is not Authenticode-signed.
Allow the app through Windows Defender Firewall on Private networks when prompted.
"@
Set-Content -LiteralPath (Join-Path $packageDir "VERSION.txt") -Value $version
Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal -Force
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$zipPath.sha256" -Value "$hash  $(Split-Path -Leaf $zipPath)" -Encoding utf8

Write-Host "Created $zipPath"
