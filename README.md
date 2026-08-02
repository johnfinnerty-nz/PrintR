# PrintR

PrintR is an MVP for printing from an Android phone through a Windows computer on the same Wi-Fi network.

Primary flow:

```text
Android phone -> PrintR Android app -> local Wi-Fi upload -> PrintR Agent on Windows -> Windows printer queue -> printer
```

The repo contains:

```text
/printr-android        Kotlin Android app
/printr-windows-agent  .NET 8 Windows desktop/LAN agent
/docs                  Setup, troubleshooting, and security notes
```

## MVP Status

- Android share target for PDFs, DOCX, images, text, and common document MIME types.
- Android in-app file picker using Storage Access Framework.
- Manual pairing by IP, port, and token.
- Secure pairing storage using encrypted preferences when available, with a private fallback.
- Windows HTTP API on port `8787` with token authentication.
- Windows printer enumeration, spool folder, job tracking, upload validation, mock print mode, TXT/image printing, DOCX-to-PDF conversion, and PDF command fallback.
- Branded Android launcher icon and Windows executable/tray icon.
- Printer controls for copies, color, duplex long/short edge, portrait/landscape, A4/US Letter, and page ranges where the backend supports them.
- Direct Android PDF printing through `PrintManager`.
- DOCX printing through the Windows Agent. Direct Android DOCX printing is not implemented yet; the app tells users to send DOCX files to the Windows computer instead.

Screenshot TODOs live in [docs/screenshots.md](docs/screenshots.md).

## Run The Windows Agent

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then:

```powershell
cd printr-windows-agent
dotnet run --project .\src\PrintR.Agent\PrintR.Agent.csproj
```

The agent shows the hostname, private LAN IP addresses, port, pairing token, printers, recent jobs, and service status.

The agent creates a branded Windows tray icon. Use it to open the agent, pair an Android phone, review recent jobs, open firewall settings, toggle Start with Windows, test a page, toggle mock printing, or quit from the tray menu.

Development mode without paper:

```powershell
$env:PRINTR_MOCK_PRINT = "true"
dotnet run --project .\src\PrintR.Agent\PrintR.Agent.csproj
```

Optional PDF print command:

```powershell
$env:PRINTR_PDF_COMMAND = "C:\Program Files\SumatraPDF\SumatraPDF.exe"
```

DOCX support requires a converter on the Windows computer. LibreOffice is recommended:

1. Install LibreOffice from [libreoffice.org](https://www.libreoffice.org/download/download-libreoffice/).
2. PrintR Agent auto-detects these paths:
   - `C:\Program Files\LibreOffice\program\soffice.exe`
   - `C:\Program Files (x86)\LibreOffice\program\soffice.exe`
3. If LibreOffice is elsewhere, set it in the agent UI or with:

```powershell
$env:PRINTR_LIBREOFFICE_PATH = "C:\Path\To\LibreOffice\program\soffice.exe"
```

Microsoft Word COM conversion is scaffolded as an optional fallback and disabled by default because unattended Office automation can be brittle.

Run tests:

```powershell
cd printr-windows-agent
dotnet test
```

## Build The Android App

Open `printr-android` in Android Studio and let Gradle sync. Use the Android Studio bundled JDK (JDK 17 or newer) and run the `app` configuration on a device. The repository includes the Gradle wrapper.

Command line:

```powershell
cd printr-android
.\gradlew.bat testDebugUnitTest assembleDebug --no-daemon
```

On the phone:

1. Run PrintR Agent on Windows.
2. Open the tray menu and choose `Pair Android phone`.
3. In Android, tap `Scan QR` and scan the QR code.
4. Tap `Test connection`.
5. Choose a file or share a PDF/DOCX/image/text file to PrintR.
6. Choose `Send to Windows computer`.
7. For DOCX, use `Send to Windows computer`; direct Android DOCX printing is not supported yet.

Manual pairing:

1. Open PrintR Agent.
2. Copy the computer IP address, port, and pairing token.
3. Enter them in the Android pairing form.
4. Tap `Test connection`.

Discovery:

- Android can send a UDP LAN discovery probe for PrintR Agent.
- Discovery never enables printing by itself; the token or QR pairing is still required.
- Some networks block discovery. Use manual pairing on guest Wi-Fi, public Wi-Fi, VPNs, or networks with client isolation.

Firewall:

- Allow PrintR Agent through Windows Defender Firewall on Private networks.
- Do not enable Public network access unless you intentionally need it.
- PrintR does not silently weaken firewall settings.

Token rotation:

- Click `Rotate pairing token` in PrintR Agent.
- Old Android pairings stop working and must be paired again.

Start with Windows:

- Use the tray menu's `Start with Windows` toggle. It writes only to the current user's startup setting and does not require administrator rights.

## API

- `GET /health` does not require a token.
- `GET /printers`, `POST /print`, and `GET /jobs/{id}` require `X-PrintR-Token`.
- `POST /print` accepts multipart form fields: `file`, `printerName`, `copies`, `colorMode`, `duplex`, `duplexMode`, `orientation`, `paperSize`, and `pageRange`.
- `GET /health` reports supported formats and DOCX converter availability.
- DOCX jobs may move through `queued`, `converting`, `converted`, `printing`, `completed`, or `failed`.

Supported Windows upload formats are PDF, PNG, JPG/JPEG, TXT, and DOCX. Macro-enabled Office files such as `.docm`, `.dotm`, `.xlsm`, and `.pptm` are intentionally rejected. The Android app also shows the Windows printer list after pairing and remembers the selected printer and print defaults securely.

The Windows Agent window exposes the same settings used by the phone: printer, copies, color mode, duplex edge, orientation, paper size, and page range. Native TXT/image printing uses Windows `PrintDocument`; PDF and DOCX printing use the configured PDF tool. Driver support still determines whether a physical printer honors each option.

## Package The Windows Agent

Create a self-contained, single-file ZIP package without administrator rights:

```powershell
cd printr-windows-agent
.\installer\Package.ps1
```

The package is written to `printr-windows-agent\artifacts`. Unzip it on the Windows computer, launch `PrintR.Agent.exe`, and use the tray menu to enable Start with Windows if desired. The package does not silently change firewall rules; allow the executable on Private networks when Windows prompts.

## Troubleshooting

See [docs/troubleshooting.md](docs/troubleshooting.md).
See also [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) for real-device setup issues.

## Security

See [docs/security.md](docs/security.md). Short version: PrintR is LAN-only by default, requires a strong pairing token, validates uploads, stores files in a dedicated spool folder, and does not support internet/cloud printing.

Current limitation: local HTTP is protected by token authentication but is not encrypted on the LAN. Full local HTTPS with certificate pinning is a planned hardening step.
