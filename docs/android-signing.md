# Android release signing

PrintR's direct-download Android release identity was created on 20 September 2026. The APK uses an RSA-3072 release key, separate from the development/debug key. No private signing material is stored in Git or uploaded to GitHub.

Public certificate SHA-256:

```text
f3504c2b93e15250878d2406db07f56f9e7ba6487056cbc93bc9a710dc1b886f
```

This is an Android application signing identity, not a publicly trusted Windows Authenticode certificate. Android release certificates can be self-signed; Windows public distribution has different trust requirements. See [Android signing guidance](https://developer.android.com/studio/publish/app-signing) and [Windows signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options).

## Local storage

The signing directory is `%LOCALAPPDATA%\PrintR-Signing\Android`, outside this repository. Access is limited to the current Windows user and SYSTEM. It contains:

- `printr-release.p12`: the encrypted private key and certificate.
- `password.clixml`: a randomly generated password protected with Windows DPAPI for this user on this computer.
- `printr-release.cer`: the public certificate, safe to distribute.
- `signing-info.json`: public identity metadata.

Do not delete or regenerate this directory. Do not paste the password or private key into chat, source files, issue trackers, command-line arguments or build logs. Same-user malware or an administrator can still compromise local keys; Windows account protection is not a hardware security module.

## Build a signed APK

From the repository root in PowerShell 7, with JDK 17 and Android SDK build-tools 35.0.0 installed:

```powershell
.\scripts\AndroidRelease.ps1 -Action Build
```

The helper loads the protected credentials, runs release tests and lint, builds the APK, verifies its signature and expected certificate, and writes a fresh directory under `artifacts`. It includes the APK, its SHA-256 checksum and the public certificate. Existing releases are not overwritten. Signing environment variables are restored afterwards. Do not use Gradle debug logging or build scans when signing with private credentials.

`-Action Initialize` is only for the initial identity creation. It refuses to overwrite an existing signing directory. Do not create a replacement key for future releases. Keep the same identity and increment Android `versionCode` for updates.

## Required off-device backup

The DPAPI password file is not a portable backup. Losing the Windows profile or this computer can make it impossible to decrypt that password. Before distributing the signed APK, export a portable encrypted backup to secure off-device storage:

```powershell
.\scripts\AndroidRelease.ps1 -Action ExportBackup -BackupPath 'E:\PrintR-backup\printr-release.p12'
```

Replace the example drive and folder with your actual secure backup destination. The destination folder must exist and the file must not already exist. The helper prompts privately for a new password of at least 20 characters, confirms it, and exports the same signing identity with that password. Store the password separately in your password manager. Never pass a literal backup password in a command. A backup export/identity check was tested locally; an off-device backup has not yet been saved.

To recover on another machine, the backup `.p12` is a normal PKCS12 keystore. Use it with alias `printr-release` and the backup password for both the keystore and key password. Configure the four documented `PRINTR_*` signing variables securely, then run the normal release build. Verify the resulting APK's certificate against the fingerprint above. Recreate local DPAPI protection under the new account rather than copying the old password file. Never replace the certificate with a newly generated one during recovery.

## Installation and distribution

The signed release cannot update an installed debug APK because their signing certificates differ. Uninstalling the debug app removes its saved pairings and preferences, so note the connection details privately and pair again after installing the release. Do not uninstall the user's app automatically. An unsigned APK cannot be installed.

The signed APK was installed and launched on a fresh API 35 emulator, then reinstalled with the same release key successfully. A debug-signed replacement was correctly rejected. This is an emulator installation/signature check, not a physical-device upgrade or printer test of the new APK.

This key is for the current direct-download APK workflow. Google Play enrollment is a separate decision: Play App Signing distinguishes the app-signing key from an upload key. Plan that enrollment before relying on cross-store updates.

GitHub's manual release workflow remains unconfigured until signing secrets are deliberately supplied. Version 1.1.0 uses the locally signed APK for direct downloads and GitHub release assets. Only the APK and public certificate are distributed; the private key is not uploaded. No store submission has been made.

## Windows signing

The Windows EXE remains unsigned. A self-signed Windows certificate would not establish public publisher trust. As checked on 20 September 2026, Microsoft Artifact Signing supports New Zealand organizations, but individual developers must be in the United States or Canada. John is publishing as an individual, so this route is not currently eligible. See [Microsoft's eligibility requirements](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart).

A certificate authority supporting individual developers is the next route. [Certum's Standard Code Signing](https://www.certum.eu/en/code-signing-certificates/) lists individual applicants; identity verification, current pricing, country support and account approval must be confirmed before purchase. No account, purchase or identity submission has been made. An open-source signing program would also require a suitable project license and program approval; the repository currently has no license file. Do not choose a license just to qualify for signing without the owner's decision.
