# PrintR Troubleshooting

## Android cannot find my computer

- Confirm both devices are on the same Wi-Fi network.
- Avoid guest Wi-Fi, public Wi-Fi, VPNs, or networks with client isolation.
- Use manual pairing with the IP address shown in PrintR Agent if discovery is blocked.

## QR pairing fails

- Rotate the QR code by opening PrintR Agent and choosing Pair Android phone.
- Confirm the QR code is from PrintR Agent, not another app.
- If the token was rotated, scan the new QR code.

## Manual pairing fails

- Enter the IP address shown in PrintR Agent.
- Use port `8787` unless changed.
- Copy the pairing token carefully.
- Tap Test connection before printing.

## Wrong token

Pairing tokens are strong and rotatable. If the token was rotated in PrintR Agent, old Android pairings stop working and must be paired again.

## Firewall blocked

- Open Windows Defender Firewall.
- Choose Allow an app through firewall.
- Allow PrintR Agent on Private networks.
- Do not enable Public network access unless you understand the risk.

## Upload succeeds but print fails

- Confirm the Windows printer works locally.
- Try mock mode with `PRINTR_MOCK_PRINT=true`.
- Check recent jobs in PrintR Agent for the exact failure.

## DOCX conversion fails

- Install LibreOffice.
- Configure the `soffice.exe` path in PrintR Agent if it is not auto-detected.
- `.docm` and other macro-enabled files are intentionally rejected.

## PDF printing fails

- Configure `PRINTR_PDF_COMMAND` to a reliable PDF print tool such as SumatraPDF.
- Confirm the converted or uploaded PDF opens locally.

## Printer options ignored

PrintR sends copies, color mode, duplex edge, paper size, orientation, and page range to the selected backend. Printer drivers and PDF tools can still override them. For PDFs and DOCX, install SumatraPDF and restart the agent so the reliable PDF command path is used. For native TXT/image printing, confirm the selected printer supports the requested paper and duplex mode in Windows printer preferences.

If duplex is still ignored, choose `Long edge` or `Short edge` explicitly in the Android print options and retry. Some drivers only honor their Windows queue default; set that queue to duplex or create a printer profile with duplex enabled.

## Computer sleeps before printing

Keep the Windows computer awake while printing. Disable sleep temporarily for long DOCX conversions or large uploads.

## Public Wi-Fi or guest Wi-Fi blocks device discovery

Use a private home/work network or manual IP pairing. Many guest networks intentionally block device-to-device traffic.
