namespace UnMessage
{
    partial class ChatForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        private void InitializeComponent()
        {
            pnlTop = new Panel();
            lblUsername = new Label();
            txtUsername = new TextBox();
            lblIP = new Label();
            txtIP = new TextBox();
            lblPort = new Label();
            txtPort = new TextBox();
            btnConnect = new Button();
            btnDisconnect = new Button();
            btnEndChat = new Button();
            btnIdentitySearch = new Button();
            btnCreateGroup = new Button();
            btnProtocolSelfCheck = new Button();
            splitContainer = new SplitContainer();
            lstUsers = new ListBox();
            lblUsersTitle = new Label();
            tabConversations = new TabControl();
            pnlBottom = new Panel();
            txtMessage = new TextBox();
            btnEmoji = new Button();
            btnExchangeIdentity = new Button();
            btnSend = new Button();
            statusStrip = new StatusStrip();
            lblStatus = new ToolStripStatusLabel();
            lblEncryption = new ToolStripStatusLabel();

            pnlTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            pnlBottom.SuspendLayout();
            statusStrip.SuspendLayout();
            SuspendLayout();

            // pnlTop
            pnlTop.Controls.Add(btnEndChat);
            pnlTop.Controls.Add(btnIdentitySearch);
            pnlTop.Controls.Add(btnProtocolSelfCheck);
            pnlTop.Controls.Add(btnDisconnect);
            pnlTop.Controls.Add(btnConnect);
            pnlTop.Controls.Add(txtPort);
            pnlTop.Controls.Add(lblPort);
            pnlTop.Controls.Add(txtIP);
            pnlTop.Controls.Add(lblIP);
            pnlTop.Controls.Add(txtUsername);
            pnlTop.Controls.Add(lblUsername);
            pnlTop.Dock = DockStyle.Top;
            pnlTop.Location = new Point(0, 0);
            pnlTop.Name = "pnlTop";
            pnlTop.Padding = new Padding(10, 8, 10, 8);
            pnlTop.Size = new Size(1008, 56);

            // lblUsername
            lblUsername.AutoSize = true;
            lblUsername.Location = new Point(14, 16);
            lblUsername.Name = "lblUsername";
            lblUsername.Text = "用户名:";

            // txtUsername
            txtUsername.Location = new Point(72, 12);
            txtUsername.Name = "txtUsername";
            txtUsername.Size = new Size(110, 27);

            // lblIP
            lblIP.AutoSize = true;
            lblIP.Location = new Point(194, 16);
            lblIP.Name = "lblIP";
            lblIP.Text = "服务器:";

            // txtIP
            txtIP.Location = new Point(252, 12);
            txtIP.Name = "txtIP";
            txtIP.Size = new Size(150, 27);
            txtIP.Text = "127.0.0.1";

            // lblPort
            lblPort.AutoSize = true;
            lblPort.Location = new Point(414, 16);
            lblPort.Name = "lblPort";
            lblPort.Text = "端口:";

            // txtPort
            txtPort.Location = new Point(456, 12);
            txtPort.Name = "txtPort";
            txtPort.Size = new Size(66, 27);
            txtPort.Text = "9000";

            // btnConnect
            btnConnect.Location = new Point(536, 9);
            btnConnect.Name = "btnConnect";
            btnConnect.Size = new Size(86, 35);
            btnConnect.Text = "连接";
            btnConnect.UseVisualStyleBackColor = true;
            btnConnect.Click += btnConnect_Click;

            // btnDisconnect
            btnDisconnect.Location = new Point(630, 9);
            btnDisconnect.Name = "btnDisconnect";
            btnDisconnect.Size = new Size(86, 35);
            btnDisconnect.Text = "断开";
            btnDisconnect.UseVisualStyleBackColor = true;
            btnDisconnect.Click += btnDisconnect_Click;

