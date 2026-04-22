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

    private readonly ComboBox cmbDrive;
    private readonly Button btnScan;
    private readonly Button btnEject;
    private readonly DataGridView dgvProcesses;
    private readonly CheckBox chkSelectAll;
    private readonly Button btnKillSelected;
    private readonly Button btnKillAllAndEject;
    private readonly ToolStripStatusLabel lblStatus;

    private CancellationTokenSource? _scanCancellationTokenSource;
    private bool _suppressSelectAllChange;
    private bool _isLoadingDrives;
    private bool _isBusy;

    public MainForm()
    {
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Text = $"DriveUnlocker v{GetDisplayVersion()}";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 420);
        ClientSize = new Size(900, 540);

        cmbDrive = new ComboBox();
        btnScan = new Button();
        btnEject = new Button();
        dgvProcesses = new DataGridView();
        chkSelectAll = new CheckBox();
        btnKillSelected = new Button();
        btnKillAllAndEject = new Button();
        lblStatus = new ToolStripStatusLabel();

        InitializeLayout();
        Load += MainForm_Load;
        FormClosing += MainForm_FormClosing;
    }

    private void InitializeLayout()
    {
        SuspendLayout();

        // ── Status strip ──────────────────────────────────────────────────────
        StatusStrip statusStrip = new() { Dock = DockStyle.Bottom, SizingGrip = false };
        lblStatus.Name = "lblStatus";
        lblStatus.Text = "ℹ️ 请选择驱动器并点击扫描";
        statusStrip.Items.Add(lblStatus);

        // ── Bottom separator (between grid and action panel) ──────────────────
        Panel bottomSeparator = new()
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = Color.FromArgb(213, 213, 213)
        };

        // ── Action panel (FlowLayoutPanel for proper spacing) ─────────────────
        FlowLayoutPanel actionPanel = new()
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(10, 8, 10, 8),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = SystemColors.Control
        };

        chkSelectAll.AutoSize = false;
        chkSelectAll.Text = "全选";
        chkSelectAll.Size = new Size(66, 28);
        chkSelectAll.Margin = new Padding(0, 0, 6, 0);
        chkSelectAll.TextAlign = ContentAlignment.MiddleRight;
        chkSelectAll.CheckAlign = ContentAlignment.MiddleLeft;
        chkSelectAll.CheckedChanged += chkSelectAll_CheckedChanged;

        btnKillSelected.Text = "Kill 选中项";
        btnKillSelected.Size = new Size(100, 28);
        btnKillSelected.Margin = new Padding(0, 0, 6, 0);
        btnKillSelected.Click += btnKillSelected_Click;

        btnKillAllAndEject.Text = "⚡ Kill 全部并弹出";
        btnKillAllAndEject.Size = new Size(150, 28);
        btnKillAllAndEject.Margin = new Padding(0, 0, 0, 0);
        btnKillAllAndEject.Click += btnKillAllAndEject_Click;

        actionPanel.Controls.AddRange(new Control[] { chkSelectAll, btnKillSelected, btnKillAllAndEject });

        // ── DataGridView ──────────────────────────────────────────────────────
        dgvProcesses.Name = "dgvProcesses";
        dgvProcesses.Dock = DockStyle.Fill;
        dgvProcesses.AllowUserToAddRows = false;
        dgvProcesses.AllowUserToDeleteRows = false;
        dgvProcesses.AllowUserToResizeRows = false;
        dgvProcesses.MultiSelect = false;
        dgvProcesses.RowHeadersVisible = false;
        dgvProcesses.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgvProcesses.AutoGenerateColumns = false;
        dgvProcesses.BackgroundColor = SystemColors.Window;
        dgvProcesses.BorderStyle = BorderStyle.None;
        dgvProcesses.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        dgvProcesses.GridColor = Color.FromArgb(230, 230, 230);
        dgvProcesses.ColumnHeadersHeight = 32;
        dgvProcesses.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        dgvProcesses.RowTemplate.Height = 28;
        dgvProcesses.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            SelectionBackColor = Color.FromArgb(204, 232, 255),
            SelectionForeColor = Color.Black,
            Padding = new Padding(2, 0, 2, 0)
        };
        dgvProcesses.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(247, 247, 247),
            SelectionBackColor = Color.FromArgb(204, 232, 255),
            SelectionForeColor = Color.Black
        };
        dgvProcesses.EnableHeadersVisualStyles = false;
        dgvProcesses.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(242, 242, 242),
            ForeColor = Color.FromArgb(50, 50, 50),
            SelectionBackColor = Color.FromArgb(242, 242, 242),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0)
        };
        dgvProcesses.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        dgvProcesses.CurrentCellDirtyStateChanged += dgvProcesses_CurrentCellDirtyStateChanged;
        dgvProcesses.CellContentClick += dgvProcesses_CellContentClick;
        dgvProcesses.CellValueChanged += dgvProcesses_CellValueChanged;
        InitializeProcessGridColumns();

        // ── Top separator (between top panel and grid) ────────────────────────
        Panel topSeparator = new()
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Color.FromArgb(213, 213, 213)
        };

        // ── Top panel ─────────────────────────────────────────────────────────
        Panel topPanel = new()
        {
            Dock = DockStyle.Top,
            Height = 52,
            BackColor = SystemColors.Control
        };

        Label lblDrive = new()
        {
            AutoSize = false,
            Location = new Point(16, 15),
            Size = new Size(64, 22),
            Text = "驱动器：",
            TextAlign = ContentAlignment.MiddleLeft
        };

        cmbDrive.Name = "cmbDrive";
        cmbDrive.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbDrive.FormattingEnabled = true;
        cmbDrive.Location = new Point(84, 15);
        cmbDrive.Size = new Size(280, 23);
        cmbDrive.SelectedIndexChanged += CmbDrive_SelectedIndexChanged;

        btnScan.Name = "btnScan";
        btnScan.Text = "🔍 扫描占用";
        btnScan.Location = new Point(376, 12);
        btnScan.Size = new Size(110, 28);
        btnScan.Click += btnScan_Click;

        btnEject.Name = "btnEject";
        btnEject.Text = "⏏ 弹出驱动器";
        btnEject.Location = new Point(494, 12);
        btnEject.Size = new Size(120, 28);
        btnEject.Enabled = false;
        btnEject.Click += btnEject_Click;

        topPanel.Controls.AddRange(new Control[] { lblDrive, cmbDrive, btnScan, btnEject });

        // ── Build control tree ────────────────────────────────────────────────
        // Dock layout: later-added controls occupy the outer edge for the same side.
        // Order here: outer-to-inner is statusStrip → actionPanel → bottomSeparator
        //             and topPanel → topSeparator, with dgvProcesses filling the rest.
        Controls.Add(dgvProcesses);       // Fill  (processed last)
        Controls.Add(bottomSeparator);    // Bottom (just above actionPanel)
        Controls.Add(actionPanel);        // Bottom
        Controls.Add(statusStrip);        // Bottom (outermost, at very bottom)
        Controls.Add(topSeparator);       // Top    (just below topPanel)
        Controls.Add(topPanel);           // Top    (outermost, at very top)

        ResumeLayout(performLayout: false);
        PerformLayout();

        RefreshActionState();
    }

    private void InitializeProcessGridColumns()
    {
        DataGridViewCheckBoxColumn selectedColumn = new()
        {
            Name = ColumnSelected,
            Width = 36,
            HeaderText = string.Empty,
            Resizable = DataGridViewTriState.False
        };

        DataGridViewTextBoxColumn nameColumn = new()
        {
            Name = ColumnName,
            HeaderText = "进程名称",
            Width = 160,
            ReadOnly = true
        };

        DataGridViewTextBoxColumn pidColumn = new()
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
        };

        DataGridViewTextBoxColumn pathColumn = new()
        {
            Name = ColumnPath,
            HeaderText = "可执行文件路径",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        };

        DataGridViewButtonColumn actionColumn = new()
        {
            Name = ColumnAction,
            HeaderText = string.Empty,
            Width = 60,
            Resizable = DataGridViewTriState.False,
            UseColumnTextForButtonValue = true,
            Text = "Kill"
        };

        dgvProcesses.Columns.AddRange(selectedColumn, nameColumn, pidColumn, pathColumn, actionColumn);
    }

    private void MainForm_Load(object? sender, EventArgs e)
    {
        LoadDrives();
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _scanCancellationTokenSource?.Cancel();
        _scanCancellationTokenSource?.Dispose();
        _scanCancellationTokenSource = null;
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
        // Don't interrupt an ongoing scan or kill operation
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
            ClearProcesses();
            SetStatus("未检测到可移动驱动器");
            RefreshActionState();
            return;
        }

        foreach (DriveInfo drive in drives)
        {
            cmbDrive.Items.Add(new DriveOption(drive, DriveHelper.FormatDriveLabel(drive)));
        }

        // Try to restore the previously selected drive
        bool previousDriveRestored = false;
        if (previousDriveName is not null)
        {
            for (int i = 0; i < cmbDrive.Items.Count; i++)
            {
                if (cmbDrive.Items[i] is DriveOption opt && opt.Drive?.Name == previousDriveName)
                {
                    cmbDrive.SelectedIndex = i;
                    previousDriveRestored = true;
                    break;
                }
            }
        }

        cmbDrive.EndUpdate();
        _isLoadingDrives = false;

        if (!previousDriveRestored)
        {
            // Previously selected drive is gone — reset state
            cmbDrive.SelectedIndex = 0;
            ClearProcesses();
            SetStatus("驱动器已移除，请重新选择驱动器");
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
            ClearProcesses();
            cmbDrive.Items.Add(new DriveOption(null, "未检测到可移动驱动器"));
            cmbDrive.SelectedIndex = 0;
            cmbDrive.EndUpdate();
            _isLoadingDrives = false;
            if (!preserveStatus)
            {
                SetStatus("未检测到可移动驱动器");
            }
            RefreshActionState();
            return;
        }

        foreach (DriveInfo drive in drives)
        {
            cmbDrive.Items.Add(new DriveOption(drive, DriveHelper.FormatDriveLabel(drive)));
        }

        cmbDrive.SelectedIndex = 0;
        cmbDrive.EndUpdate();
        _isLoadingDrives = false;
        ClearProcesses();
        if (!preserveStatus)
        {
            SetStatus("请选择驱动器并点击扫描");
        }
        RefreshActionState();
    }

    private async void btnScan_Click(object? sender, EventArgs e)
    {
        DriveInfo? selectedDrive = GetSelectedDrive();
        if (selectedDrive is null)
        {
            SetStatus("请先选择驱动器");
            return;
        }

        _scanCancellationTokenSource?.Cancel();
        _scanCancellationTokenSource?.Dispose();
        _scanCancellationTokenSource = new CancellationTokenSource();
        CancellationToken token = _scanCancellationTokenSource.Token;

        ClearProcesses();
        SetUiBusy(true);
        SetStatus("⏳ 正在扫描…");

        try
        {
            string drivePath = selectedDrive.RootDirectory.FullName;
            List<LockingProcess> processes = await Task.Run(
                () => RestartManagerScanner.ScanDrive(drivePath),
                token);

            token.ThrowIfCancellationRequested();
            PopulateProcesses(processes);
            if (processes.Count == 0)
            {
                SetStatus("✅ 未发现占用进程，可以安全弹出");
            }
            else
            {
                SetStatus($"⚠️ 找到 {processes.Count} 个占用进程");
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("已取消上一次扫描");
        }
        catch (DirectoryNotFoundException)
        {
            SetStatus("❌ 驱动器已断开");
        }
        catch (Exception ex)
        {
            SetStatus($"❌ 扫描出错：{ex.Message}");
        }
        finally
        {
            SetUiBusy(false);
        }
    }

    private async void btnEject_Click(object? sender, EventArgs e)
    {
        char? driveLetter = GetSelectedDriveLetter();
        if (driveLetter is null)
        {
            SetStatus("请先选择驱动器");
            return;
        }

        SetUiBusy(true);

        try
        {
            bool ejected = await Task.Run(() => DriveEjector.Eject(driveLetter.Value));

            if (ejected)
            {
                ClearProcesses();
                LoadDrives(preserveStatus: true);
                SetStatus("✅ 驱动器已安全弹出");
            }
            else
            {
                SetStatus("❌ 弹出失败，请确认所有占用已清除");
            }
        }
        finally
        {
            SetUiBusy(false);
        }
    }

    private async void btnKillSelected_Click(object? sender, EventArgs e)
    {
        List<(DataGridViewRow Row, int Pid)> selectedRows = GetCheckedProcessRows();
        if (selectedRows.Count == 0)
        {
            SetStatus("请先勾选要终止的进程");
            return;
        }

        SetUiBusy(true);

        try
        {
            List<int> pids = selectedRows.Select(item => item.Pid).ToList();
            List<ProcessKiller.KillResult> results = await Task.Run(() => ProcessKiller.KillAll(pids));

            int successCount = 0;
            string? lastFailureMessage = null;

            for (int index = results.Count - 1; index >= 0; index--)
            {
                ProcessKiller.KillResult result = results[index];
                if (result.Success)
                {
                    dgvProcesses.Rows.Remove(selectedRows[index].Row);
                    successCount++;
                }
                else
                {
                    lastFailureMessage = result.Message;
                }
            }

            UpdateSelectAllState();
            RefreshActionState();

            if (lastFailureMessage is null)
            {
                SetStatus($"✅ 已终止 {successCount} 个进程");
            }
            else if (successCount > 0)
            {
                SetStatus($"⚠️ 已终止 {successCount} 个进程，部分失败：{lastFailureMessage}");
            }
            else
            {
                SetStatus($"❌ {lastFailureMessage}");
            }

            if (dgvProcesses.Rows.Count == 0)
            {
                SetStatus("✅ 未发现占用进程，可以安全弹出");
            }
        }
        finally
        {
            SetUiBusy(false);
        }
    }

    private async void btnKillAllAndEject_Click(object? sender, EventArgs e)
    {
        char? driveLetter = GetSelectedDriveLetter();
        if (driveLetter is null)
        {
            SetStatus("请先选择驱动器");
            return;
        }

        SetUiBusy(true);

        try
        {
            List<(DataGridViewRow Row, int Pid)> allRows = GetAllProcessRows();
            if (allRows.Count > 0)
            {
                List<int> pids = allRows.Select(item => item.Pid).ToList();
                List<ProcessKiller.KillResult> results = await Task.Run(() => ProcessKiller.KillAll(pids));

                bool allSucceeded = true;
                string? failureMessage = null;

                for (int index = results.Count - 1; index >= 0; index--)
                {
                    ProcessKiller.KillResult result = results[index];
                    if (result.Success)
                    {
                        dgvProcesses.Rows.Remove(allRows[index].Row);
                    }
                    else
                    {
                        allSucceeded = false;
                        failureMessage ??= result.Message;
                    }
                }

                UpdateSelectAllState();
                RefreshActionState();

                if (!allSucceeded)
                {
                    SetStatus($"❌ {failureMessage ?? "存在未能终止的进程，未执行弹出"}");
                    return;
                }
            }

            bool ejected = await Task.Run(() => DriveEjector.Eject(driveLetter.Value));

            if (ejected)
            {
                ClearProcesses();
                LoadDrives(preserveStatus: true);
                SetStatus("✅ 驱动器已安全弹出");
            }
            else
            {
                SetStatus("❌ 弹出失败");
            }
        }
        finally
        {
            SetUiBusy(false);
        }
    }

    private async void dgvProcesses_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (dgvProcesses.Columns[e.ColumnIndex].Name != ColumnAction)
        {
            return;
        }

        DataGridViewRow row = dgvProcesses.Rows[e.RowIndex];
        int pid = GetRowPid(row);

        SetUiBusy(true);

        try
        {
            ProcessKiller.KillResult result = await Task.Run(() => ProcessKiller.Kill(pid));

            if (result.Success)
            {
                dgvProcesses.Rows.Remove(row);
                UpdateSelectAllState();
                RefreshActionState();

                if (dgvProcesses.Rows.Count == 0)
                {
                    SetStatus("✅ 未发现占用进程，可以安全弹出");
                }
                else
                {
                    SetStatus($"✅ {result.Message}");
                }
            }
            else
            {
                SetStatus($"❌ {result.Message}");
            }
        }
        finally
        {
            SetUiBusy(false);
        }
    }

    private void dgvProcesses_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (dgvProcesses.IsCurrentCellDirty)
        {
            dgvProcesses.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    private void dgvProcesses_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 && dgvProcesses.Columns[e.ColumnIndex].Name == ColumnSelected)
        {
            UpdateSelectAllState();
            RefreshActionState();
        }
    }

    private void chkSelectAll_CheckedChanged(object? sender, EventArgs e)
    {
        if (_suppressSelectAllChange)
        {
            return;
        }

        foreach (DataGridViewRow row in dgvProcesses.Rows)
        {
            row.Cells[ColumnSelected].Value = chkSelectAll.Checked;
        }

        RefreshActionState();
    }

    private void CmbDrive_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (!_isLoadingDrives)
        {
                ClearProcesses();
            SetStatus("请选择驱动器并点击扫描");
        }

        RefreshActionState();
    }

    private void PopulateProcesses(IEnumerable<LockingProcess> processes)
    {
        foreach (LockingProcess process in processes)
        {
            dgvProcesses.Rows.Add(false, process.Name, process.Pid, process.ExePath, "Kill");
        }

        UpdateSelectAllState();
        RefreshActionState();
    }

    private void ClearProcesses()
    {
        dgvProcesses.Rows.Clear();
        UpdateSelectAllState();
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
        bool hasRows = dgvProcesses.Rows.Count > 0;
        bool hasCheckedRows = GetCheckedProcessRows().Count > 0;

        cmbDrive.Enabled = !_isBusy && hasDrive;
        btnScan.Enabled = !_isBusy && hasDrive;
        btnEject.Enabled = !_isBusy && hasDrive;
        chkSelectAll.Enabled = !_isBusy && hasRows;
        btnKillSelected.Enabled = !_isBusy && hasCheckedRows;
        btnKillAllAndEject.Enabled = !_isBusy && hasDrive;
        dgvProcesses.Enabled = !_isBusy;
    }

    private void UpdateSelectAllState()
    {
        _suppressSelectAllChange = true;

        bool hasRows = dgvProcesses.Rows.Count > 0;
        bool allChecked = hasRows && dgvProcesses.Rows
            .Cast<DataGridViewRow>()
            .All(row => row.Cells[ColumnSelected].Value as bool? == true);

        chkSelectAll.Checked = allChecked;

        _suppressSelectAllChange = false;
    }

    private List<(DataGridViewRow Row, int Pid)> GetCheckedProcessRows()
    {
        return dgvProcesses.Rows
            .Cast<DataGridViewRow>()
            .Where(row => row.Cells[ColumnSelected].Value as bool? == true)
            .Select(row => (row, GetRowPid(row)))
            .ToList();
    }

    private List<(DataGridViewRow Row, int Pid)> GetAllProcessRows()
    {
        return dgvProcesses.Rows
            .Cast<DataGridViewRow>()
            .Select(row => (row, GetRowPid(row)))
            .ToList();
    }

    private int GetRowPid(DataGridViewRow row)
    {
        return Convert.ToInt32(row.Cells[ColumnPid].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private DriveInfo? GetSelectedDrive()
    {
        return (cmbDrive.SelectedItem as DriveOption)?.Drive;
    }

    private char? GetSelectedDriveLetter()
    {
        return GetSelectedDrive()?.Name.FirstOrDefault();
    }

    private void SetStatus(string message)
    {
        lblStatus.Text = message;
    }

    private static string GetDisplayVersion()
    {
        Version? version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null
            ? "1.0"
            : $"{version.Major}.{version.Minor}";
    }

    private sealed record DriveOption(DriveInfo? Drive, string DisplayText)
    {
        public override string ToString()
        {
            return DisplayText;
        }
    }
}
