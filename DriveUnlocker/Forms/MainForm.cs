using DriveUnlocker.Core;
using DriveUnlocker.Helpers;
using System.Reflection;

namespace DriveUnlocker.Forms;

public class MainForm : Form
{
    private const string ColumnSelected = "Selected";
    private const string ColumnName = "ProcessName";
    private const string ColumnPid = "Pid";
    private const string ColumnPath = "ExePath";
    private const string ColumnAction = "Action";

    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;

    // ── 驱动器 Tab 控件 ────────────────────────────────────────────────────
    private readonly ComboBox cmbDrive;
    private readonly Button btnScan;
    private readonly Button btnEject;
    private readonly DataGridView dgvProcesses;
    private readonly CheckBox chkSelectAll;
    private readonly Button btnKillSelected;
    private readonly Button btnKillAllAndEject;

    // ── 文件检查 Tab 控件 ──────────────────────────────────────────────────
    private readonly TextBox txtFilePath;
    private readonly Button btnBrowse;
    private readonly Button btnFileScan;
    private readonly DataGridView dgvFileProcesses;
    private readonly CheckBox chkFileSelectAll;
    private readonly Button btnFileKillSelected;

    // ── 共享 ───────────────────────────────────────────────────────────────
    private readonly TabControl tabMain;
    private readonly ToolStripStatusLabel lblStatus;

    private CancellationTokenSource? _scanCts;
    private bool _suppressSelectAllChange;
    private bool _suppressFileSelectAllChange;
    private bool _isLoadingDrives;
    private bool _isBusy;

    public MainForm()
    {
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Text = $"DriveUnlocker v{GetDisplayVersion()}";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 460);
        ClientSize = new Size(900, 560);

        cmbDrive = new ComboBox();
        btnScan = new Button();
        btnEject = new Button();
        dgvProcesses = new DataGridView();
        chkSelectAll = new CheckBox();
        btnKillSelected = new Button();
        btnKillAllAndEject = new Button();

        txtFilePath = new TextBox();
        btnBrowse = new Button();
        btnFileScan = new Button();
        dgvFileProcesses = new DataGridView();
        chkFileSelectAll = new CheckBox();
        btnFileKillSelected = new Button();

        tabMain = new TabControl();
        lblStatus = new ToolStripStatusLabel();

        InitializeLayout();
        EnableFormWideFileDrop();
        Load += MainForm_Load;
        FormClosing += MainForm_FormClosing;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Layout
    // ══════════════════════════════════════════════════════════════════════

    private void InitializeLayout()
    {
        SuspendLayout();

        // 状态栏（窗体级，两个 Tab 共用）
        StatusStrip statusStrip = new() { Dock = DockStyle.Bottom, SizingGrip = false };
        lblStatus.Name = "lblStatus";
        lblStatus.Text = "ℹ️ 请选择驱动器并点击扫描";
        statusStrip.Items.Add(lblStatus);

        // 选项卡
        tabMain.Dock = DockStyle.Fill;
        tabMain.Padding = new Point(12, 4);

        TabPage tabDrive = new() { Text = "  驱动器  ", Padding = new Padding(0) };
        TabPage tabFile = new() { Text = "  文件占用检查  ", Padding = new Padding(0) };

        InitializeDriveTab(tabDrive);
        InitializeFileTab(tabFile);

        tabMain.TabPages.Add(tabDrive);
        tabMain.TabPages.Add(tabFile);

        tabMain.SelectedIndexChanged += (_, _) => RefreshStatusOnTabSwitch(tabMain);

        Controls.Add(tabMain);
        Controls.Add(statusStrip);

        ResumeLayout(performLayout: false);
        PerformLayout();

        RefreshActionState();
    }

