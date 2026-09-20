#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Initialize', 'Build', 'ExportBackup')]
    [string]$Action = 'Build',
    [string]$SigningDirectory = (Join-Path $env:LOCALAPPDATA 'PrintR-Signing\Android'),
    [string]$OutputDirectory,
    [string]$BackupPath,
    [Security.SecureString]$BackupPassword,
    [string]$JavaHome = $env:JAVA_HOME
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'This local signing helper uses Windows DPAPI. Use the documented signing variables on other platforms.' }
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$signingRoot = [IO.Path]::GetFullPath($SigningDirectory)
$repoPrefix = $repoRoot.TrimEnd('\') + '\'
if ($signingRoot.Equals($repoRoot, [StringComparison]::OrdinalIgnoreCase) -or $signingRoot.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Signing keys must be stored outside the repository.'
}
if (-not $JavaHome) { throw 'Set JAVA_HOME or pass -JavaHome to a JDK installation.' }
$keytool = Join-Path $JavaHome 'bin\keytool.exe'
if (-not (Test-Path -LiteralPath $keytool -PathType Leaf)) { throw 'keytool was not found under the selected JDK.' }
$keystore = Join-Path $signingRoot 'printr-release.p12'
$credentialFile = Join-Path $signingRoot 'password.clixml'
$certificateFile = Join-Path $signingRoot 'printr-release.cer'
$metadataFile = Join-Path $signingRoot 'signing-info.json'
$keyAlias = 'printr-release'
$variableNames = @('JAVA_HOME', 'PRINTR_KEYSTORE', 'PRINTR_STORE_PASSWORD', 'PRINTR_KEY_ALIAS', 'PRINTR_KEY_PASSWORD', 'PRINTR_BACKUP_PASSWORD')
$previous = @{}
foreach ($name in $variableNames) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }

function Assert-NativeSuccess([string]$Operation) {
    if ($LASTEXITCODE -ne 0) { throw "$Operation failed (exit $LASTEXITCODE)." }
}

function Get-CertificateFingerprint([string]$Path) {
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new([IO.File]::ReadAllBytes($Path))
    try { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($certificate.RawData)).ToLowerInvariant() }
    finally { $certificate.Dispose() }
}

