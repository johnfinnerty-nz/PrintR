# PrintR 1.1.0 validation

Local validation completed on 20 September 2026. The Android release APK is signed with a dedicated release identity. The Windows executable remains unsigned. See the [1.1.0 release notes](releases/1.1.0.md) for downloads and distribution limitations.

## Results

| Check | Result |
| --- | --- |
| Windows agent unit tests | 61 passed, including five Settings layout regression cases |
| Linux/shared unit tests on the Windows .NET runtime | 31 passed |
| Android debug unit tests | 15 passed |
| Android release unit tests | 15 passed |
| Android debug and release lint | Passed, 0 errors and 11 warnings per variant |
| Windows self-contained HTTPS smoke test | Passed, 10 mock jobs |
| Linux x64 self-contained HTTPS smoke test in Ubuntu 22.04 WSL | Passed, 10 mock jobs |
| Real LibreOffice conversion on Windows | DOCX, XLSX, PPTX, ODT, ODS, ODP and RTF passed |
| Real LibreOffice conversion on Linux | The same seven formats passed |
| Android emulator to Windows agent | Pinned HTTPS pairing, printer enumeration, XLSX upload and completed mock job passed |
| Android test APK signature | APK v2 debug signature verified |
| Android release APK signature | APK v2 signature verified with the dedicated RSA-3072 release certificate |
| Signed Android installation on a fresh API 35 emulator | Clean install, launch and same-key reinstallation passed; replacing it with a debug-signed APK was correctly rejected |
| Android signing safety checks | Existing-key replacement refused; encrypted backup export preserves the same certificate; private directory permissions verified |
| Physical Canon MX720 series Printer WS | User reports successful DOCX, PDF, CSV and TXT printing, and confirms color, duplex, copies and page-range controls |
| Windows/Linux NuGet vulnerability check | No vulnerable packages reported by the configured NuGet feed |
| Windows visual checks | Overview, Settings and Recent jobs captured; action and Browse button borders checked after content-sized row correction |

The HTTPS smoke test includes authentication failures, pairing, rejected file content, invalid page ranges, a 31 MB upload, expanded formats, job polling and spool cleanup. Android pairing was retained when updating with the same debug signing key. Agent-run checks sent no physical print jobs; the subsequent physical-printer results above were reported by the user. A new release signing identity cannot update the debug app in place.

The user-confirmed options were not recorded as a format-by-option matrix. In particular, native Windows TXT/CSV/image page-range handling is not implemented in the code; the user report does not change that limitation. Additional printer models and file formats still require their own checks.

## Installed for this work

The Windows build uses .NET 8, JDK 17 and Android API 35 tools. The Android emulator and API 35 image were added for UI checks. SumatraPDF 3.6.1 and LibreOffice 26.8.0.3 were installed on Windows. Ubuntu 22.04 WSL has CUPS client tools and LibreOffice Writer, Calc and Impress for Linux validation.

## Remaining distribution limitations and follow-up

- Save an off-device, password-protected backup of the new Android release key before distribution. Follow [signing and recovery instructions](android-signing.md). The older test APK remains debug-signed and the old unsigned artifact is retained for reference.
- Obtain a trusted Authenticode certificate for future Windows releases. The 1.1.0 Windows download is explicitly unsigned and may trigger Windows security warnings.
- Exercise install, upgrade and rollback on clean target machines. The Linux runtime check used WSL, not a separate desktop or ARM64 machine.
- Extend physical-printer checks beyond the user-confirmed Canon MX720 formats and options, including orientation, paper sizes and other models. A completed job status means backend submission, not proof of paper output.
- Review the Android lint warnings before store submission. They cover pinned dependency versions, target API level, custom certificate trust managers and backup metadata. Successful lint does not mean those warnings were eliminated.
- Keep LibreOffice patched. Format validation and isolated conversion profiles do not provide a full sandbox for hostile documents.

See [deployment instructions](deployment.md), [captured screenshots](screenshots.md) and the [GitHub release](https://github.com/johnfinnerty-nz/PrintR/releases/tag/v1.1.0).
