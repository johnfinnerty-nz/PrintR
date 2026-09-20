# PrintR for Linux

The Linux agent runs the same HTTPS API, token pairing, file validation and job queue as Windows. It uses CUPS for printing and LibreOffice for Office conversion. This release is a background service with command-line administration; the desktop GUI is Windows-only.

## Install and run

On Ubuntu 22.04 or newer, install system dependencies and configure a printer first:

```bash
sudo apt install cups cups-client cups-filters libreoffice-writer libreoffice-calc libreoffice-impress
lpstat -p -d
```

Extract the matching `linux-x64` or `linux-arm64` archive, then:

```bash
chmod +x PrintR.Agent.Linux
./PrintR.Agent.Linux --status
./PrintR.Agent.Linux
```

The binary includes .NET. Standard distribution libraries including ICU, OpenSSL and libc are still required. Alpine/musl is not a supported target. CUPS must have a configured, working printer. Driver support determines which paper, color and duplex options work.

For a user service, run `bash install.sh` from the extracted folder, then `systemctl --user enable --now printr-agent`. This does not change firewall rules or enable lingering. The user service normally starts at sign-in. Use `journalctl --user -u printr-agent` for logs. Stop it with `systemctl --user stop printr-agent` before replacing the executable during an upgrade.

## Pair and configure

Run `./PrintR.Agent.Linux --pairing` locally to retrieve the IP address, port, token and TLS fingerprint. Enter these in Android's Computers screen. Keep the pairing JSON private. UDP discovery uses port 8788, and HTTPS defaults to 8787. Permit these only from your trusted LAN if a firewall is enabled.

Configuration is under `~/.config/PrintR Agent/settings.json` and jobs under `~/.local/share/PrintR Agent/`. `--status` shows the actual path. Stop the service before editing configuration, preserve the existing token and certificate fields, then restart it. Supported settings are `FriendlyName`, `Port` (1024 to 65535), `LibreOfficePath`, `MaxUploadMegabytes` (1 to 100), `JobTimeoutSeconds` (30 to 600), `MockPrintMode` and `DebugKeepSpoolFiles`. `PRINTR_DATA_DIR` overrides both directories for isolated testing. Set `PRINTR_MOCK_PRINT=true` to test without paper.

PDF, PNG, JPEG, BMP, TXT and CSV go to CUPS; CSV is plain text. DOCX, XLSX, PPTX, ODT, ODS, ODP and RTF require LibreOffice. Macro-enabled and legacy binary Office files are rejected. A completed PrintR job means the spooler accepted it; check CUPS for physical completion.

## Build

```powershell
pwsh ./Package.ps1 -Runtime linux-x64
```

Choose `linux-arm64` for an ARM64 package. Test on the intended CPU and printer before distributing that build.
