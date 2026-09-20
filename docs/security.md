# PrintR Security Notes

PrintR is intended for same-Wi-Fi printing only.

- Both agents require `X-PrintR-Token` for protected endpoints. `/health`, `/discovery-info` and `/pair/test` are the exceptions; `/pair/test` validates the token in its request body.
- `/discovery-info` and `/pair/test` expose only safe pairing metadata and pairing success/failure. They do not allow printing.
- New pairings use local HTTPS. The Windows Agent generates a self-signed certificate, includes its SHA-256 fingerprint in the QR payload, and Android pins that fingerprint for every request.
- The certificate is stored in the current user's PrintR Agent app-data folder. If it is regenerated, Android correctly refuses the changed certificate until the phone is paired again.
- On first run, the agent creates a strong random token in `%APPDATA%\PrintR Agent\settings.json`.
- Use the agent window's rotate-token action if the token is exposed.
- Android disables device backup for PrintR so pairing tokens and certificate fingerprints are not copied into cloud backups.
- The HTTPS server binds to configured local URLs and is designed for private LAN use, not internet exposure.
- Uploads are capped at 100 MB by default.
- The shared backend accepts PDF, PNG, JPG/JPEG, BMP, TXT, CSV, DOCX, XLSX, PPTX, ODT, ODS, ODP and RTF. Extensions, MIME types, signatures and Office archive structure are checked.
- Macro-enabled Office files such as `.docm`, `.dotm`, `.xlsm`, and `.pptm` are intentionally rejected.
- Office files are converted in dedicated temporary job folders and isolated LibreOffice profiles with macro security enabled. These checks are not a general-purpose sandbox. Pair only trusted devices and keep conversion tools patched.
- Macro-enabled extensions and embedded macro payloads detected in Office archives are rejected. Word COM conversion is optional, disabled by default, and should be used cautiously.
- Uploaded filenames are sanitized and never trusted as filesystem paths.
- Files are stored under `%LOCALAPPDATA%\PrintR Agent\Spool`.
- Temporary files are deleted after processing unless debug retention is enabled. Mock mode alone does not retain documents.
- External PDF printing is only attempted through a configured executable path, such as SumatraPDF. User-supplied print options are not passed to a shell command.
- LibreOffice conversion uses a configured or detected `soffice.exe` path and `ProcessStartInfo.ArgumentList`, not shell-concatenated commands.

Do not port-forward the agent or expose it to the public internet.