            // btnEndChat
            btnEndChat.Location = new Point(724, 9);
            btnEndChat.Name = "btnEndChat";
            btnEndChat.Size = new Size(100, 35);
            btnEndChat.Text = "结束聊天";
            btnEndChat.UseVisualStyleBackColor = true;
            btnEndChat.Enabled = false;
            btnEndChat.Click += btnEndChat_Click;

            // btnIdentitySearch
            btnIdentitySearch.Location = new Point(830, 9);
            btnIdentitySearch.Name = "btnIdentitySearch";
            btnIdentitySearch.Size = new Size(110, 35);
            btnIdentitySearch.Text = "识别卡搜人";
            btnIdentitySearch.UseVisualStyleBackColor = true;
            btnIdentitySearch.Click += btnIdentitySearch_Click;

            // btnProtocolSelfCheck
            btnProtocolSelfCheck.Location = new Point(946, 9);
            btnProtocolSelfCheck.Name = "btnProtocolSelfCheck";
            btnProtocolSelfCheck.Size = new Size(52, 35);
            btnProtocolSelfCheck.Text = "自检";
            btnProtocolSelfCheck.UseVisualStyleBackColor = true;
            btnProtocolSelfCheck.Click += btnProtocolSelfCheck_Click;

            // btnCreateGroup
            btnCreateGroup.Dock = DockStyle.Top;
            btnCreateGroup.Name = "btnCreateGroup";
            btnCreateGroup.Size = new Size(200, 30);
            btnCreateGroup.Text = "➕ 创建群聊";
            btnCreateGroup.UseVisualStyleBackColor = true;
            btnCreateGroup.Click += btnCreateGroup_Click;

            // splitContainer
            splitContainer.Dock = DockStyle.Fill;
            splitContainer.FixedPanel = FixedPanel.Panel1;
            splitContainer.Location = new Point(0, 56);
            splitContainer.Name = "splitContainer";
            splitContainer.SplitterDistance = 260;
            splitContainer.Panel1MinSize = 260;

            // splitContainer.Panel1 - 用户列表
            splitContainer.Panel1.Controls.Add(lstUsers);
            splitContainer.Panel1.Controls.Add(btnCreateGroup);
            splitContainer.Panel1.Controls.Add(lblUsersTitle);

            // splitContainer.Panel2 - 聊天区域
            splitContainer.Panel2.Controls.Add(tabConversations);

            // lblUsersTitle
            lblUsersTitle.Dock = DockStyle.Top;
            lblUsersTitle.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
            lblUsersTitle.Location = new Point(0, 0);
            lblUsersTitle.Name = "lblUsersTitle";
            lblUsersTitle.Padding = new Padding(0, 4, 0, 4);
            lblUsersTitle.Size = new Size(250, 28);
            lblUsersTitle.Text = "在线用户 / 群聊（双击选择）";
            lblUsersTitle.TextAlign = ContentAlignment.MiddleCenter;

            // lstUsers
            lstUsers.BorderStyle = BorderStyle.FixedSingle;
            lstUsers.Dock = DockStyle.Fill;
            lstUsers.DrawMode = DrawMode.OwnerDrawFixed;
            lstUsers.Font = new Font("微软雅黑", 10F, FontStyle.Regular, GraphicsUnit.Point, 134);
            lstUsers.ItemHeight = 26;
            lstUsers.Location = new Point(0, 28);
            lstUsers.Name = "lstUsers";
            lstUsers.Size = new Size(260, 580);
            lstUsers.Click += lstUsers_Click;
            lstUsers.DoubleClick += lstUsers_DoubleClick;
            lstUsers.DrawItem += lstUsers_DrawItem;

            // tabConversations
            tabConversations.Dock = DockStyle.Fill;
            tabConversations.Name = "tabConversations";
            tabConversations.SelectedIndexChanged += tabConversations_SelectedIndexChanged;

