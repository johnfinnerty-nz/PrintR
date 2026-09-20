# PrintR

PrintR is a public software-development project by [John Finnerty](https://www.johnfinnerty.co.nz/), a Christchurch, New Zealand software developer. It explores local network printing from an Android phone through a Windows or Linux computer and includes Android, desktop-agent, setup, troubleshooting, and security work.

Status: active development project with public test builds. It is not presented as a hosted printing service or a production support offering.

Version 1.1 adds a redesigned Windows dashboard, Android Print/Computers/Settings navigation, persistent deployment settings, more document formats, and a Linux CUPS agent. See [deployment and signing](docs/deployment.md), [Linux setup](printr-linux-agent/README.md), and [screenshots](docs/screenshots.md).

See [1.1.0 validation results and remaining release gates](docs/validation-1.1.0.md).

Primary flow:

```text
Android phone -> PrintR app -> encrypted local upload -> Windows or Linux agent -> OS print queue -> printer
```

The repo contains:

```text
/printr-android        Kotlin Android app
/printr-windows-agent  .NET 8 Windows desktop/LAN agent
/printr-linux-agent    .NET 8 Linux/CUPS background agent
/docs                  Setup, troubleshooting, and security notes
```

## Development Status

- Android share target for PDFs, DOCX, images, text, and common document MIME types.
- Android in-app file picker using Storage Access Framework.
- Automatic LAN discovery, plus QR and manual pairing by IP, port, token, and TLS fingerprint.
- Secure pairing storage using encrypted preferences when available, with a private fallback.
- Shared HTTPS API on port `8787`, with token authentication and certificate fingerprint pinning after pairing.
- Printer enumeration, a bounded job queue, persistent job tracking, validated uploads, mock printing and Office-to-PDF conversion. Windows uses native printing and SumatraPDF; Linux uses CUPS.
- Branded Android launcher icon and Windows executable/tray icon.
- Printer controls for copies, color, duplex long/short edge, portrait/landscape, A4/US Letter, and page ranges where the backend supports them.
- Direct Android PDF printing through `PrintManager`.
- Office documents print through a Windows or Linux agent. Direct Android printing is PDF-only.

## Downloads

For controlled testing, download the latest build here:

- [PrintR Android APK](https://github.com/johnfinnerty-nz/PrintR/releases/latest/download/PrintR.apk)
- [PrintR Agent for Windows](https://github.com/johnfinnerty-nz/PrintR/releases/latest/download/PrintR-Agent-win-x64.zip)
- [PrintR Agent for Linux x64](https://github.com/johnfinnerty-nz/PrintR/releases/latest/download/PrintR-Agent-linux-x64.tar.gz)
- [Download checksums](https://github.com/johnfinnerty-nz/PrintR/releases/latest/download/SHA256SUMS.txt)

The Windows download is self-contained. Unzip it, launch `PrintR.Agent.exe`, and allow it through Windows Defender Firewall on Private networks when prompted. Android may ask you to allow installation from the app you used to open the APK.

Android 1.1.1 reduces corner rounding and fixes the Computers tab wrapping on narrow screens. It uses the same release signing key as 1.1.0, so it can update that version without uninstalling. Earlier debug-signed Android builds must be uninstalled first, which clears saved pairings and preferences. Windows and Linux agents remain at 1.1.0; Windows is still unsigned. See the [1.1.1 release notes](docs/releases/1.1.1.md) for details.

## Run The Windows Agent

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then:

```powershell
cd printr-windows-agent
dotnet run --project .\src\PrintR.Agent\PrintR.Agent.csproj
```

The agent shows setup health, printers, recent jobs and service status. Pairing credentials are shown only through the pairing actions.

The branded tray menu opens the dashboard, pairing QR code and Settings, or quits the agent. The dashboard has Overview, Recent jobs, Settings and Diagnostics tabs.

When the Android app opens, it automatically looks for PrintR Agents on the current Wi-Fi network. The `Discover` button runs the scan again. Discovery only shows available computers; it never authorizes printing without pairing.

Development mode without paper:

```powershell
$env:PRINTR_MOCK_PRINT = "true"
dotnet run --project .\src\PrintR.Agent\PrintR.Agent.csproj
```

PDF printing requires SumatraPDF. It is detected automatically or can be configured in Settings or with:

```powershell
$env:PRINTR_PDF_COMMAND = "C:\Program Files\SumatraPDF\SumatraPDF.exe"
```

Office and OpenDocument support requires LibreOffice on the computer:

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

1. Run PrintR Agent on Windows or Linux.
2. On Windows, choose `Pair a phone`. On Linux, retrieve local connection details with `--pairing`.
3. In Android, open `Computers` and scan the QR code or enter the details manually.
4. Tap `Test connection`.
5. Choose a file or share a PDF/DOCX/image/text file to PrintR.
6. Open `Print` and choose `Send to computer`.
7. Direct Android printing is available for PDFs only.

Manual pairing:

1. Open PrintR Agent.
2. Copy the computer IP address, port, pairing token, and TLS fingerprint.
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

- Click `Rotate token` in the Windows Settings tab.
- Old Android pairings stop working and must be paired again.

Start with Windows:

- Enable `Start with Windows` in Settings and save. It writes only to the current user's startup setting and does not require administrator rights.

## API

- `GET /health` does not require a token, but all API traffic uses HTTPS for new agents.
- `GET /printers`, `POST /print`, and `GET /jobs/{id}` require `X-PrintR-Token`.
- `POST /print` accepts multipart form fields: `file`, `printerName`, `copies`, `colorMode`, `duplex`, `duplexMode`, `orientation`, `paperSize`, and `pageRange`.
- `GET /health` reports supported formats and DOCX converter availability.
- DOCX jobs may move through `queued`, `converting`, `converted`, `printing`, `completed`, or `failed`.

Supported uploads are PDF, PNG, JPG/JPEG, BMP, TXT, CSV, DOCX, XLSX, PPTX, ODT, ODS, ODP and RTF. Office documents need LibreOffice. Windows PDF printing also needs SumatraPDF; Linux uses CUPS. CSV prints as plain text. Macro-enabled and legacy binary Office files are rejected. The Android app shows the paired computer's printer list and remembers print defaults.

Android controls copies, color, duplex, orientation, paper and page ranges. Windows Settings configures the computer name, port, converter paths, upload limit, processing timeout, startup, mock mode and file retention. Overview shows setup readiness and printers; Recent jobs shows submitted work. Driver support determines which print options are honored. Native Windows TXT/CSV/image printing does not implement page ranges; use PDF when page selection is required.

## Package The Windows Agent

Create a self-contained, single-file ZIP package without administrator rights:

```powershell
cd printr-windows-agent
.\installer\Package.ps1
```

The package is written to `printr-windows-agent\artifacts`. Unzip it on the Windows computer, launch `PrintR.Agent.exe`, and enable Start with Windows in Settings if desired. The package does not silently change firewall rules; allow the executable on Private networks when Windows prompts.

## Troubleshooting

See [docs/troubleshooting.md](docs/troubleshooting.md).

## Security

See [docs/security.md](docs/security.md). Short version: PrintR is LAN-only by default, requires a strong pairing token, validates uploads, stores files in a dedicated spool folder, and does not support internet/cloud printing.

New pairings use HTTPS and pin the agent certificate fingerprint from its pairing details. Older HTTP pairings remain available only as a temporary compatibility path; remove and re-pair them to restore encrypted printing.
