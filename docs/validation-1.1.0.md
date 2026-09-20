# PrintR 1.1.0 validation

Local validation completed on 20 September 2026. These are testing builds, not signed public releases.

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
| Windows/Linux NuGet vulnerability check | No vulnerable packages reported by the configured NuGet feed |
| Windows visual checks | Overview, Settings and Recent jobs captured; action and Browse button borders checked after content-sized row correction |

The HTTPS smoke test includes authentication failures, pairing, rejected file content, invalid page ranges, a 31 MB upload, expanded formats, job polling and spool cleanup. The Android pairing remains valid after reinstalling the updated APK. No physical print jobs were sent.

## Installed for this work

The Windows build uses .NET 8, JDK 17 and Android API 35 tools. The Android emulator and API 35 image were added for UI checks. SumatraPDF 3.6.1 and LibreOffice 26.8.0.3 were installed on Windows. Ubuntu 22.04 WSL has CUPS client tools and LibreOffice Writer, Calc and Impress for Linux validation.

## Remaining release gates

- Provide a stable Android production signing key. The installable test APK uses a debug key; the release APK is unsigned.
- Sign the Windows executable with an Authenticode certificate if distributing it publicly.
- Exercise install, upgrade and rollback on clean target machines. The Linux runtime check used WSL, not a separate desktop or ARM64 machine.
- Test physical printer output, driver-specific paper sizes, color, copies, orientation and duplex. A completed job means backend submission, not verified paper output.
- Review the Android lint warnings before store submission. They cover pinned dependency versions, target API level, custom certificate trust managers and backup metadata. Successful lint does not mean those warnings were eliminated.
- Keep LibreOffice patched. Format validation and isolated conversion profiles do not provide a full sandbox for hostile documents.

See [deployment instructions](deployment.md) and [captured screenshots](screenshots.md). No GitHub release was published.
