using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

namespace UnMessage.Server;

/// <summary>
/// 服务器管理窗体：提供启动/停止服务器、查看日志和连接状态等管理功能。
/// </summary>
public partial class ServerForm : Form
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static readonly Icon LockIcon = CreateLockIcon();

    private const int MaxBroadcastLength = 1024;
    private const int MaxLogLines = 2000;

    /// <summary>中继服务器实例。</summary>
    private RelayServer? _server;

    private readonly System.Windows.Forms.Timer _metricAnimationTimer = new() { Interval = 16 };
    private int _onlineTarget;
    private int _pairsTarget;
    private int _onlineDisplay;
    private int _pairsDisplay;

    public ServerForm()
    {
        InitializeComponent();
        ApplyModernTheme();
        Icon = LockIcon;
        _metricAnimationTimer.Tick += (_, _) => AnimateMetrics();
        txtBroadcast.KeyDown += txtBroadcast_KeyDown;
    }

    private void ApplyModernTheme()
    {
        BackColor = Color.FromArgb(245, 247, 251);

        pnlTop.BackColor = Color.White;
        pnlBroadcast.BackColor = Color.White;
        rtbLog.BackColor = Color.White;
        rtbLog.ForeColor = Color.FromArgb(43, 52, 67);
        rtbLog.BorderStyle = BorderStyle.FixedSingle;

        txtPort.BorderStyle = BorderStyle.FixedSingle;
        txtBroadcast.BorderStyle = BorderStyle.FixedSingle;
        txtPort.BackColor = Color.White;
        txtBroadcast.BackColor = Color.White;
        txtPort.ForeColor = Color.FromArgb(45, 53, 68);
        txtBroadcast.ForeColor = Color.FromArgb(45, 53, 68);

        lblPort.ForeColor = Color.FromArgb(70, 84, 109);
        lblBroadcast.ForeColor = Color.FromArgb(70, 84, 109);
        lblOnlineCaption.ForeColor = Color.FromArgb(70, 84, 109);
        lblPairsCaption.ForeColor = Color.FromArgb(70, 84, 109);
        lblOnlineCount.ForeColor = Color.FromArgb(53, 71, 102);
        lblPairsCount.ForeColor = Color.FromArgb(53, 71, 102);

        StylePrimaryButton(btnStart);
        StyleDangerButton(btnStop);
        StylePrimaryButton(btnBroadcast);

        statusStrip.BackColor = Color.White;
        statusStrip.SizingGrip = false;
        lblStatusText.ForeColor = Color.FromArgb(81, 94, 117);
    }

    private static void StylePrimaryButton(Button button)
    {
        StyleButton(button, Color.FromArgb(70, 120, 255), Color.White, Color.FromArgb(55, 103, 230));
    }

    private static void StyleDangerButton(Button button)
    {
        StyleButton(button, Color.FromArgb(237, 96, 116), Color.White, Color.FromArgb(224, 78, 99));
    }

    private static void StyleButton(Button button, Color normalBack, Color foreColor, Color hoverBack)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = normalBack;
        button.ForeColor = foreColor;
        button.Font = new Font(button.Font, FontStyle.Bold);
        button.Cursor = Cursors.Hand;

        ApplyRoundedCorner(button, 8);
        button.Resize += (_, _) => ApplyRoundedCorner(button, 8);

        button.MouseEnter += (_, _) =>
        {
            if (button.Enabled)
                button.BackColor = hoverBack;
        };
        button.MouseLeave += (_, _) =>
        {
            if (button.Enabled)
                button.BackColor = normalBack;
        };
        button.EnabledChanged += (_, _) =>
        {
            if (button.Enabled)
            {
                button.BackColor = normalBack;
                button.ForeColor = foreColor;
            }
            else
            {
                button.BackColor = Color.FromArgb(229, 233, 241);
                button.ForeColor = Color.FromArgb(154, 164, 181);
            }
        };
    }

    private static void ApplyRoundedCorner(Control control, int radius)
    {
        if (control.Width <= 0 || control.Height <= 0)
            return;

        var path = new GraphicsPath();
        int diameter = radius * 2;
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(control.Width - diameter, 0, diameter, diameter, 270, 90);
        path.AddArc(control.Width - diameter, control.Height - diameter, diameter, diameter, 0, 90);
        path.AddArc(0, control.Height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        control.Region = new Region(path);
    }

    /// <summary>
    /// 「启动服务器」按钮点击：在指定端口启动中继服务器。
    /// </summary>
    private async void btnStart_Click(object sender, EventArgs e)
    {
        if (!int.TryParse(txtPort.Text, out int port) || port < 1 || port > 65535)
        {
            MessageBox.Show("请输入有效的端口号（1-65535）。", "输入错误",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 释放旧实例，创建新服务器
        _server?.Dispose();
        _server = new RelayServer();

        // 绑定日志事件 → 在日志区域显示
        _server.Log += msg =>
            Invoke(() => AppendLog(msg));

        // 绑定在线人数变化事件
        _server.OnlineCountChanged += count =>
            Invoke(() => SetMetricTargets(onlineTarget: count, pairsTarget: null));

        // 绑定活跃配对数变化事件
        _server.ActivePairsChanged += count =>
            Invoke(() => SetMetricTargets(onlineTarget: null, pairsTarget: count));

        // 更新 UI 状态
        SetRunningState(true);

        try
        {
            await _server.StartAsync(port);
        }
        catch (SocketException ex)
        {
            AppendLog($"启动失败：{ex.Message}", Color.Red);
            SetRunningState(false);
        }
        catch (Exception ex)
        {
            AppendLog($"错误：{ex.Message}", Color.Red);
            SetRunningState(false);
        }
    }

    /// <summary>
    /// 「停止服务器」按钮点击：停止中继服务器。
    /// </summary>
    private void btnStop_Click(object sender, EventArgs e)
    {
        _server?.Stop();
        SetRunningState(false);
    }

    /// <summary>
    /// 「发送公告」按钮点击：向所有在线客户端发送公告消息。
    /// </summary>
    private async void btnBroadcast_Click(object sender, EventArgs e)
    {
        string message = txtBroadcast.Text.Trim();
        if (string.IsNullOrWhiteSpace(message) || _server is null || !_server.IsRunning) return;
        if (message.Length > MaxBroadcastLength)
        {
            MessageBox.Show($"公告内容过长，最多 {MaxBroadcastLength} 个字符。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        txtBroadcast.Clear();
        await _server.SendBroadcastAsync(message);
    }

    private void txtBroadcast_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.SuppressKeyPress = true;
            btnBroadcast.PerformClick();
        }
    }

    /// <summary>
    /// 在日志区域追加一条带时间戳的日志消息。
    /// </summary>
    private void AppendLog(string message, Color? color = null)
    {
        TrimLogLinesIfNeeded();

        string timestamp = DateTime.Now.ToString("HH:mm:ss");

        rtbLog.SelectionStart = rtbLog.TextLength;
        rtbLog.SelectionColor = Color.DarkGray;
        rtbLog.AppendText($"[{timestamp}] ");

        rtbLog.SelectionStart = rtbLog.TextLength;
        rtbLog.SelectionColor = color ?? Color.FromArgb(34, 34, 34);
        rtbLog.AppendText($"{message}\n");

        rtbLog.ScrollToCaret();
    }

    private void TrimLogLinesIfNeeded()
    {
        if (rtbLog.Lines.Length < MaxLogLines) return;

        string[] lines = rtbLog.Lines;
        int keep = MaxLogLines / 2;
        rtbLog.Lines = lines.Skip(lines.Length - keep).ToArray();
        rtbLog.SelectionStart = rtbLog.TextLength;
    }

    /// <summary>
    /// 根据服务器运行状态更新界面控件。
    /// </summary>
    private void SetRunningState(bool running)
    {
        txtPort.Enabled = !running;
        btnStart.Enabled = !running;
        btnStop.Enabled = running;
        txtBroadcast.Enabled = running;
        btnBroadcast.Enabled = running;

        lblStatusIndicator.ForeColor = running ? Color.LimeGreen : Color.Gray;

        if (running)
        {
            _metricAnimationTimer.Start();
        }
        else
        {
            SetMetricTargets(0, 0);
            _onlineDisplay = 0;
            _pairsDisplay = 0;
            lblOnlineCount.Text = "0";
            lblPairsCount.Text = "0";
        }

        lblStatusText.Text = running
            ? $"服务器运行中 — 监听端口 {txtPort.Text}"
            : "服务器未运行";
    }

    private void SetMetricTargets(int? onlineTarget, int? pairsTarget)
    {
        if (onlineTarget.HasValue) _onlineTarget = onlineTarget.Value;
        if (pairsTarget.HasValue) _pairsTarget = pairsTarget.Value;
        if (!_metricAnimationTimer.Enabled)
            _metricAnimationTimer.Start();
    }

    private void AnimateMetrics()
    {
        bool changed = false;

        if (_onlineDisplay != _onlineTarget)
        {
            _onlineDisplay += Math.Sign(_onlineTarget - _onlineDisplay);
            lblOnlineCount.Text = _onlineDisplay.ToString();
            changed = true;
        }

        if (_pairsDisplay != _pairsTarget)
        {
            _pairsDisplay += Math.Sign(_pairsTarget - _pairsDisplay);
            lblPairsCount.Text = _pairsDisplay.ToString();
            changed = true;
        }

        if (!changed && (_server?.IsRunning != true))
            _metricAnimationTimer.Stop();
    }

    /// <summary>
    /// 窗体关闭时释放服务器资源。
    /// </summary>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _metricAnimationTimer.Stop();
        _server?.Dispose();
        base.OnFormClosed(e);
    }

    private static Icon CreateLockIcon()
    {
        using var bmp = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        using (var bodyBrush = new SolidBrush(Color.FromArgb(51, 144, 236)))
        using (var shacklePen = new Pen(Color.FromArgb(36, 112, 190), 6f))
        using (var keyHoleBrush = new SolidBrush(Color.White))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            g.FillRoundedRectangle(bodyBrush, new Rectangle(12, 26, 40, 30), new Size(8, 8));
            g.DrawArc(shacklePen, 18, 8, 28, 28, 200, 140);

            g.FillEllipse(keyHoleBrush, 29, 36, 6, 6);
            g.FillRectangle(keyHoleBrush, 31, 42, 2, 8);
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(hIcon);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }
}