    private void InitializeDriveTab(TabPage page)
    {
        // ── 顶部面板 ──────────────────────────────────────────────────────
        Panel topPanel = new()
        {
            Dock = DockStyle.Top,
            Height = 52,
            BackColor = SystemColors.Control
        };

        TableLayoutPanel topLayout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Padding = new Padding(10, 0, 10, 0),
            Margin = new Padding(0)
        };
        topLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Label lblDrive = new()
        {
            AutoSize = true,
            Text = "驱动器：",
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 6, 0)
        };

        cmbDrive.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbDrive.FormattingEnabled = true;
        cmbDrive.Size = new Size(280, 23);
        cmbDrive.Anchor = AnchorStyles.None;
        cmbDrive.Margin = new Padding(0, 0, 10, 0);
        cmbDrive.SelectedIndexChanged += CmbDrive_SelectedIndexChanged;

        btnScan.Text = "🔍 扫描占用";
        btnScan.Size = new Size(110, 28);
        btnScan.Anchor = AnchorStyles.None;
        btnScan.Margin = new Padding(0, 0, 6, 0);
        btnScan.Click += btnScan_Click;

        btnEject.Text = "⏏ 弹出驱动器";
        btnEject.Size = new Size(120, 28);
        btnEject.Anchor = AnchorStyles.None;
        btnEject.Enabled = false;
        btnEject.Click += btnEject_Click;

        topLayout.Controls.Add(lblDrive, 0, 0);
        topLayout.Controls.Add(cmbDrive, 1, 0);
        topLayout.Controls.Add(btnScan, 2, 0);
        topLayout.Controls.Add(btnEject, 3, 0);
        topPanel.Controls.Add(topLayout);

        // ── 分隔线 ────────────────────────────────────────────────────────
        Panel topSep = new() { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(213, 213, 213) };

        // ── DataGridView ──────────────────────────────────────────────────
        ConfigureProcessGrid(dgvProcesses);
        dgvProcesses.CurrentCellDirtyStateChanged += dgvProcesses_CurrentCellDirtyStateChanged;
        dgvProcesses.CellContentClick += dgvProcesses_CellContentClick;
        dgvProcesses.CellValueChanged += dgvProcesses_CellValueChanged;
        AddProcessGridColumns(dgvProcesses);

        // ── 底部分隔线 ────────────────────────────────────────────────────
        Panel bottomSep = new() { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(213, 213, 213) };

        // ── 操作栏 ────────────────────────────────────────────────────────
        Panel actionPanel = new() { Dock = DockStyle.Bottom, Height = 44, BackColor = SystemColors.Control };
        TableLayoutPanel actionLayout = BuildActionLayout();

        chkSelectAll.AutoSize = false;
        chkSelectAll.Text = "全选";
        chkSelectAll.Size = new Size(66, 28);
        chkSelectAll.Anchor = AnchorStyles.None;
        chkSelectAll.TextAlign = ContentAlignment.MiddleRight;
        chkSelectAll.CheckAlign = ContentAlignment.MiddleLeft;
        chkSelectAll.CheckedChanged += chkSelectAll_CheckedChanged;

        btnKillSelected.Text = "Kill 选中项";
        btnKillSelected.Size = new Size(100, 28);
        btnKillSelected.Anchor = AnchorStyles.None;
        btnKillSelected.Click += btnKillSelected_Click;

        btnKillAllAndEject.Text = "⚡ Kill 全部并弹出";
        btnKillAllAndEject.Size = new Size(150, 28);
        btnKillAllAndEject.Anchor = AnchorStyles.None;
        btnKillAllAndEject.Click += btnKillAllAndEject_Click;

        actionLayout.Controls.Add(chkSelectAll, 0, 0);
        actionLayout.Controls.Add(btnKillSelected, 1, 0);
        actionLayout.Controls.Add(btnKillAllAndEject, 2, 0);
        actionPanel.Controls.Add(actionLayout);

        page.Controls.Add(dgvProcesses);
        page.Controls.Add(bottomSep);
        page.Controls.Add(actionPanel);
        page.Controls.Add(topSep);
        page.Controls.Add(topPanel);
    }

    private void InitializeFileTab(TabPage page)
    {
        // ── 顶部面板 ──────────────────────────────────────────────────────
        Panel topPanel = new()
        {
            Dock = DockStyle.Top,
            Height = 52,
            BackColor = SystemColors.Control
        };

        TableLayoutPanel topLayout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Padding = new Padding(10, 0, 10, 0),
            Margin = new Padding(0)
        };
        topLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // 标签
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));  // 路径输入框（弹性）
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // 浏览按钮
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // 检查按钮

        Label lblFile = new()
        {
            AutoSize = true,
            Text = "文件路径：",
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 6, 0)
        };

        txtFilePath.Margin = new Padding(0, 14, 6, 14);
        txtFilePath.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        txtFilePath.PlaceholderText = "输入完整文件路径，或将文件拖拽至窗口任意位置…";
        txtFilePath.KeyDown += TxtFilePath_KeyDown;

        btnBrowse.Text = "浏览…";
        btnBrowse.Size = new Size(72, 28);
        btnBrowse.Anchor = AnchorStyles.None;
        btnBrowse.Margin = new Padding(0, 0, 6, 0);
        btnBrowse.Click += btnBrowse_Click;

        btnFileScan.Text = "🔍 检查占用";
        btnFileScan.Size = new Size(110, 28);
        btnFileScan.Anchor = AnchorStyles.None;
        btnFileScan.Click += btnFileScan_Click;

        topLayout.Controls.Add(lblFile, 0, 0);
        topLayout.Controls.Add(txtFilePath, 1, 0);
        topLayout.Controls.Add(btnBrowse, 2, 0);
        topLayout.Controls.Add(btnFileScan, 3, 0);
        topPanel.Controls.Add(topLayout);

        // ── 分隔线 ────────────────────────────────────────────────────────
        Panel topSep = new() { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(213, 213, 213) };

        // ── DataGridView ──────────────────────────────────────────────────
        ConfigureProcessGrid(dgvFileProcesses);
        dgvFileProcesses.CurrentCellDirtyStateChanged += dgvFileProcesses_CurrentCellDirtyStateChanged;
        dgvFileProcesses.CellContentClick += dgvFileProcesses_CellContentClick;
        dgvFileProcesses.CellValueChanged += dgvFileProcesses_CellValueChanged;
        AddProcessGridColumns(dgvFileProcesses);

        // ── 底部分隔线 ────────────────────────────────────────────────────
        Panel bottomSep = new() { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(213, 213, 213) };

        // ── 操作栏 ────────────────────────────────────────────────────────
        Panel actionPanel = new() { Dock = DockStyle.Bottom, Height = 44, BackColor = SystemColors.Control };
        TableLayoutPanel actionLayout = BuildActionLayout();

        chkFileSelectAll.AutoSize = false;
        chkFileSelectAll.Text = "全选";
        chkFileSelectAll.Size = new Size(66, 28);
        chkFileSelectAll.Anchor = AnchorStyles.None;
        chkFileSelectAll.TextAlign = ContentAlignment.MiddleRight;
        chkFileSelectAll.CheckAlign = ContentAlignment.MiddleLeft;
        chkFileSelectAll.CheckedChanged += chkFileSelectAll_CheckedChanged;

        btnFileKillSelected.Text = "Kill 选中项";
        btnFileKillSelected.Size = new Size(100, 28);
        btnFileKillSelected.Anchor = AnchorStyles.None;
        btnFileKillSelected.Click += btnFileKillSelected_Click;

        actionLayout.Controls.Add(chkFileSelectAll, 0, 0);
        actionLayout.Controls.Add(btnFileKillSelected, 1, 0);
        // 列 2（Kill 全部并弹出）仅驱动器 Tab 使用，此处留空保持对齐
        actionPanel.Controls.Add(actionLayout);

        page.Controls.Add(dgvFileProcesses);
        page.Controls.Add(bottomSep);
        page.Controls.Add(actionPanel);
        page.Controls.Add(topSep);
        page.Controls.Add(topPanel);
    }

    // 两个 Tab 操作栏共用相同列宽，保证切换时按钮不漂移
    private static TableLayoutPanel BuildActionLayout()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Padding = new Padding(10, 0, 10, 0),
            Margin = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));   // 全选
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));  // Kill 选中项
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166));  // Kill 全部并弹出（文件 Tab 留空）
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // 剩余空间
        return layout;
    }

    private static void ConfigureProcessGrid(DataGridView dgv)
    {
        dgv.Dock = DockStyle.Fill;
        dgv.AllowUserToAddRows = false;
        dgv.AllowUserToDeleteRows = false;
        dgv.AllowUserToResizeRows = false;
        dgv.MultiSelect = false;
        dgv.RowHeadersVisible = false;
        dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgv.AutoGenerateColumns = false;
        dgv.BackgroundColor = SystemColors.Window;
        dgv.BorderStyle = BorderStyle.None;
        dgv.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        dgv.GridColor = Color.FromArgb(230, 230, 230);
        dgv.ColumnHeadersHeight = 32;
        dgv.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        dgv.RowTemplate.Height = 28;
        dgv.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            SelectionBackColor = Color.FromArgb(204, 232, 255),
            SelectionForeColor = Color.Black,
            Padding = new Padding(2, 0, 2, 0)
        };
        dgv.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(247, 247, 247),
            SelectionBackColor = Color.FromArgb(204, 232, 255),
            SelectionForeColor = Color.Black
        };
        dgv.EnableHeadersVisualStyles = false;
        dgv.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(242, 242, 242),
            ForeColor = Color.FromArgb(50, 50, 50),
            SelectionBackColor = Color.FromArgb(242, 242, 242),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0)
        };
        dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
    }

    private static void AddProcessGridColumns(DataGridView dgv)
    {
        dgv.Columns.AddRange(
            new DataGridViewCheckBoxColumn
            {
                Name = ColumnSelected,
                Width = 36,
                HeaderText = string.Empty,
                Resizable = DataGridViewTriState.False
            },
            new DataGridViewTextBoxColumn
            {
                Name = ColumnName,
                HeaderText = "进程名称",
                Width = 160,
                ReadOnly = true
            },
            new DataGridViewTextBoxColumn
            {
                Name = ColumnPid,
                HeaderText = "PID",
                Width = 70,
                ReadOnly = true,
                Resizable = DataGridViewTriState.False,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleRight,
                    Padding = new Padding(0, 0, 8, 0)
                }
            },
            new DataGridViewTextBoxColumn
            {
                Name = ColumnPath,
                HeaderText = "可执行文件路径",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            },
            new DataGridViewButtonColumn
            {
                Name = ColumnAction,
                HeaderText = string.Empty,
                Width = 60,
                Resizable = DataGridViewTriState.False,
                UseColumnTextForButtonValue = true,
                Text = "Kill"
            });
    }

    // ══════════════════════════════════════════════════════════════════════
    // 窗体事件
    // ══════════════════════════════════════════════════════════════════════

    private void MainForm_Load(object? sender, EventArgs e)
    {
        LoadDrives();
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg == WmDeviceChange)
        {
            int wParam = m.WParam.ToInt32();
            if (wParam == DbtDeviceArrival || wParam == DbtDeviceRemoveComplete)
            {
                HandleDeviceChange();
            }
        }
    }

    private void HandleDeviceChange()
    {
        if (_isBusy) return;

        string? previousDriveName = GetSelectedDrive()?.Name;
        List<DriveInfo> drives = DriveHelper.GetEjectableDrives();

        _isLoadingDrives = true;
        cmbDrive.BeginUpdate();
        cmbDrive.Items.Clear();

        if (drives.Count == 0)
        {
            cmbDrive.Items.Add(new DriveOption(null, "未检测到可移动驱动器"));
            cmbDrive.SelectedIndex = 0;
            cmbDrive.EndUpdate();
            _isLoadingDrives = false;
            ClearDriveProcesses();
            SetStatus("未检测到可移动驱动器");
            RefreshActionState();
            return;
        }

        foreach (DriveInfo drive in drives)
            cmbDrive.Items.Add(new DriveOption(drive, DriveHelper.FormatDriveLabel(drive)));

        bool restored = false;
        if (previousDriveName is not null)
        {
            for (int i = 0; i < cmbDrive.Items.Count; i++)
            {
                if (cmbDrive.Items[i] is DriveOption opt && opt.Drive?.Name == previousDriveName)
                {
                    cmbDrive.SelectedIndex = i;
                    restored = true;
                    break;
                }
            }
        }

        cmbDrive.EndUpdate();
        _isLoadingDrives = false;

        if (!restored)
        {
            cmbDrive.SelectedIndex = 0;
            ClearDriveProcesses();
            SetStatus("驱动器已移除，请重新选择");
        }
        else
        {
            SetStatus("ℹ️ 检测到驱动器变化，列表已更新");
        }

        RefreshActionState();
    }

    private void LoadDrives(bool preserveStatus = false)
    {
        List<DriveInfo> drives = DriveHelper.GetEjectableDrives();

        _isLoadingDrives = true;
        cmbDrive.BeginUpdate();
        cmbDrive.Items.Clear();

        if (drives.Count == 0)
        {
            ClearDriveProcesses();
            cmbDrive.Items.Add(new DriveOption(null, "未检测到可移动驱动器"));
            cmbDrive.SelectedIndex = 0;
            cmbDrive.EndUpdate();
            _isLoadingDrives = false;
            if (!preserveStatus) SetStatus("未检测到可移动驱动器");
            RefreshActionState();
            return;
        }

        foreach (DriveInfo drive in drives)
            cmbDrive.Items.Add(new DriveOption(drive, DriveHelper.FormatDriveLabel(drive)));

        cmbDrive.SelectedIndex = 0;
        cmbDrive.EndUpdate();
        _isLoadingDrives = false;
        ClearDriveProcesses();
        if (!preserveStatus) SetStatus("请选择驱动器并点击扫描");
        RefreshActionState();
    }

    // ══════════════════════════════════════════════════════════════════════
    // 驱动器 Tab 事件
    // ══════════════════════════════════════════════════════════════════════

    private void CmbDrive_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (!_isLoadingDrives)
        {
            ClearDriveProcesses();
            SetStatus("请选择驱动器并点击扫描");
        }
        RefreshActionState();
    }

    private async void btnScan_Click(object? sender, EventArgs e)
    {
        DriveInfo? drive = GetSelectedDrive();
        if (drive is null) { SetStatus("请先选择驱动器"); return; }

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        CancellationToken token = _scanCts.Token;

        ClearDriveProcesses();
        SetUiBusy(true);
        SetStatus("⏳ 正在扫描…");

        try
        {
            string drivePath = drive.RootDirectory.FullName;
            List<LockingProcess> processes = await Task.Run(
                () => RestartManagerScanner.ScanDrive(drivePath), token);

            token.ThrowIfCancellationRequested();
            PopulateDriveProcesses(processes);
            SetStatus(processes.Count == 0
                ? "✅ 未发现占用进程，可以安全弹出"
                : $"⚠️ 找到 {processes.Count} 个占用进程");
        }
        catch (OperationCanceledException) { SetStatus("已取消扫描"); }
        catch (DirectoryNotFoundException) { SetStatus("❌ 驱动器已断开"); }
        catch (Exception ex) { SetStatus($"❌ 扫描出错：{ex.Message}"); }
        finally { SetUiBusy(false); }
    }

    private async void btnEject_Click(object? sender, EventArgs e)
    {
        char? letter = GetSelectedDriveLetter();
        if (letter is null) { SetStatus("请先选择驱动器"); return; }

        SetUiBusy(true);
        try
        {
            bool ejected = await Task.Run(() => DriveEjector.Eject(letter.Value));
            if (ejected)
            {
                ClearDriveProcesses();
                LoadDrives(preserveStatus: true);
                SetStatus("✅ 驱动器已安全弹出");
            }
            else
            {
                SetStatus("❌ 弹出失败，请确认所有占用已清除");
            }
        }
        finally { SetUiBusy(false); }
    }

    private async void btnKillSelected_Click(object? sender, EventArgs e)
    {
        List<(DataGridViewRow Row, int Pid)> rows = GetCheckedDriveRows();
        if (rows.Count == 0) { SetStatus("请先勾选要终止的进程"); return; }

        SetUiBusy(true);
        try
        {
            List<int> pids = rows.Select(r => r.Pid).ToList();
            List<ProcessKiller.KillResult> results = await Task.Run(() => ProcessKiller.KillAll(pids));

            int ok = 0;
            string? lastFail = null;

            for (int i = results.Count - 1; i >= 0; i--)
            {
                if (results[i].Success) { dgvProcesses.Rows.Remove(rows[i].Row); ok++; }
                else lastFail = results[i].Message;
            }

            UpdateDriveSelectAll();
            RefreshActionState();

            SetStatus(lastFail is null
                ? $"✅ 已终止 {ok} 个进程"
                : ok > 0
                    ? $"⚠️ 已终止 {ok} 个进程，部分失败：{lastFail}"
                    : $"❌ {lastFail}");

            if (dgvProcesses.Rows.Count == 0)
                SetStatus("✅ 未发现占用进程，可以安全弹出");
        }
        finally { SetUiBusy(false); }
    }

    private async void btnKillAllAndEject_Click(object? sender, EventArgs e)
    {
        char? letter = GetSelectedDriveLetter();
        if (letter is null) { SetStatus("请先选择驱动器"); return; }

        SetUiBusy(true);
        try
        {
            List<(DataGridViewRow Row, int Pid)> allRows = GetAllDriveRows();
            if (allRows.Count > 0)
            {
                List<int> pids = allRows.Select(r => r.Pid).ToList();
                List<ProcessKiller.KillResult> results = await Task.Run(() => ProcessKiller.KillAll(pids));
                string? failMsg = null;

                for (int i = results.Count - 1; i >= 0; i--)
                {
                    if (results[i].Success) dgvProcesses.Rows.Remove(allRows[i].Row);
                    else failMsg ??= results[i].Message;
                }

                UpdateDriveSelectAll();
                RefreshActionState();

                if (failMsg is not null)
                {
                    SetStatus($"❌ {failMsg ?? "存在未能终止的进程，未执行弹出"}");
                    return;
                }
            }

            bool ejected = await Task.Run(() => DriveEjector.Eject(letter.Value));
            if (ejected)
            {
                ClearDriveProcesses();
                LoadDrives(preserveStatus: true);
                SetStatus("✅ 驱动器已安全弹出");
            }
            else
            {
                SetStatus("❌ 弹出失败");
            }
        }
        finally { SetUiBusy(false); }
    }

    private async void dgvProcesses_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || dgvProcesses.Columns[e.ColumnIndex].Name != ColumnAction) return;

        DataGridViewRow row = dgvProcesses.Rows[e.RowIndex];
        int pid = GetRowPid(row);

        SetUiBusy(true);
        try
        {
            ProcessKiller.KillResult result = await Task.Run(() => ProcessKiller.Kill(pid));
            if (result.Success)
            {
                dgvProcesses.Rows.Remove(row);
                UpdateDriveSelectAll();
                RefreshActionState();
                SetStatus(dgvProcesses.Rows.Count == 0
                    ? "✅ 未发现占用进程，可以安全弹出"
                    : $"✅ {result.Message}");
            }
            else
            {
                SetStatus($"❌ {result.Message}");
            }
        }
        finally { SetUiBusy(false); }
    }

    private void dgvProcesses_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (dgvProcesses.IsCurrentCellDirty)
            dgvProcesses.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    private void dgvProcesses_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 && dgvProcesses.Columns[e.ColumnIndex].Name == ColumnSelected)
        {
            UpdateDriveSelectAll();
            RefreshActionState();
        }
    }

    private void chkSelectAll_CheckedChanged(object? sender, EventArgs e)
    {
        if (_suppressSelectAllChange) return;
        foreach (DataGridViewRow row in dgvProcesses.Rows)
            row.Cells[ColumnSelected].Value = chkSelectAll.Checked;
        RefreshActionState();
    }

    // ══════════════════════════════════════════════════════════════════════
    // 文件检查 Tab 事件
    // ══════════════════════════════════════════════════════════════════════

    private void btnBrowse_Click(object? sender, EventArgs e)
    {
        using OpenFileDialog dlg = new()
        {
            Title = "选择要检查占用的文件",
            Filter = "所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            txtFilePath.Text = dlg.FileName;
        }
    }

    // ── 窗体级拖拽（任意位置拖入文件即触发检查）────────────────────────────

    private void EnableFormWideFileDrop()
    {
        SetAllowDropRecursive(this);
    }

    private void SetAllowDropRecursive(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            child.AllowDrop = true;
            child.DragEnter += FormWide_DragEnter;
            child.DragDrop += FormWide_DragDrop;
            SetAllowDropRecursive(child);
        }
    }

    private static void FormWide_DragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void FormWide_DragDrop(object? sender, DragEventArgs e)
    {
        if (_isBusy)
        {
            SetStatus("⚠️ 当前正在执行操作，请稍后再试");
            return;
        }

        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        string filePath = files[0];

        if (!File.Exists(filePath))
        {
            SetStatus($"❌ 文件不存在：{filePath}");
            return;
        }

        tabMain.SelectedIndex = 1;
        txtFilePath.Text = filePath;
        btnFileScan_Click(null, EventArgs.Empty);
    }

    private void TxtFilePath_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            btnFileScan_Click(sender, EventArgs.Empty);
        }
    }

    private async void btnFileScan_Click(object? sender, EventArgs e)
    {
        string path = txtFilePath.Text.Trim();

        if (string.IsNullOrEmpty(path))
        {
            SetStatus("请输入或选择一个文件路径");
            return;
        }

        if (!File.Exists(path))
        {
            SetStatus($"❌ 文件不存在：{path}");
            return;
        }

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        CancellationToken token = _scanCts.Token;

        ClearFileProcesses();
        SetUiBusy(true);
        SetStatus("⏳ 正在检查文件占用…");

        try
        {
            List<LockingProcess> processes = await Task.Run(
                () => RestartManagerScanner.ScanFile(path), token);

            token.ThrowIfCancellationRequested();
            PopulateFileProcesses(processes);
            SetStatus(processes.Count == 0
                ? "✅ 该文件当前未被任何进程占用"
                : $"⚠️ 找到 {processes.Count} 个占用该文件的进程");
        }
        catch (OperationCanceledException) { SetStatus("已取消检查"); }
        catch (Exception ex) { SetStatus($"❌ 检查出错：{ex.Message}"); }
        finally { SetUiBusy(false); }
    }

    private async void btnFileKillSelected_Click(object? sender, EventArgs e)
    {
        List<(DataGridViewRow Row, int Pid)> rows = GetCheckedFileRows();
        if (rows.Count == 0) { SetStatus("请先勾选要终止的进程"); return; }

        SetUiBusy(true);
        try
        {
            List<int> pids = rows.Select(r => r.Pid).ToList();
            List<ProcessKiller.KillResult> results = await Task.Run(() => ProcessKiller.KillAll(pids));

            int ok = 0;
            string? lastFail = null;

            for (int i = results.Count - 1; i >= 0; i--)
            {
                if (results[i].Success) { dgvFileProcesses.Rows.Remove(rows[i].Row); ok++; }
                else lastFail = results[i].Message;
            }

            UpdateFileSelectAll();
            RefreshActionState();

            SetStatus(lastFail is null
                ? $"✅ 已终止 {ok} 个进程"
                : ok > 0
                    ? $"⚠️ 已终止 {ok} 个进程，部分失败：{lastFail}"
                    : $"❌ {lastFail}");

            if (dgvFileProcesses.Rows.Count == 0)
                SetStatus("✅ 该文件当前未被任何进程占用");
        }
        finally { SetUiBusy(false); }
    }

    private async void dgvFileProcesses_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || dgvFileProcesses.Columns[e.ColumnIndex].Name != ColumnAction) return;

        DataGridViewRow row = dgvFileProcesses.Rows[e.RowIndex];
        int pid = GetRowPid(row);

        SetUiBusy(true);
        try
        {
            ProcessKiller.KillResult result = await Task.Run(() => ProcessKiller.Kill(pid));
            if (result.Success)
            {
                dgvFileProcesses.Rows.Remove(row);
                UpdateFileSelectAll();
                RefreshActionState();
                SetStatus(dgvFileProcesses.Rows.Count == 0
                    ? "✅ 该文件当前未被任何进程占用"
                    : $"✅ {result.Message}");
            }
            else
            {
                SetStatus($"❌ {result.Message}");
            }
        }
        finally { SetUiBusy(false); }
    }

    private void dgvFileProcesses_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (dgvFileProcesses.IsCurrentCellDirty)
            dgvFileProcesses.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    private void dgvFileProcesses_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 && dgvFileProcesses.Columns[e.ColumnIndex].Name == ColumnSelected)
        {
            UpdateFileSelectAll();
            RefreshActionState();
        }
    }

    private void chkFileSelectAll_CheckedChanged(object? sender, EventArgs e)
    {
        if (_suppressFileSelectAllChange) return;
        foreach (DataGridViewRow row in dgvFileProcesses.Rows)
            row.Cells[ColumnSelected].Value = chkFileSelectAll.Checked;
        RefreshActionState();
    }

    // ══════════════════════════════════════════════════════════════════════
    // 状态与 UI 辅助
    // ══════════════════════════════════════════════════════════════════════

    private void RefreshStatusOnTabSwitch(TabControl tabMain)
    {
        if (tabMain.SelectedIndex == 0)
            SetStatus(dgvProcesses.Rows.Count > 0
                ? $"⚠️ 找到 {dgvProcesses.Rows.Count} 个占用进程"
                : "ℹ️ 请选择驱动器并点击扫描");
        else
            SetStatus(dgvFileProcesses.Rows.Count > 0
                ? $"⚠️ 找到 {dgvFileProcesses.Rows.Count} 个占用该文件的进程"
                : "ℹ️ 请选择或输入文件路径，然后点击检查占用");
    }

    private void PopulateDriveProcesses(IEnumerable<LockingProcess> processes)
    {
        foreach (LockingProcess p in processes)
            dgvProcesses.Rows.Add(false, p.Name, p.Pid, p.ExePath, "Kill");
        UpdateDriveSelectAll();
        RefreshActionState();
    }

    private void ClearDriveProcesses()
    {
        dgvProcesses.Rows.Clear();
        UpdateDriveSelectAll();
        RefreshActionState();
    }

    private void PopulateFileProcesses(IEnumerable<LockingProcess> processes)
    {
        foreach (LockingProcess p in processes)
            dgvFileProcesses.Rows.Add(false, p.Name, p.Pid, p.ExePath, "Kill");
        UpdateFileSelectAll();
        RefreshActionState();
    }

    private void ClearFileProcesses()
    {
        dgvFileProcesses.Rows.Clear();
        UpdateFileSelectAll();
        RefreshActionState();
    }

    private void SetUiBusy(bool isBusy)
    {
        _isBusy = isBusy;
        RefreshActionState();
    }

    private void RefreshActionState()
    {
        bool hasDrive = GetSelectedDrive() is not null;
        bool hasDriveRows = dgvProcesses.Rows.Count > 0;
        bool hasDriveChecked = GetCheckedDriveRows().Count > 0;

        bool hasFileRows = dgvFileProcesses.Rows.Count > 0;
        bool hasFileChecked = GetCheckedFileRows().Count > 0;

        // 驱动器 Tab
        cmbDrive.Enabled = !_isBusy && hasDrive;
        btnScan.Enabled = !_isBusy && hasDrive;
        btnEject.Enabled = !_isBusy && hasDrive;
        chkSelectAll.Enabled = !_isBusy && hasDriveRows;
        btnKillSelected.Enabled = !_isBusy && hasDriveChecked;
        btnKillAllAndEject.Enabled = !_isBusy && hasDrive;
        dgvProcesses.Enabled = !_isBusy;

        // 文件检查 Tab
        txtFilePath.Enabled = !_isBusy;
        btnBrowse.Enabled = !_isBusy;
        btnFileScan.Enabled = !_isBusy;
        chkFileSelectAll.Enabled = !_isBusy && hasFileRows;
        btnFileKillSelected.Enabled = !_isBusy && hasFileChecked;
        dgvFileProcesses.Enabled = !_isBusy;
    }

    private void UpdateDriveSelectAll()
    {
        _suppressSelectAllChange = true;
        bool hasRows = dgvProcesses.Rows.Count > 0;
        bool allChecked = hasRows && dgvProcesses.Rows
            .Cast<DataGridViewRow>()
            .All(r => r.Cells[ColumnSelected].Value as bool? == true);
        chkSelectAll.Checked = allChecked;
        _suppressSelectAllChange = false;
    }

    private void UpdateFileSelectAll()
    {
        _suppressFileSelectAllChange = true;
        bool hasRows = dgvFileProcesses.Rows.Count > 0;
        bool allChecked = hasRows && dgvFileProcesses.Rows
            .Cast<DataGridViewRow>()
            .All(r => r.Cells[ColumnSelected].Value as bool? == true);
        chkFileSelectAll.Checked = allChecked;
        _suppressFileSelectAllChange = false;
    }

    private List<(DataGridViewRow Row, int Pid)> GetCheckedDriveRows() =>
        dgvProcesses.Rows
            .Cast<DataGridViewRow>()
            .Where(r => r.Cells[ColumnSelected].Value as bool? == true)
            .Select(r => (r, GetRowPid(r)))
            .ToList();

    private List<(DataGridViewRow Row, int Pid)> GetAllDriveRows() =>
        dgvProcesses.Rows
            .Cast<DataGridViewRow>()
            .Select(r => (r, GetRowPid(r)))
            .ToList();

    private List<(DataGridViewRow Row, int Pid)> GetCheckedFileRows() =>
        dgvFileProcesses.Rows
            .Cast<DataGridViewRow>()
            .Where(r => r.Cells[ColumnSelected].Value as bool? == true)
            .Select(r => (r, GetRowPid(r)))
            .ToList();

    private static int GetRowPid(DataGridViewRow row) =>
        Convert.ToInt32(row.Cells[ColumnPid].Value, System.Globalization.CultureInfo.InvariantCulture);

    private DriveInfo? GetSelectedDrive() =>
        (cmbDrive.SelectedItem as DriveOption)?.Drive;

    private char? GetSelectedDriveLetter() =>
        GetSelectedDrive()?.Name.FirstOrDefault();

    private void SetStatus(string message)
    {
        lblStatus.Text = message;
    }

    private static string GetDisplayVersion()
    {
        Version? ver = Assembly.GetExecutingAssembly().GetName().Version;
        return ver is null ? "1.0" : $"{ver.Major}.{ver.Minor}";
    }

    private sealed record DriveOption(DriveInfo? Drive, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }
}
