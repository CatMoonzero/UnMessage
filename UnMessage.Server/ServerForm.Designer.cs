namespace UnMessage.Server
{
    partial class ServerForm
    {
        private System.ComponentModel.IContainer components = null;
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }
        #region Windows Form Designer generated code
        private void InitializeComponent()
        {
            pnlTop = new Panel();
            lblPort = new Label();
            txtPort = new TextBox();
            btnStart = new Button();
            btnStop = new Button();
            lblStatusIndicator = new Label();
            lblOnlineCaption = new Label();
            lblOnlineCount = new Label();
            lblPairsCaption = new Label();
            lblPairsCount = new Label();
            rtbLog = new RichTextBox();
            pnlBroadcast = new Panel();
            lblBroadcast = new Label();
            txtBroadcast = new TextBox();
            btnBroadcast = new Button();
            statusStrip = new StatusStrip();
            lblStatusText = new ToolStripStatusLabel();
            pnlTop.SuspendLayout();
            pnlBroadcast.SuspendLayout();
            statusStrip.SuspendLayout();
            SuspendLayout();
            // pnlTop
            pnlTop.Controls.Add(lblPairsCount);
            pnlTop.Controls.Add(lblPairsCaption);
            pnlTop.Controls.Add(lblOnlineCount);
            pnlTop.Controls.Add(lblOnlineCaption);
            pnlTop.Controls.Add(lblStatusIndicator);
            pnlTop.Controls.Add(btnStop);
            pnlTop.Controls.Add(btnStart);
            pnlTop.Controls.Add(txtPort);
            pnlTop.Controls.Add(lblPort);
            pnlTop.Dock = DockStyle.Top;
            pnlTop.BackColor = SystemColors.Control;
            pnlTop.Location = new Point(0, 0);
            pnlTop.Name = "pnlTop";
            pnlTop.Padding = new Padding(12);
            pnlTop.Size = new Size(784, 60);
            // lblPort
            lblPort.AutoSize = true;
            lblPort.Location = new Point(16, 18);
            lblPort.Name = "lblPort";
            lblPort.Text = "监听端口:";
            // txtPort
            txtPort.Location = new Point(102, 14);
            txtPort.Name = "txtPort";
            txtPort.Size = new Size(76, 27);
            txtPort.Text = "9000";
            // btnStart
            btnStart.Location = new Point(194, 11);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(100, 35);
            btnStart.Text = "启动";
            btnStart.UseVisualStyleBackColor = true;
            btnStart.Click += btnStart_Click;
            // btnStop
            btnStop.Location = new Point(304, 11);
            btnStop.Name = "btnStop";
            btnStop.Size = new Size(100, 35);
            btnStop.Text = "停止";
            btnStop.UseVisualStyleBackColor = true;
            btnStop.Enabled = false;
            btnStop.Click += btnStop_Click;
            // lblStatusIndicator
            lblStatusIndicator.AutoSize = true;
            lblStatusIndicator.Font = new Font("微软雅黑", 14F, FontStyle.Bold);
            lblStatusIndicator.ForeColor = Color.Gray;
            lblStatusIndicator.Location = new Point(420, 12);
            lblStatusIndicator.Name = "lblStatusIndicator";
            lblStatusIndicator.Text = "\u25CF";
            // lblOnlineCaption
            lblOnlineCaption.AutoSize = true;
            lblOnlineCaption.Location = new Point(450, 18);
            lblOnlineCaption.Name = "lblOnlineCaption";
            lblOnlineCaption.Text = "在线:";
            // lblOnlineCount
            lblOnlineCount.AutoSize = true;
            lblOnlineCount.Font = new Font("微软雅黑", 11F, FontStyle.Bold);
            lblOnlineCount.Location = new Point(508, 16);
            lblOnlineCount.Name = "lblOnlineCount";
            lblOnlineCount.Text = "0";
            // lblPairsCaption
            lblPairsCaption.AutoSize = true;
            lblPairsCaption.Location = new Point(540, 18);
            lblPairsCaption.Name = "lblPairsCaption";
            lblPairsCaption.Text = "配对:";
            // lblPairsCount
            lblPairsCount.AutoSize = true;
            lblPairsCount.Font = new Font("微软雅黑", 11F, FontStyle.Bold);
            lblPairsCount.Location = new Point(590, 16);
            lblPairsCount.Name = "lblPairsCount";
            lblPairsCount.Text = "0";
            // rtbLog
            rtbLog.Dock = DockStyle.Fill;
            rtbLog.Font = new Font("微软雅黑", 9F);
            rtbLog.ReadOnly = true;
            rtbLog.Name = "rtbLog";
            rtbLog.Text = "";
            // pnlBroadcast
            pnlBroadcast.Controls.Add(btnBroadcast);
            pnlBroadcast.Controls.Add(txtBroadcast);
            pnlBroadcast.Controls.Add(lblBroadcast);
            pnlBroadcast.Dock = DockStyle.Top;
            pnlBroadcast.Location = new Point(0, 60);
            pnlBroadcast.Name = "pnlBroadcast";
            pnlBroadcast.Padding = new Padding(12, 6, 12, 6);
            pnlBroadcast.Size = new Size(784, 44);
            // lblBroadcast
            lblBroadcast.AutoSize = true;
            lblBroadcast.Location = new Point(16, 12);
            lblBroadcast.Name = "lblBroadcast";
            lblBroadcast.Text = "公告:";
            // txtBroadcast
            txtBroadcast.Location = new Point(62, 8);
            txtBroadcast.Name = "txtBroadcast";
            txtBroadcast.Size = new Size(500, 27);
            txtBroadcast.PlaceholderText = "输入公告消息...";
            txtBroadcast.Enabled = false;
            // btnBroadcast
            btnBroadcast.Location = new Point(574, 5);
            btnBroadcast.Name = "btnBroadcast";
            btnBroadcast.Size = new Size(100, 35);
            btnBroadcast.Text = "发送公告";
            btnBroadcast.UseVisualStyleBackColor = true;
            btnBroadcast.Enabled = false;
            btnBroadcast.Click += btnBroadcast_Click;
            // statusStrip
            statusStrip.Items.AddRange(new ToolStripItem[] { lblStatusText });
            statusStrip.Location = new Point(0, 519);
            statusStrip.Name = "statusStrip";
            statusStrip.Size = new Size(784, 22);
            // lblStatusText
            lblStatusText.Name = "lblStatusText";
            lblStatusText.Spring = true;
            lblStatusText.Text = "服务器未运行";
            lblStatusText.TextAlign = ContentAlignment.MiddleLeft;
            // ServerForm
            AutoScaleDimensions = new SizeF(9F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(784, 541);
            Controls.Add(rtbLog);
            Controls.Add(pnlBroadcast);
            Controls.Add(pnlTop);
            Controls.Add(statusStrip);
            MinimumSize = new Size(600, 400);
            Name = "ServerForm";
            Text = "UnMessage 中继服务器";
            pnlTop.ResumeLayout(false);
            pnlTop.PerformLayout();
            pnlBroadcast.ResumeLayout(false);
            pnlBroadcast.PerformLayout();
            statusStrip.ResumeLayout(false);
            statusStrip.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }
        #endregion
        private Panel pnlTop;
        private Label lblPort;
        private TextBox txtPort;
        private Button btnStart;
        private Button btnStop;
        private Label lblStatusIndicator;
        private Label lblOnlineCaption;
        private Label lblOnlineCount;
        private Label lblPairsCaption;
        private Label lblPairsCount;
        private RichTextBox rtbLog;
        private Panel pnlBroadcast;
        private Label lblBroadcast;
        private TextBox txtBroadcast;
        private Button btnBroadcast;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStatusText;
    }
}
