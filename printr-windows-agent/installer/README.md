# PrintR Agent Packaging

The repository includes a no-admin ZIP packaging path in `Package.ps1`. It produces a self-contained, single-file `PrintR Agent` package suitable for copying to a Windows computer. A signed MSIX/WiX installer can be added later without changing the application data or startup behavior.

Installer responsibilities:

- Ship `PrintR.Agent.exe` and its branded icon.
- Let the user create a Start Menu shortcut or use the in-app Start with Windows toggle.
- Offer an optional Start with Windows setting using the current user's Run key.
- Explain that Windows Defender Firewall must allow PrintR Agent on Private networks for Android phones to connect.
- Avoid enabling Public network access by default.

Build the package:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Package.ps1
```

Normal use stores settings, pairing tokens, job metadata, and spool files under the current user's Windows app-data folders. Administrator rights are not required unless an administrator chooses to add a firewall rule for all users.