function Set-PrivateDirectoryAcl([string]$Path) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetOwner($identity)
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sid in @($identity, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'))) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

try {
    $env:JAVA_HOME = $JavaHome
    if ($Action -eq 'Initialize') {
        if (Test-Path -LiteralPath $signingRoot) { throw 'Signing directory already exists. Refusing to replace a signing identity.' }
        New-Item -ItemType Directory -Path $signingRoot | Out-Null
        Set-PrivateDirectoryAcl $signingRoot
        $passwordBytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(48)
        try { $password = [Convert]::ToBase64String($passwordBytes) }
        finally { [Array]::Clear($passwordBytes, 0, $passwordBytes.Length) }
        $securePassword = ConvertTo-SecureString $password -AsPlainText -Force
        [Management.Automation.PSCredential]::new($keyAlias, $securePassword) | Export-Clixml -LiteralPath $credentialFile
        $env:PRINTR_STORE_PASSWORD = $password
        $env:PRINTR_KEY_PASSWORD = $password
        & $keytool -genkeypair -noprompt -keystore $keystore -storetype PKCS12 -alias $keyAlias -keyalg RSA -keysize 3072 -sigalg SHA256withRSA -validity 10950 -dname 'CN=PrintR, C=NZ' -storepass:env PRINTR_STORE_PASSWORD -keypass:env PRINTR_KEY_PASSWORD
        Assert-NativeSuccess 'Release key generation'
        & $keytool -exportcert -keystore $keystore -alias $keyAlias -storepass:env PRINTR_STORE_PASSWORD -file $certificateFile
        Assert-NativeSuccess 'Public certificate export'
        $fingerprint = Get-CertificateFingerprint $certificateFile
        [ordered]@{
            applicationId = 'com.printr.android'
            alias = $keyAlias
            storeType = 'PKCS12'
            certificateSha256 = $fingerprint
            createdUtc = [DateTime]::UtcNow.ToString('o')
        } | ConvertTo-Json | Set-Content -LiteralPath $metadataFile -Encoding utf8
        Write-Host "Created Android release identity in $signingRoot"
        Write-Host "Public certificate SHA-256: $fingerprint"
        Write-Host 'Export a portable backup with -Action ExportBackup before relying on this key for distribution.'
        return
    }

    foreach ($required in @($keystore, $credentialFile, $certificateFile, $metadataFile)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Signing state is missing: $required. No key was generated or replaced." }
    }
    $credential = Import-Clixml -LiteralPath $credentialFile
    if ($credential -isnot [Management.Automation.PSCredential]) { throw 'The signing password file is not a Windows-protected credential.' }
    $metadata = Get-Content -Raw -LiteralPath $metadataFile | ConvertFrom-Json
    $fingerprint = Get-CertificateFingerprint $certificateFile
    if ($metadata.certificateSha256 -ne $fingerprint -or $metadata.alias -ne $keyAlias -or $credential.UserName -ne $keyAlias) { throw 'Signing metadata and certificate do not match.' }
    $env:PRINTR_KEYSTORE = $keystore
    $env:PRINTR_STORE_PASSWORD = $credential.GetNetworkCredential().Password
    $env:PRINTR_KEY_PASSWORD = $env:PRINTR_STORE_PASSWORD
    $env:PRINTR_KEY_ALIAS = $keyAlias

    if ($Action -eq 'ExportBackup') {
        if (-not $BackupPath) { throw 'Supply -BackupPath pointing to a NEW .p12 file on your secure backup storage.' }
        $backupFullPath = [IO.Path]::GetFullPath($BackupPath)
        if ([IO.Path]::GetExtension($backupFullPath) -ne '.p12') { throw 'BackupPath must end in .p12.' }
        if ($backupFullPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Private backups must stay outside the repository.' }
        if (Test-Path -LiteralPath $backupFullPath) { throw 'Refusing to overwrite an existing backup.' }
        if (-not $BackupPassword) {
            $BackupPassword = Read-Host 'New backup password (save it in your password manager)' -AsSecureString
            $confirmation = Read-Host 'Confirm backup password' -AsSecureString
            $confirmationText = [Management.Automation.PSCredential]::new('backup', $confirmation).GetNetworkCredential().Password
            $backupText = [Management.Automation.PSCredential]::new('backup', $BackupPassword).GetNetworkCredential().Password
            if ($backupText -cne $confirmationText) { throw 'Backup passwords do not match.' }
            $confirmationText = $null
            $backupText = $null
        }
        if ($BackupPassword.Length -lt 20) { throw 'Use at least 20 characters for the portable backup password.' }
        $env:PRINTR_BACKUP_PASSWORD = [Management.Automation.PSCredential]::new('backup', $BackupPassword).GetNetworkCredential().Password
        & $keytool -importkeystore -noprompt -srckeystore $keystore -srcstoretype PKCS12 -srcalias $keyAlias -srcstorepass:env PRINTR_STORE_PASSWORD -srckeypass:env PRINTR_KEY_PASSWORD -destkeystore $backupFullPath -deststoretype PKCS12 -destalias $keyAlias -deststorepass:env PRINTR_BACKUP_PASSWORD -destkeypass:env PRINTR_BACKUP_PASSWORD
        Assert-NativeSuccess 'Portable backup export'
        & $keytool -list -keystore $backupFullPath -alias $keyAlias -storepass:env PRINTR_BACKUP_PASSWORD
        Assert-NativeSuccess 'Portable backup verification'
        Write-Host "Created password-protected backup: $backupFullPath"
        Write-Host 'Keep its password separately. This backup does not depend on Windows DPAPI.'
        return
    }

    $androidRoot = Join-Path $repoRoot 'printr-android'
    $sdkRoot = $env:ANDROID_HOME
    if (-not $sdkRoot) { $sdkRoot = Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
    $apksigner = Join-Path $sdkRoot 'build-tools\35.0.0\apksigner.bat'
    if (-not (Test-Path -LiteralPath $apksigner -PathType Leaf)) { throw 'Install Android build-tools 35.0.0 before building.' }
    if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot ('artifacts\signed-android-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
    if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory so previous releases remain intact.' }
    Push-Location $androidRoot
    try {
        & .\gradlew.bat testReleaseUnitTest lintRelease assembleRelease --no-daemon --no-configuration-cache
        Assert-NativeSuccess 'Android release build and tests'
    }
    finally { Pop-Location }
    $apk = Join-Path $androidRoot 'app\build\outputs\apk\release\app-release.apk'
    $verification = & $apksigner verify --verbose --print-certs $apk
    Assert-NativeSuccess 'APK signature verification'
    $verification | Write-Output
    if (-not ($verification -match "Signer #1 certificate SHA-256 digest: $fingerprint")) { throw 'The APK was not signed by the expected release identity.' }
    $outputMetadata = Get-Content -Raw (Join-Path $androidRoot 'app\build\outputs\apk\release\output-metadata.json') | ConvertFrom-Json
    $version = $outputMetadata.elements[0].versionName
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
    $destination = Join-Path $OutputDirectory "PrintR-$version-release.apk"
    Copy-Item -LiteralPath $apk -Destination $destination
    Copy-Item -LiteralPath $certificateFile -Destination (Join-Path $OutputDirectory 'PrintR-android-release.cer')
    $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $destination)" | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding utf8
    $fingerprint | Set-Content -LiteralPath (Join-Path $OutputDirectory 'certificate-sha256.txt') -Encoding utf8
    Write-Host "Verified signed release: $destination"
}
finally {
    foreach ($name in $variableNames) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
    $password = $null
    $credential = $null
    $securePassword = $null
    $BackupPassword = $null
    $confirmation = $null
    $confirmationText = $null
    $backupText = $null
}
