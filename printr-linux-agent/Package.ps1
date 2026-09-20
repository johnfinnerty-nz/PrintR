[CmdletBinding()]
param([ValidateSet('linux-x64', 'linux-arm64')][string]$Runtime = 'linux-x64')
$ErrorActionPreference = 'Stop'
$artifactDir = Join-Path $PSScriptRoot 'artifacts'
$packageDir = Join-Path $artifactDir "PrintR-Agent-$Runtime"
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
dotnet publish (Join-Path $PSScriptRoot 'PrintR.Agent.Linux.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $packageDir
if ($LASTEXITCODE -ne 0) { throw 'Linux publish failed. No archive was created.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md'), (Join-Path $PSScriptRoot 'install.sh'), (Join-Path $PSScriptRoot 'printr-agent.service') -Destination $packageDir -Force
$archive = Join-Path $artifactDir "PrintR-Agent-$Runtime.tar.gz"
tar -czf $archive -C $packageDir .
if ($LASTEXITCODE -ne 0) { throw 'Linux archive creation failed.' }
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$archive.sha256" -Value "$hash  $(Split-Path -Leaf $archive)" -Encoding utf8
Write-Output "Created $archive"