            // pnlBottom
            pnlBottom.Controls.Add(txtMessage);
            pnlBottom.Controls.Add(btnEmoji);
            pnlBottom.Controls.Add(btnExchangeIdentity);
            pnlBottom.Controls.Add(btnSend);
            pnlBottom.Dock = DockStyle.Bottom;
            pnlBottom.Location = new Point(0, 658);
            pnlBottom.Name = "pnlBottom";
            pnlBottom.Padding = new Padding(10, 10, 10, 10);
            pnlBottom.Size = new Size(1008, 52);

            // txtMessage
            txtMessage.Dock = DockStyle.Fill;
            txtMessage.Multiline = false;
            txtMessage.PlaceholderText = "输入消息...";
            txtMessage.Name = "txtMessage";
            txtMessage.KeyDown += txtMessage_KeyDown;

            // btnEmoji
            btnEmoji.Dock = DockStyle.Right;
            btnEmoji.Size = new Size(52, 32);
            btnEmoji.Text = "😀";
            btnEmoji.Name = "btnEmoji";
            btnEmoji.UseVisualStyleBackColor = true;
            btnEmoji.Enabled = false;
            btnEmoji.Click += btnEmoji_Click;

            // btnExchangeIdentity
            btnExchangeIdentity.Dock = DockStyle.Right;
            btnExchangeIdentity.Size = new Size(150, 32);
            btnExchangeIdentity.Text = "交换身份识别卡";
            btnExchangeIdentity.Name = "btnExchangeIdentity";
            btnExchangeIdentity.UseVisualStyleBackColor = true;
            btnExchangeIdentity.Enabled = false;
            btnExchangeIdentity.Click += btnExchangeIdentity_Click;

            // btnSend
            btnSend.Dock = DockStyle.Right;
            btnSend.Size = new Size(86, 32);
            btnSend.Text = "发送";
            btnSend.Name = "btnSend";
            btnSend.UseVisualStyleBackColor = true;
            btnSend.Click += btnSend_Click;

            // statusStrip
            statusStrip.Items.AddRange(new ToolStripItem[] { lblStatus, lblEncryption });
            statusStrip.Location = new Point(0, 714);
            statusStrip.Name = "statusStrip";
            statusStrip.Size = new Size(1008, 26);

            // lblStatus
            lblStatus.Name = "lblStatus";
            lblStatus.Spring = true;
            lblStatus.Text = "未连接";
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;

            // lblEncryption
            lblEncryption.ForeColor = Color.Red;
            lblEncryption.Name = "lblEncryption";
            lblEncryption.Text = "🔓 未加密";

            // ChatForm
            AutoScaleDimensions = new SizeF(9F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1008, 740);
            Controls.Add(splitContainer);
            Controls.Add(pnlBottom);
            Controls.Add(pnlTop);
            Controls.Add(statusStrip);
            MinimumSize = new Size(700, 500);
            Name = "ChatForm";
            Text = "UnMessage - 端对端加密聊天";
            pnlTop.ResumeLayout(false);
            pnlTop.PerformLayout();
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            pnlBottom.ResumeLayout(false);
            pnlBottom.PerformLayout();
            statusStrip.ResumeLayout(false);
            statusStrip.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Panel pnlTop;
        private Label lblUsername;
        private TextBox txtUsername;
        private Label lblIP;
        private TextBox txtIP;
        private Label lblPort;
        private TextBox txtPort;
        private Button btnConnect;
        private Button btnDisconnect;
        private Button btnEndChat;
        private Button btnIdentitySearch;
        private Button btnProtocolSelfCheck;
        private Button btnCreateGroup;
        private SplitContainer splitContainer;
        private Label lblUsersTitle;
        private ListBox lstUsers;
        private TabControl tabConversations;
        private Panel pnlBottom;
        private TextBox txtMessage;
        private Button btnEmoji;
        private Button btnExchangeIdentity;
        private Button btnSend;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStatus;
        private ToolStripStatusLabel lblEncryption;
    }
}
