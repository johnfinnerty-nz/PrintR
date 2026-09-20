using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace PrintR.Agent;

public sealed class AgentForm : Form
{
    private static readonly Color Ink = Color.FromArgb(24, 40, 58);
    private static readonly Color Teal = Color.FromArgb(0, 112, 105);
    private static readonly Color Canvas = Color.FromArgb(244, 247, 250);
    private readonly AgentSettingsStore _store;
    private readonly AgentSettings _startup;
    private readonly IPrinterService _printers;
    private readonly JobStore _jobs;
    private readonly IConversionBackendHealth _conversion;
    private readonly PairingService _pairing;
    private readonly Action _shutdown;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Padding = new Point(24, 12) };
    private readonly Label _status = LabelFor("Ready to connect", 24, true);
    private readonly Label _summary = LabelFor("", 11);
    private readonly Label _readiness = LabelFor("", 11);
    private readonly Label _saved = LabelFor("Name, port and converter changes require a restart.", 10);
    private readonly DataGridView _printerGrid = Grid();
    private readonly DataGridView _jobGrid = Grid();
    private readonly RichTextBox _diagnostics = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10), BackColor = Color.White, DetectUrls = false };
    private readonly TextBox _name = new() { Dock = DockStyle.Fill, MaxLength = 80 };
    private readonly TextBox _office = new() { Dock = DockStyle.Fill, PlaceholderText = "Auto-detect LibreOffice" };
    private readonly TextBox _pdf = new() { Dock = DockStyle.Fill, PlaceholderText = "Auto-detect SumatraPDF" };
    private readonly NumericUpDown _port = new() { Minimum = 1024, Maximum = 65535, Width = 140 };
    private readonly NumericUpDown _limit = new() { Minimum = 1, Maximum = 100, Width = 140 };
    private readonly NumericUpDown _timeout = new() { Minimum = 30, Maximum = 600, Increment = 30, Width = 140 };
    private readonly CheckBox _mock = new() { Text = "Mock printing (accept jobs without using paper)", AutoSize = true };
    private readonly CheckBox _keep = new() { Text = "Keep uploaded files for debugging", AutoSize = true };
    private readonly CheckBox _start = new() { Text = "Start PrintR when I sign in to Windows", AutoSize = true };
    private bool _allowClose;
    private string _lastJobs = "";
    private string _lastPrinters = "";

    public AgentForm(AgentSettingsStore settingsStore, AgentSettings settings, IPrinterService printers, JobStore jobs,
        IConversionBackendHealth conversionHealth, PairingService pairingService, TlsCertificateInfo certificate, Action requestShutdown)
    {
        _store = settingsStore; _startup = settings; _printers = printers; _jobs = jobs;
        _conversion = conversionHealth; _pairing = pairingService; _shutdown = requestShutdown;
        Text = "PrintR Agent"; ClientSize = new Size(1000, 730); MinimumSize = new Size(840, 700);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
        BackColor = Canvas; ForeColor = Ink; Font = new Font("Segoe UI", 10);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, BackColor = Ink, Padding = new Padding(24, 16, 24, 16), Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize)); header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var brand = LabelFor("PrintR", 26, true); brand.ForeColor = Color.White; brand.AutoSize = true; brand.Margin = new Padding(0, 0, 0, 4);
        var tagline = LabelFor("YOUR PHONE. YOUR FILES. YOUR PRINTER.", 10); tagline.ForeColor = Color.FromArgb(177, 220, 217); tagline.AutoSize = true; tagline.Margin = Padding.Empty;
        header.Controls.Add(brand, 0, 0); header.Controls.Add(tagline, 0, 1); shell.Controls.Add(header, 0, 0);
        _tabs.Margin = new Padding(20, 16, 20, 8); shell.Controls.Add(_tabs, 0, 1);
        var footer = LabelFor($"PrintR {AppInfo.Version}   |   HTTPS on port {settings.Port}   |   Local network only", 9);
        footer.AutoSize = true; footer.Padding = new Padding(24, 8, 12, 8); shell.Controls.Add(footer, 0, 2); Controls.Add(shell);
        BuildOverview(); BuildJobs(); BuildSettings(); Page("Diagnostics").Controls.Add(_diagnostics);
        _tray = new NotifyIcon { Text = "PrintR Agent", Icon = Icon, Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        _tray.ContextMenuStrip.Items.Add("Open PrintR", null, (_, _) => OpenWindow(0));
        _tray.ContextMenuStrip.Items.Add("Pair phone", null, (_, _) => ShowQrCode());
        _tray.ContextMenuStrip.Items.Add("Settings", null, (_, _) => OpenWindow(2));
        _tray.ContextMenuStrip.Items.Add("Quit", null, (_, _) => Quit());
        _tray.DoubleClick += (_, _) => OpenWindow(0);
        FormClosing += (_, e) => { if (!_allowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; } _timer.Stop(); _tray.Visible = false; };
        FormClosed += (_, _) => { _timer.Dispose(); _tray.Dispose(); };
        _timer.Tick += (_, _) => RefreshStatus(); RefreshStatus(); _timer.Start();
        Shown += (_, _) =>
        {
            var area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
        };
    }

    private void BuildOverview()
    {
        var page = Page("Overview");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        for (var row = 0; row < 5; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _status.AutoSize = true; _status.Margin = new Padding(0, 0, 0, 6);
        _summary.AutoSize = true; _summary.Margin = new Padding(0, 0, 0, 10);
        _status.ForeColor = Teal; layout.Controls.Add(_status, 0, 0); layout.Controls.Add(_summary, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
        actions.Controls.Add(ButtonFor("Pair a phone", ShowQrCode, true));
        actions.Controls.Add(ButtonFor("Copy pairing details", () => { Clipboard.SetText(_pairing.CreatePayloadJson()); MessageBox.Show("Pairing details copied. Keep them private.", "PrintR"); }));
        actions.Controls.Add(ButtonFor("Refresh printers", RefreshStatus)); layout.Controls.Add(actions, 0, 2);
        _readiness.AutoSize = true; _readiness.BackColor = Canvas; _readiness.Padding = new Padding(16, 12, 16, 12); _readiness.Margin = new Padding(0, 0, 0, 14);
        layout.Controls.Add(_readiness, 0, 3);
        var printerHeading = LabelFor("Printers on this computer", 13, true); printerHeading.AutoSize = true; printerHeading.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(printerHeading, 0, 4);
        _printerGrid.Columns.Add("Printer", "PRINTER"); _printerGrid.Columns.Add("Default", "DEFAULT"); _printerGrid.Columns.Add("State", "STATUS");
        _printerGrid.Columns[0].FillWeight = 200;
        layout.Controls.Add(_printerGrid, 0, 5); page.Controls.Add(layout);
    }

    private void BuildJobs()
    {
        var page = Page("Recent jobs");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(LabelFor("Your recent print activity", 22, true), 0, 0);
        layout.Controls.Add(LabelFor("Completed means submitted to the printer. Double-click a row for its result and warnings.", 10), 0, 1);
        foreach (var column in new[] { "FILE", "STATUS", "PRINTER", "SUBMITTED" }) _jobGrid.Columns.Add(column, column);
        _jobGrid.Columns[0].FillWeight = 180;
        _jobGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && _jobGrid.Rows[e.RowIndex].Tag is PrintJob job) MessageBox.Show(job.Message + "\n\n" + string.Join("\n", job.Warnings), job.FileName ?? "Print job"); };
        layout.Controls.Add(_jobGrid, 0, 2); page.Controls.Add(layout);
    }

    private void BuildSettings()
    {
        var settings = _store.Load();
        _name.Text = settings.FriendlyName; _port.Value = settings.Port; _office.Text = settings.LibreOfficePath ?? "";
        _pdf.Text = settings.PdfToolPath ?? ""; _limit.Value = settings.MaxUploadMegabytes; _timeout.Value = settings.JobTimeoutSeconds;
        _mock.Checked = settings.MockPrintMode; _keep.Checked = settings.DebugKeepSpoolFiles; _start.Checked = settings.StartWithWindows;
        var page = Page("Settings"); page.AutoScroll = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 0, Padding = new Padding(0, 0, 8, 0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddSetting(layout, "Computer name", _name); AddSetting(layout, "HTTPS port (restart required)", _port);
        AddSetting(layout, "LibreOffice executable", BrowseField(_office)); AddSetting(layout, "SumatraPDF executable", BrowseField(_pdf));
        AddSetting(layout, "Upload limit (MB)", _limit); AddSetting(layout, "Job timeout (seconds)", _timeout);
        AddSetting(layout, "Startup", _start); AddSetting(layout, "Testing", _mock); AddSetting(layout, "File retention", _keep);
        AddSetting(layout, "", LabelFor("Files are deleted after processing unless retention is enabled.", 9));
        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Size = Size.Empty };
        actions.Controls.Add(ButtonFor("Save settings", SaveSettings, true));
        actions.Controls.Add(ButtonFor("Restart agent", () => { _allowClose = true; _shutdown(); Application.Restart(); }));
        actions.Controls.Add(ButtonFor("Rotate token", () => { if (MessageBox.Show("All paired phones will need to pair again. Rotate the token?", "PrintR", MessageBoxButtons.YesNo) == DialogResult.Yes) { _store.RotateToken(); _saved.Text = "Token rotated. Pair your phones again."; } }));
        AddSetting(layout, "", actions); AddSetting(layout, "", _saved); page.Controls.Add(layout);
    }

    private void SaveSettings()
    {
        try
        {
            _store.UpdatePreferences(_name.Text, (int)_port.Value, _office.Text, _pdf.Text, (int)_limit.Value, (int)_timeout.Value, _keep.Checked, _mock.Checked);
            if (_start.Checked != _store.Load().StartWithWindows) { StartupManager.SetStartWithWindows(_start.Checked); _store.UpdateStartWithWindows(_start.Checked); }
            _saved.Text = "Saved. Restart to apply name, port or converter changes."; _saved.ForeColor = Teal; RefreshStatus();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { _saved.Text = ex.Message; _saved.ForeColor = Color.Firebrick; }
    }

    private void RefreshStatus()
    {
        if (IsDisposed) return;
        try
        {
            var settings = _store.Load();
            var mock = settings.MockPrintMode || Environment.GetEnvironmentVariable("PRINTR_MOCK_PRINT") == "true";
            var printers = _printers.GetPrinters();
            var office = JsonSerializer.SerializeToElement(_conversion.GetHealth()).GetProperty("libreOffice").GetProperty("Available").GetBoolean();
            var pdf = PdfPrintTool.GetStatus().Available;
            _status.Text = mock ? "Mock printing is on" : printers.Count > 0 ? "Ready for your next document" : "Connect a printer to get started";
            _summary.Text = $"{settings.FriendlyName}   |   {printers.Count} printer(s)   |   Encrypted local connection\nPair your Android phone, choose a file, and send it here.";
            _readiness.Text = $"SETUP CHECK\nPDF printing: {(pdf ? "Ready" : "Install SumatraPDF")}     Office documents: {(office ? "Ready" : "Install LibreOffice")}\nPDF, PNG, JPG, BMP, TXT, CSV, DOCX, XLSX, PPTX, ODT, ODS, ODP, RTF";
            var printerState = JsonSerializer.Serialize(printers);
            if (printerState != _lastPrinters)
            {
                _printerGrid.Rows.Clear();
                foreach (var printer in printers) _printerGrid.Rows.Add(printer.Name, printer.IsDefault ? "Yes" : "", printer.Status);
                if (printers.Count == 0) _printerGrid.Rows.Add("No printers found. Add one in Windows Settings.", "", "Not configured");
                _lastPrinters = printerState;
            }
            var jobs = _jobs.Recent(); var jobState = JsonSerializer.Serialize(jobs);
            if (jobState != _lastJobs)
            {
                _jobGrid.Rows.Clear();
                foreach (var job in jobs)
                {
                    var row = _jobGrid.Rows[_jobGrid.Rows.Add(job.FileName, job.Status, job.PrinterName ?? "Default", job.CreatedAt.LocalDateTime.ToString("g"))];
                    row.Tag = job; row.Cells[1].Style.ForeColor = job.Status == JobStatus.Failed ? Color.Firebrick : Teal;
                    foreach (DataGridViewCell cell in row.Cells) cell.ToolTipText = job.Message + " " + string.Join(" ", job.Warnings);
                }
                if (jobs.Count == 0) _jobGrid.Rows.Add("No jobs yet. Send a document from your phone.", "", "", "");
                _lastJobs = jobState;
            }
            var diagnostics = $"PrintR {AppInfo.Version}\nService: running\nListening port: {_startup.Port}\n" + string.Join("\n", NetworkInfo.GetBoundInterfaces(_startup.Port)) +
                $"\n\n{FirewallDiagnostics.GetWarning()}\n\nLast connection: {ApiDiagnostics.LastConnectionAttempt ?? "None"}\nLast error: {ApiDiagnostics.LastApiError ?? "None"}\n\n" + JsonSerializer.Serialize(_conversion.GetHealth(), new JsonSerializerOptions { WriteIndented = true });
            if (_diagnostics.Text != diagnostics) _diagnostics.Text = diagnostics;
        }
        catch (Exception ex) { _status.Text = "Setup needs attention"; _summary.Text = ex.Message; }
    }

    private void ShowQrCode()
    {
        using var bitmap = _pairing.CreateQrBitmap();
        using var form = new Form { Text = "Pair your phone", ClientSize = new Size(460, 510), StartPosition = FormStartPosition.CenterParent, BackColor = Color.White, Font = Font };
        var label = LabelFor("Open PrintR on Android and tap Computers, then Scan QR.\nThis code is private. Do not share it in screenshots.", 10);
        label.Dock = DockStyle.Bottom; label.Height = 70; label.Padding = new Padding(16);
        form.Controls.Add(new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = bitmap }); form.Controls.Add(label); form.ShowDialog(this);
    }
    private void OpenWindow(int tab) { Show(); WindowState = FormWindowState.Normal; _tabs.SelectedIndex = tab; Activate(); }
    private void Quit() { _allowClose = true; _shutdown(); Close(); Application.Exit(); }
    private TabPage Page(string title) { var page = new TabPage(title) { BackColor = Color.White, Padding = new Padding(24) }; _tabs.TabPages.Add(page); return page; }
    private static Label LabelFor(string text, float size, bool bold = false) => new()
    { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = Ink, Dock = DockStyle.Fill, AutoSize = false, UseCompatibleTextRendering = true, Padding = new Padding(0, 3, 0, 3), TextAlign = ContentAlignment.MiddleLeft };
    internal static Button ButtonFor(string text, Action action, bool primary = false)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(0, 44), UseCompatibleTextRendering = true, Padding = new Padding(12, 5, 12, 5), FlatStyle = FlatStyle.Flat, BackColor = primary ? Teal : Color.White, ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand, Margin = new Padding(0, 4, 10, 4) };
        button.FlatAppearance.BorderColor = primary ? Teal : Color.FromArgb(208, 218, 228); button.Click += (_, _) => action(); return button;
    }
    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        GridColor = Color.FromArgb(232, 237, 241), RowTemplate = { Height = 42 }, EnableHeadersVisualStyles = false,
        ColumnHeadersHeight = 36, ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Canvas, ForeColor = Ink, Font = new Font("Segoe UI", 9, FontStyle.Bold) },
        DefaultCellStyle = new DataGridViewCellStyle { Padding = new Padding(8, 4, 8, 4), SelectionBackColor = Color.FromArgb(222, 241, 238), SelectionForeColor = Ink }
    };
    internal static void AddSetting(TableLayoutPanel layout, string label, Control control)
    {
        var row = layout.RowCount++;
        // Composite rows must include their children's preferred heights and margins at every DPI.
        layout.RowStyles.Add(control is FlowLayoutPanel or TableLayoutPanel
            ? new RowStyle(SizeType.AutoSize)
            : new RowStyle(SizeType.Absolute, 43));
        var caption = LabelFor(label, 10);
        caption.AutoSize = true;
        layout.Controls.Add(caption, 0, row); control.Margin = new Padding(0, 6, 0, 6); layout.Controls.Add(control, 1, row);
    }
    internal static Control BrowseField(TextBox input)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Size = Size.Empty, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88)); panel.Controls.Add(input, 0, 0);
        var browse = new Button { Text = "Browse", AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(6, 0, 0, 0) };
        browse.Click += (_, _) => { using var dialog = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe", CheckFileExists = true }; if (dialog.ShowDialog() == DialogResult.OK) input.Text = dialog.FileName; };
        panel.Controls.Add(browse, 1, 0); return panel;
    }
}
