# PrintR Troubleshooting

Most PrintR problems come down to one of three things: the phone and computer cannot reach each other, Windows Firewall is blocking the agent, or the printer/converter needs attention. Start with the section that best matches what you are seeing.

## Android cannot find my computer

PrintR searches automatically when the Android app opens. Give the scan a moment, then tap `Discover` to try again if the computer is not listed.

First, check that the phone and Windows computer are connected to the same Wi-Fi network. Guest and public networks often prevent devices from talking to each other, and a VPN or client isolation setting can do the same.

If discovery still does not find the computer, open PrintR Agent and use the private LAN IP address shown there for manual pairing.

## QR pairing fails

Open PrintR Agent and choose `Pair Android phone` again, then scan the QR code shown in that window. Make sure you are scanning the code from PrintR Agent, not an older screenshot or another app.

If you recently rotated the pairing token, the old QR code is no longer valid. Generate and scan the new one.

## Certificate changed

PrintR pins the Windows Agent certificate when you pair. If the Android app says the certificate changed, do not bypass the warning. Open the agent, choose `Pair Android phone`, and scan the current QR code again. This can happen after Windows is reinstalled or the agent's local settings are reset.

## Manual pairing fails

Use the IP address shown in PrintR Agent and port `8787`, unless you changed the port. Copy the pairing token and TLS fingerprint carefully, then tap `Test connection` in the Android app before trying to print.

If the test times out, the most likely causes are a firewall rule, different Wi-Fi networks, or a network that blocks device-to-device traffic.

## Wrong token

Pairing tokens can be rotated for security. If you rotated the token in PrintR Agent, the old Android pairing will stop working. Pair the phone again with the new token or QR code.

## Firewall blocked

Windows may be blocking the connection before it reaches PrintR Agent. Open Windows Defender Firewall, choose `Allow an app through firewall`, and allow PrintR Agent on `Private` networks. Printing uses HTTPS on TCP port `8787`; discovery also uses UDP port `8788`.

Leave `Public` network access disabled unless you have a specific reason to enable it. If Windows asks for permission the first time PrintR Agent starts, allow it only when the current network is trusted.

## Upload succeeds but print fails

Check that the printer can print a test page directly from Windows. To separate a printer problem from a PrintR problem, try mock mode with `PRINTR_MOCK_PRINT=true`; mock mode accepts and processes jobs without using paper.

The recent jobs list in PrintR Agent usually contains the specific failure message.

## DOCX conversion fails

DOCX files need a document converter on the Windows computer. Install LibreOffice and configure the `soffice.exe` path in PrintR Agent if it is not detected automatically.

PrintR intentionally rejects `.docm` and other macro-enabled Office files. This prevents macros from being run during conversion.

## PDF printing fails

Configure `PRINTR_PDF_COMMAND` to a reliable PDF print tool such as SumatraPDF, then restart the agent. Also check that the uploaded or converted PDF opens locally on Windows.

## Printer options ignored

PrintR sends copies, color mode, duplex edge, paper size, orientation, and page range to the selected backend. Printer drivers and PDF tools can still override them. For PDFs and DOCX, install SumatraPDF and restart the agent so the reliable PDF command path is used. For native TXT/image printing, confirm the selected printer supports the requested paper and duplex mode in Windows printer preferences.

If duplex is still ignored, choose `Long edge` or `Short edge` explicitly in the Android print options and try again. Some drivers only honor the Windows queue default, so set that queue to duplex or create a printer profile with duplex enabled.

## Computer sleeps before printing

Keep the Windows computer awake while a job is uploading, converting, or printing. For a long DOCX conversion or a large upload, temporarily increase the computer's sleep timeout.

## Public Wi-Fi or guest Wi-Fi blocks device discovery

This is usually a network policy rather than an app error. Try a private home or work network, or pair manually by IP address. Many guest networks intentionally block device-to-device traffic, so PrintR cannot discover or reach the computer there.
