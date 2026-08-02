# PrintR Security Notes

PrintR is intended for same-Wi-Fi printing only.

- The Windows agent requires `X-PrintR-Token` for every endpoint except `/health`.
- `/discovery-info` and `/pair/test` expose only safe pairing metadata and pairing success/failure. They do not allow printing.
- Local HTTP is protected by token authentication but is not encrypted on the LAN yet.
- TODO: add local HTTPS, generate a self-signed certificate, include the fingerprint in the QR payload, and pin it in Android.
- On first run, the agent creates a strong random token in `%APPDATA%\PrintR Agent\settings.json`.
- Use the agent window's rotate-token action if the token is exposed.
- The HTTP server binds to configured local URLs and is designed for private LAN use, not internet exposure.
- Uploads are capped at 100 MB by default.
- Only PDF, PNG, JPG/JPEG, TXT, and DOCX are accepted by the Windows MVP backend.
- Macro-enabled Office files such as `.docm`, `.dotm`, `.xlsm`, and `.pptm` are intentionally rejected.
- DOCX files are treated as untrusted Office documents and converted in a dedicated temporary job folder.
- PrintR never executes Office macros. Word COM conversion is optional, disabled by default, and should be used cautiously.
- Uploaded filenames are sanitized and never trusted as filesystem paths.
- Files are stored under `%LOCALAPPDATA%\PrintR Agent\Spool`.
- Temporary files are deleted after printing unless debug/mock retention is enabled.
- External PDF printing is only attempted through a configured executable path, such as SumatraPDF. User-supplied print options are not passed to a shell command.
- LibreOffice conversion uses a configured or detected `soffice.exe` path and `ProcessStartInfo.ArgumentList`, not shell-concatenated commands.

Do not port-forward the agent or expose it to the public internet.
