# Deploying PrintR 1.1

## Release artifacts

Windows: run `printr-windows-agent/installer/Package.ps1`. It checks publish success, packages only the self-contained executable and operator notes, and writes a SHA-256 checksum. Existing staging folders remain available for rollback.

Linux: run `pwsh printr-linux-agent/Package.ps1 -Runtime linux-x64`. The tarball includes the self-contained agent, a user-service installer and instructions. `linux-arm64` is a build option, but requires separate runtime validation. See [Linux setup](../printr-linux-agent/README.md).

Android: run `gradlew testDebugUnitTest assembleDebug lintDebug` in `printr-android`. Debug APKs are installable for testing. A production release requires a stable private signing key so future updates can install over existing copies. Set `PRINTR_KEYSTORE` (absolute path), `PRINTR_STORE_PASSWORD`, `PRINTR_KEY_ALIAS` and `PRINTR_KEY_PASSWORD`, then run `gradlew testReleaseUnitTest lintRelease assembleRelease`. Without those variables, `assembleRelease` produces an unsigned APK. Keep keys outside the repository and back them up securely.

The manual release-candidate workflow requires signing secrets and fails if they are absent. It uploads a signed APK artifact; it does not publish a GitHub release. The verify workflow builds and tests all three targets and uploads Windows, Linux and debug Android artifacts. Windows Authenticode signing is not configured because no certificate was supplied.

## Supported file types

| Files | Windows | Linux |
| --- | --- | --- |
| PDF | SumatraPDF | CUPS |
| PNG, JPG/JPEG, BMP | Native Windows printing | CUPS filters |
| TXT, CSV | Plain text | Plain text through CUPS filters |
| DOCX, XLSX, PPTX | LibreOffice, then SumatraPDF | LibreOffice, then CUPS |
| ODT, ODS, ODP, RTF | LibreOffice, then SumatraPDF | LibreOffice, then CUPS |

CSV prints as plain text, not a formatted spreadsheet. Legacy binary DOC/XLS/PPT, macro-enabled files, HEIC, WebP, TIFF, animated images, HTML and arbitrary archives are not advertised as supported. Font substitution and page layout can differ during Office conversion. Export to PDF first when exact layout is essential. Direct Android printing remains PDF-only.

## Configuration and operations

The Windows Settings tab controls computer name, HTTPS port, tool paths, upload cap (1 to 100 MB), processing timeout (30 to 600 seconds), startup, mock mode and spool retention. Name, port and LibreOffice path changes require restart. Linux exposes the same persistent settings file and a user service. Uploaded files are deleted after processing by default. Debug retention keeps document contents on disk until manually removed.

The service binds loopback and current private IPv4 interfaces using HTTPS. Discovery uses UDP 8788. Firewall changes remain explicit operator actions. A network address change requires restarting the agent. `--loopback` disables LAN listeners and discovery for local testing. `PRINTR_DATA_DIR` isolates settings, certificates, jobs and spool files.

PrintR accepts at most 16 waiting jobs and processes them in order. It limits concurrent uploads and request rate. The maximum file size is 100 MB, with a lower configurable cap. File extensions, MIME types, signatures and Office archive structure are checked. Office conversion uses an isolated LibreOffice profile with macro security enabled. This is not a general-purpose sandbox for untrusted documents; keep LibreOffice and the OS patched, and pair only trusted devices.

`completed` means accepted by the printer backend, not confirmed physical output. Timeouts, restarts and printer errors can leave an uncertain spooler outcome. Check the operating system's print queue before retrying to avoid duplicate pages. Recent jobs survive restarts; interrupted jobs are marked failed.

Native Windows TXT/CSV/image printing uses the Windows driver API, which can block inside a driver and cannot always be interrupted by the job timeout. Native page ranges are not implemented for those formats. Use PDF for page selection. A hung driver may require restarting the agent.

## Validation

```powershell
dotnet test printr-windows-agent/tests/PrintR.Agent.Tests/PrintR.Agent.Tests.csproj -c Release
dotnet test printr-linux-agent/tests/PrintR.Linux.Tests.csproj -c Release
python scripts/smoke_agent.py path/to/PrintR.Agent.exe
```

On Linux, pass the Linux binary to the same smoke script. It tests an isolated HTTPS mock service, authentication, pairing, rejected files, page ranges, a 31 MB upload, expanded formats, job polling and spool cleanup. No physical print jobs are sent. Archive fixtures in this smoke test validate routing and structure; Office conversion needs separate real-document testing.

Real conversion checks are provided by `scripts/create_conversion_fixtures.py` and `scripts/ConversionSmoke`. Generate DOCX/XLSX/PPTX/RTF fixtures using python-docx, openpyxl and python-pptx, export the corresponding ODT/ODS/ODP files with LibreOffice, then run `dotnet run --project scripts/ConversionSmoke -- input-directory output-directory`. The harness validates and converts all seven formats using the production converter.

Before public release, sign and verify the Android APK and Windows EXE, test installation and upgrade on clean machines, and print representative documents on physical printers. Test copies, duplex, orientation and paper size with each supported driver. Confirm behavior when CUPS, SumatraPDF or LibreOffice is missing. Preserve the Android signing identity, and keep pairing QR codes and tokens out of screenshots.

Implementation references: [CUPS printing options](https://www.cups.org/doc/options.html) and [LibreOffice command-line options](https://help.libreoffice.org/latest/en-US/text/shared/guide/start_parameters.html).
