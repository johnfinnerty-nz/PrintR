using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows.Forms;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace PrintR.Agent;

public sealed class AgentForm : Form
{
    private readonly AgentSettingsStore _settingsStore;
    private readonly AgentSettings _settings;
    private readonly IPrinterService _printers;
    private readonly JobStore _jobs;
    private readonly IConversionBackendHealth _conversionHealth;
    private readonly PairingService _pairingService;
    private readonly Action _requestShutdown;
    private readonly NotifyIcon _tray;
    private readonly RichTextBox _text = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = RichTextBoxScrollBars.Vertical,
        BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Consolas", 9F),
        DetectUrls = false
    };
    private readonly TextBox _libreOfficePath = new() { PlaceholderText = "Path to soffice.exe", Width = 460 };
    private readonly CheckBox _mockMode = new() { Text = "Mock print mode (no paper)", AutoSize = true };
    private bool _allowClose;

    public AgentForm(AgentSettingsStore settingsStore, AgentSettings settings, IPrinterService printers, JobStore jobs, IConversionBackendHealth conversionHealth, PairingService pairingService, Action requestShutdown)
    {
        _settingsStore = settingsStore;
        _settings = settings;
        _printers = printers;
        _jobs = jobs;
        _conversionHealth = conversionHealth;
        _pairingService = pairingService;
        _requestShutdown = requestShutdown;
        Text = "PrintR Agent";
        Width = 900;
        Height = 680;
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(16);
        Icon = LoadAgentIcon();
        _libreOfficePath.Text = settings.LibreOfficePath ?? Environment.GetEnvironmentVariable("PRINTR_LIBREOFFICE_PATH") ?? "";
        _mockMode.Checked = settingsStore.Load().MockPrintMode || string.Equals(Environment.GetEnvironmentVariable("PRINTR_MOCK_PRINT"), "true", StringComparison.OrdinalIgnoreCase);
        _mockMode.CheckedChanged += (_, _) =>
        {
            _settingsStore.UpdateMockPrintMode(_mockMode.Checked);
            RefreshText();
        };

        var rotate = new Button { Text = "Rotate pairing token", AutoSize = true, Height = 32 };
        rotate.Click += (_, _) =>
        {
            _settingsStore.RotateToken();
            MessageBox.Show("Pairing token rotated. Existing Android pairings must be paired again.", "PrintR Agent");
            RefreshText();
        };

        var saveLibreOffice = new Button { Text = "Save path", AutoSize = true, Height = 32 };
        saveLibreOffice.Click += (_, _) =>
        {
            _settingsStore.UpdateLibreOfficePath(_libreOfficePath.Text);
            MessageBox.Show("LibreOffice path saved. Restart the agent for converter detection to refresh.", "PrintR Agent");
            RefreshText();
        };

        var testDocx = new Button { Text = "Test DOCX conversion", AutoSize = true, Height = 32 };
        testDocx.Click += (_, _) =>
        {
            MessageBox.Show(FormatHealth(), "PrintR Agent DOCX conversion");
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 92,
            AutoSize = false,
            AutoScroll = true,
            WrapContents = true,
            Padding = new Padding(0, 4, 0, 4)
        };
        actions.Controls.Add(MakeButton("Restart agent", () => Application.Restart()));
        actions.Controls.Add(MakeButton("Copy pairing details", CopyPairingDetails));
        actions.Controls.Add(MakeButton("Show QR code", ShowQrCode));
        actions.Controls.Add(MakeButton("Firewall settings", OpenFirewallSettings));
        actions.Controls.Add(MakeButton("Open logs folder", OpenLogsFolder));
        actions.Controls.Add(MakeButton("Test print", () => _ = TestPrintAsync()));

        var converterSettings = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 72,
            AutoSize = false,
            WrapContents = true,
            Padding = new Padding(0, 4, 0, 4),
            AutoScroll = true
        };
        converterSettings.Controls.Add(new Label { Text = "LibreOffice:", AutoSize = true, Padding = new Padding(0, 7, 6, 0) });
        converterSettings.Controls.Add(_libreOfficePath);
        converterSettings.Controls.Add(saveLibreOffice);
        converterSettings.Controls.Add(_mockMode);

        var utilityActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            AutoSize = false,
            WrapContents = true,
            Padding = new Padding(0, 4, 0, 4)
        };
        utilityActions.Controls.Add(rotate);
        utilityActions.Controls.Add(testDocx);

        Controls.Add(_text);
        Controls.Add(actions);
        Controls.Add(converterSettings);
        Controls.Add(utilityActions);
        Controls.Add(new Label
        {
            Text = "Local Windows service",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font(Font, FontStyle.Bold),
            Padding = new Padding(0, 4, 0, 0)
        });
        _tray = CreateTrayIcon();
        FormClosing += OnFormClosing;
        RefreshText();
        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += (_, _) => RefreshText();
        timer.Start();
    }

    private void RefreshText()
    {
        var settings = _settingsStore.Load();
        var lines = new List<string>
        {
            "PrintR Agent",
            $"Hostname: {Dns.GetHostName()}",
            $"Port: {_settings.Port}",
            $"Pairing token: {Mask(settings.Token)}",
            $"Pairing status: ready for QR or manual pairing",
            $"Mock print mode: {settings.MockPrintMode || string.Equals(Environment.GetEnvironmentVariable("PRINTR_MOCK_PRINT"), "true", StringComparison.OrdinalIgnoreCase)}",
            $"Supported file types: {string.Join(", ", FileValidation.SupportedExtensions)}",
            $"PDF print tool: {FormatPdfPrintTool()}",
            $"Firewall: {FirewallDiagnostics.GetWarning()}",
            $"Last Android connection attempt: {ApiDiagnostics.LastConnectionAttempt ?? "-"}",
            $"Last API error: {ApiDiagnostics.LastApiError ?? "-"}",
            "",
            "Bound interfaces:",
        };
        lines.AddRange(NetworkInfo.GetBoundInterfaces(settings.Port).Select(ip => $"  {ip}"));
        lines.Add("");
        lines.AddRange(new[]
        {
            "Local IP addresses:"
        });
        lines.AddRange(GetPrivateIps().Select(ip => $"  {ip}"));
        lines.Add("");
        lines.Add("Printers:");
        lines.AddRange(_printers.GetPrinters().Select(p => $"  {(p.IsDefault ? "*" : " ")} {p.Name} ({p.Status})"));
        lines.Add("");
        lines.Add("DOCX converter status:");
        var converterHealth = FormatHealth();
        lines.AddRange(converterHealth.Split(Environment.NewLine).Select(line => $"  {line}"));
        if (!converterHealth.Contains("\"available\": true", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("  Warning: DOCX printing requires LibreOffice or explicitly enabled Word COM conversion.");
        }
        lines.Add("");
        lines.Add("Recent jobs:");
        lines.AddRange(_jobs.Recent().Select(j => $"  {j.JobId} {j.Status}: {j.FileName} [{j.FileType}] printer={j.PrinterName ?? "default"} submitted={j.CreatedAt.LocalDateTime} completed={j.CompletedAt?.LocalDateTime.ToString() ?? "-"} - {j.Message} Warnings: {string.Join("; ", j.Warnings)}"));
        lines.Add("");
        lines.Add("Service status: running");
        var nextText = string.Join(Environment.NewLine, lines);
        if (string.Equals(_text.Text, nextText, StringComparison.Ordinal)) return;

        var firstVisibleLine = _text.IsHandleCreated ? GetFirstVisibleLine(_text.Handle) : 0;
        var selectionStart = _text.SelectionStart;
        var selectionLength = _text.SelectionLength;
        _text.Text = nextText;
        _text.SelectionStart = Math.Min(selectionStart, _text.TextLength);
        _text.SelectionLength = Math.Min(selectionLength, _text.TextLength - _text.SelectionStart);
        RestoreFirstVisibleLine(_text.Handle, firstVisibleLine);
    }

    private static int GetFirstVisibleLine(IntPtr handle) =>
        SendMessage(handle, 0xCE, IntPtr.Zero, IntPtr.Zero);

    private static void RestoreFirstVisibleLine(IntPtr handle, int targetLine)
    {
        var currentLine = GetFirstVisibleLine(handle);
        if (targetLine != currentLine)
        {
            SendMessage(handle, 0xB6, IntPtr.Zero, new IntPtr(targetLine - currentLine));
        }
    }

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

    private string FormatHealth() =>
        JsonSerializer.Serialize(_conversionHealth.GetHealth(), new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private async Task TestPrintAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"printr-test-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(path, $"PrintR Agent test page{Environment.NewLine}Computer: {Dns.GetHostName()}{Environment.NewLine}Time: {DateTime.Now:G}");
            var warnings = await _printers.PrintAsync(path, new PrintRequest(null, 1, "color", false, null), CancellationToken.None);
            var message = warnings.Count == 0 ? "The test page was sent to the default printer." : string.Join(Environment.NewLine, warnings);
            MessageBox.Show(message, "PrintR test print", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "PrintR test print failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static void OpenFirewallSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "ms-settings:windowsdefenderfirewall", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Open Windows Defender Firewall settings manually. {ex.Message}", "PrintR Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string FormatPdfPrintTool()
    {
        var status = PdfPrintTool.GetStatus();
        return status.Available ? status.Path ?? "available" : status.Message ?? "not available";
    }

    private static IEnumerable<IPAddress> GetPrivateIps()
    {
        return Dns.GetHostAddresses(Dns.GetHostName())
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
            .Where(ip =>
            {
                var b = ip.GetAddressBytes();
                return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
            });
    }

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open PrintR Agent", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        menu.Items.Add("Pair Android phone", null, (_, _) => ShowQrCode());
        menu.Items.Add("Recent jobs", null, (_, _) => MessageBox.Show(string.Join(Environment.NewLine, _jobs.Recent().Select(j => $"{j.Status}: {j.FileName}")), "PrintR Recent Jobs"));
        menu.Items.Add("Settings", null, (_, _) => { Show(); Activate(); });
        var startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = _settingsStore.Load().StartWithWindows };
        startup.CheckedChanged += (_, _) =>
        {
            StartupManager.SetStartWithWindows(startup.Checked);
            _settingsStore.UpdateStartWithWindows(startup.Checked);
        };
        menu.Items.Add(startup);
        menu.Items.Add("Quit", null, (_, _) => QuitAgent());
        var tray = new NotifyIcon
        {
            Text = "PrintR Agent",
            Icon = LoadAgentIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        return tray;
    }

    private static Icon LoadAgentIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowBalloonTip(2500, "PrintR Agent is still running", "Use the tray icon to reopen PrintR Agent or quit.", ToolTipIcon.Info);
            return;
        }

        _tray.Visible = false;
        _tray.Dispose();
    }

    private void QuitAgent()
    {
        _allowClose = true;
        _requestShutdown();
        Application.Exit();
    }

    private static Button MakeButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 32 };
        button.Click += (_, _) => action();
        return button;
    }

    private void CopyPairingDetails()
    {
        Clipboard.SetText(_pairingService.CreatePayloadJson());
        MessageBox.Show("Pairing details copied. Treat them like a password until paired.", "PrintR Agent");
    }

    private void ShowQrCode()
    {
        using var bitmap = _pairingService.CreateQrBitmap();
        var form = new Form { Text = "Pair Android phone", Width = 420, Height = 520 };
        var box = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = (Bitmap)bitmap.Clone() };
        var label = new TextBox { Dock = DockStyle.Bottom, ReadOnly = true, Multiline = true, Height = 120, Text = _pairingService.CreatePayloadJson() };
        form.Controls.Add(box);
        form.Controls.Add(label);
        form.Show(this);
    }

    private static void OpenLogsFolder()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintR Agent");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private static string Mask(string token) =>
        token.Length <= 8 ? "********" : $"{token[..4]}...{token[^4..]}";
}
