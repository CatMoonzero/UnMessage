using System.Runtime.InteropServices;
using System.Text.Json;
using System.Drawing.Drawing2D;

namespace UnMessage;

/// <summary>
/// 主窗体：端对端加密聊天客户端。
/// 连接服务器 → 注册用户名 → 查看在线用户 → 选择对方 → 加密聊天。
/// </summary>
public partial class ChatForm : Form
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    private const int MaxMessageLength = 4096;
    private const int MaxUsernameLength = 24;
    private const int MaxGroupNameLength = 32;
    private static readonly HashSet<string> ReservedUsernames = new(StringComparer.OrdinalIgnoreCase)
    {
        "系统",
        "system",
        "sys",
        "管理员",
        "管理",
        "admin",
        "administrator",
        "root",
        "客服",
        "support",
        "gm",
        "mod",
    };
    private static readonly string[] ReservedUsernameKeywords =
    [
        "系统",
        "管理",
        "admin",
        "root",
        "support",
        "客服",
        "gm",
        "mod",
    ];
    private static readonly Icon LockIcon = CreateLockIcon();
    private readonly Font _emojiFont = new("Segoe UI Emoji", 10F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly System.Windows.Forms.Timer _connectAnimationTimer = new() { Interval = 260 };
    private readonly Font _emojiListFont = new("Segoe UI Emoji", 10F, FontStyle.Regular, GraphicsUnit.Point);
    private bool _isConnecting;
    private int _connectDotCount;

    /// <summary>聊天客户端通信实例。</summary>
    private ChatPeer? _peer;

    /// <summary>当前在线用户列表缓存。</summary>
    private List<ClientEntry> _onlineUsers = [];

    /// <summary>当前可用群聊列表缓存。</summary>
    private List<GroupEntry> _availableGroups = [];

    /// <summary>当前已加入群聊跟踪。</summary>
    private readonly HashSet<string> _joinedGroupIds = new();
    private readonly Dictionary<string, string> _groupNamesById = new();

    /// <summary>会话 Tab 页字典（peerId/groupKey → TabPage）。</summary>
    private readonly Dictionary<string, TabPage> _chatTabs = new();

    /// <summary>会话聊天框字典（peerId/groupKey → RichTextBox）。</summary>
    private readonly Dictionary<string, RichTextBox> _chatBoxes = new();

    // ── Telegram 风格会话管理 ─────────────────────────────────

    private sealed class ConversationInfo
    {
        public required string PeerId { get; init; }
        public string DisplayName { get; set; } = "";
        public bool IsGroup { get; init; }
        public int UnreadCount { get; set; }
        public List<(string Sender, string Message, Color Color)> BufferedMessages { get; } = [];
    }

    private enum ListItemType { Header, Placeholder, Conversation, OnlineUser, Group }
    private sealed record ListItem(ListItemType Type, string? Id = null, string? Name = null);

    /// <summary>所有活跃会话（含未打开标签页的）。</summary>
    private readonly Dictionary<string, ConversationInfo> _conversations = new();

    /// <summary>用户主动发起的聊天请求 ID（用于区分主动/被动发起）。</summary>
    private readonly HashSet<string> _pendingOutgoingRequests = new();

    /// <summary>左侧列表项数据映射。</summary>
    private readonly List<ListItem> _listItems = [];
    private readonly Dictionary<string, string> _trustedIdentities = new();
    private readonly Dictionary<string, string> _identityRemarks = new();
    private readonly Dictionary<string, string> _peerIdentityMap = new();
    private readonly Dictionary<string, string> _peerSafetyNumbers = new();
    private readonly Dictionary<string, IdentityExchangeState> _identityStates = new();
    private readonly HashSet<string> _blockedPeers = new();
    private readonly ContextMenuStrip _userListMenu = new();
    private readonly ContextMenuStrip _emojiMenu = new();
    private string? _contextMenuPeerId;
    private string? _contextMenuGroupId;
    private bool _collapseConversations;
    private bool _collapseOnlineUsers;
    private bool _collapseGroups;
    private string _localIdentityId = "";
    private readonly string _identityStorePath = Path.Combine(AppContext.BaseDirectory, "identities.json");
    private readonly string _identityCardsDir = Path.Combine(AppContext.BaseDirectory, "identity-cards");
    private const int IdentityIdLength = 32;

    private enum IdentityExchangeState { Idle, Pending, Verified }

    public ChatForm()
    {
        InitializeComponent();
        ApplyModernTheme();
        Icon = LockIcon;
        InitializeUserListContextMenu();
        InitializeEmojiMenu();
        LoadIdentityStore();
        Directory.CreateDirectory(_identityCardsDir);
        _connectAnimationTimer.Tick += (_, _) => AnimateConnectingState();
        Load += (_, _) => AdjustLeftPanelWidthForTitle();
        SizeChanged += (_, _) => AdjustLeftPanelWidthForTitle();
        UpdateUI(connected: false, chatting: false);
    }

    private void ApplyModernTheme()
    {
        BackColor = Color.FromArgb(245, 247, 251);

        pnlTop.BackColor = Color.White;
        pnlBottom.BackColor = Color.White;

        splitContainer.BackColor = Color.FromArgb(228, 232, 240);
        splitContainer.Panel1.BackColor = Color.White;
        splitContainer.Panel2.BackColor = Color.White;

        lstUsers.BackColor = Color.White;
        lstUsers.ForeColor = Color.FromArgb(42, 51, 66);

        lblUsersTitle.BackColor = Color.FromArgb(248, 250, 254);
        lblUsersTitle.ForeColor = Color.FromArgb(69, 86, 116);

        txtMessage.BackColor = Color.White;
        txtMessage.BorderStyle = BorderStyle.FixedSingle;

        txtUsername.BorderStyle = BorderStyle.FixedSingle;
        txtIP.BorderStyle = BorderStyle.FixedSingle;
        txtPort.BorderStyle = BorderStyle.FixedSingle;

        StyleInput(txtUsername);
        StyleInput(txtIP);
        StyleInput(txtPort);

        StylePrimaryButton(btnConnect);
        StyleDangerButton(btnDisconnect);
        StyleNeutralButton(btnEndChat);
        StyleNeutralButton(btnIdentitySearch);
        StyleNeutralButton(btnProtocolSelfCheck);
        StyleNeutralButton(btnCreateGroup);
        StyleNeutralButton(btnEmoji);
        StyleNeutralButton(btnExchangeIdentity);
        StylePrimaryButton(btnSend);

        statusStrip.BackColor = Color.White;
        statusStrip.SizingGrip = false;
        lblStatus.ForeColor = Color.FromArgb(81, 94, 117);

        tabConversations.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabConversations.Padding = new Point(16, 6);
        tabConversations.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= tabConversations.TabPages.Count)
                return;

            var tab = tabConversations.TabPages[e.Index];
            bool selected = e.Index == tabConversations.SelectedIndex;
            var rect = e.Bounds;

            using var bgBrush = new SolidBrush(selected ? Color.FromArgb(70, 120, 255) : Color.FromArgb(242, 245, 252));
            using var textBrush = new SolidBrush(selected ? Color.White : Color.FromArgb(74, 86, 109));
            e.Graphics.FillRectangle(bgBrush, rect);
            TextRenderer.DrawText(e.Graphics, tab.Text, tabConversations.Font, rect, textBrush.Color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
    }

    private static void StyleInput(TextBox textBox)
    {
        textBox.BackColor = Color.White;
        textBox.ForeColor = Color.FromArgb(45, 53, 68);
    }

    private static void StylePrimaryButton(Button button)
    {
        StyleButton(button, Color.FromArgb(70, 120, 255), Color.White, Color.FromArgb(55, 103, 230));
    }

    private static void StyleNeutralButton(Button button)
    {
        StyleButton(button, Color.FromArgb(236, 241, 250), Color.FromArgb(67, 82, 110), Color.FromArgb(224, 232, 245));
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

    // ── 连接/断开 ──────────────────────────────────────────

    /// <summary>
    /// 「连接」按钮：连接到中继服务器并注册用户名。
    /// </summary>
    private async void btnConnect_Click(object sender, EventArgs e)
    {
        if (_isConnecting) return;

        string username = txtUsername.Text.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            MessageBox.Show("请输入用户名。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            txtUsername.Focus();
            return;
        }
        if (username.Length > MaxUsernameLength)
        {
            MessageBox.Show($"用户名过长，最多 {MaxUsernameLength} 个字符。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            txtUsername.Focus();
            return;
        }
        if (IsReservedUsername(username))
        {
            MessageBox.Show("该用户名为保留名称或包含保留关键词，请更换其他用户名。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            txtUsername.Focus();
            return;
        }

        string host = txtIP.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            MessageBox.Show("请输入服务器地址。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            txtIP.Focus();
            return;
        }

        if (!int.TryParse(txtPort.Text.Trim(), out int port) || port < 1 || port > 65535)
        {
            MessageBox.Show("请输入有效端口号（1-65535）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            txtPort.Focus();
            return;
        }

        // 释放旧连接
        _peer?.Dispose();
        _peer = new ChatPeer();
        _peer.LocalIdentityId = _localIdentityId;

        // 绑定事件
        _peer.StatusChanged += msg =>
            Invoke(() => SetStatus(msg));

        _peer.IdentityHintReceived += (peerId, identityId) => Invoke(() =>
        {
            if (!IsValidIdentityId(identityId)) return;
            _peerIdentityMap[peerId] = identityId;
            if (_trustedIdentities.ContainsKey(identityId))
                SetIdentityState(peerId, IdentityExchangeState.Verified);
            else
                SetIdentityState(peerId, IdentityExchangeState.Idle);
        });

        _peer.SafetyNumberAvailable += (peerId, safetyNumber) => Invoke(() =>
        {
            _peerSafetyNumbers[peerId] = safetyNumber;
            string peerName = _peerNamesFromConversations(peerId);
            string shortSafety = safetyNumber[..Math.Min(12, safetyNumber.Length)];
            SetStatus($"已收到 {peerName} 的安全号码 {shortSafety}...，信任状态以身份识别卡为准。");
        });

        _peer.ChatStarted += (peerId, peerName) => Invoke(() =>
        {
            if (GetIdentityState(peerId) != IdentityExchangeState.Verified)
                SetIdentityState(peerId, IdentityExchangeState.Idle);
            // 记录会话
            _conversations[peerId] = new ConversationInfo
            {
                PeerId = peerId,
                DisplayName = BuildConversationDisplayName(peerName, peerId),
            };

            if (_pendingOutgoingRequests.Remove(peerId))
            {
                // 用户主动发起 → 直接打开标签页
                OpenConversationTab(peerId);
                AppendToChat(peerId, "系统", $"正在与 \"{peerName}\" 建立加密通道...", Color.DarkOrange);
                UpdateUI(connected: true, chatting: true);
                SetStatus($"已开始与 {peerName} 的私聊会话。");
            }
            else
            {
                // 对方发起 → 缓存消息，仅在侧边栏显示通知
                var conv = _conversations[peerId];
                conv.BufferedMessages.Add(("系统", $"\"{peerName}\" 发起了聊天，正在建立加密通道...", Color.DarkOrange));
                conv.UnreadCount++;
                SetStatus($"收到 {peerName} 的聊天请求。");
            }
            RefreshUserList();
        });

        _peer.MessageReceived += (peerId, msg) => Invoke(() =>
        {
            if (IsPeerBlocked(peerId))
            {
                SetStatus($"已拦截来自 {GetPeerDisplayName(peerId)} 的消息。");
                return;
            }

            string name = GetPeerDisplayName(peerId);

            if (_chatBoxes.ContainsKey(peerId))
            {
                // 标签页已打开 → 直接显示
                AppendToChat(peerId, name, msg, Color.DodgerBlue);
            }
            else if (_conversations.TryGetValue(peerId, out var conv))
            {
                // 标签页未打开 → 缓存
                conv.BufferedMessages.Add((name, msg, Color.DodgerBlue));
            }

            // 非当前标签页 → 增加未读计数
            if (tabConversations.SelectedTab?.Tag as string != peerId
                && _conversations.TryGetValue(peerId, out var c))
            {
                c.UnreadCount++;
                if (_chatTabs.TryGetValue(peerId, out var tab))
                    tab.Text = $"{c.DisplayName} ({c.UnreadCount})";
                RefreshUserList();
            }

            SetStatus($"收到 {name} 的消息。");
        });

        _peer.IdentityReceived += (peerId, peerName, identityId) => Invoke(() =>
        {
            if (IsPeerBlocked(peerId))
            {
                _ = _peer.SendIdentityRejectAsync(peerId);
                SetStatus($"已拦截来自 {GetPeerDisplayName(peerId)} 的身份识别卡交换请求。");
                return;
            }

            if (!IsValidIdentityId(identityId))
            {
                SetStatus($"收到来自 {peerName} 的无效身份识别卡，已忽略。");
                return;
            }

            bool isResponseToMyExchange = GetIdentityState(peerId) == IdentityExchangeState.Pending;
            string shortId = identityId[..Math.Min(8, identityId.Length)];
            string display = _trustedIdentities.TryGetValue(identityId, out var trustedName)
                ? trustedName
                : peerName;

            if (!isResponseToMyExchange && !_trustedIdentities.ContainsKey(identityId))
            {
                var consent = MessageBox.Show(
                    $"{peerName} 请求交换身份识别卡。\n身份指纹: {shortId}...\n是否同意交换？",
                    "身份识别卡交换",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (consent != DialogResult.Yes)
                {
                    _ = _peer.SendIdentityRejectAsync(peerId);
                    SetIdentityState(peerId, IdentityExchangeState.Idle);
                    if (_conversations.TryGetValue(peerId, out var rejectedConv))
                        rejectedConv.BufferedMessages.Add(("系统", "你已拒绝该身份识别卡交换请求。", Color.Red));
                    RefreshUserList();
                    SetStatus($"你已拒绝与 {peerName} 交换身份识别卡。");
                    return;
                }
            }

            if (!_trustedIdentities.ContainsKey(identityId))
            {
                _trustedIdentities[identityId] = peerName;
                SaveIdentityStore();
                SaveIdentityCardToFile(peerName, identityId);

                if (!isResponseToMyExchange)
                    _ = _peer.SendIdentityCardAsync(peerId);
            }

            SetIdentityState(peerId, IdentityExchangeState.Verified);
            UpsertIdentityTrust(peerId, peerName, identityId);

            RefreshUserList();
            SetStatus($"已与 {display} 完成身份识别（{shortId}...）。");
        });

        _peer.IdentityRejected += peerId => Invoke(() =>
        {
            SetIdentityState(peerId, IdentityExchangeState.Idle);
            string name = GetPeerDisplayName(peerId);
            if (_conversations.TryGetValue(peerId, out var conv))
                conv.BufferedMessages.Add(("系统", $"{name} 已拒绝交换身份识别卡。", Color.Red));
            SetStatus($"{name} 已拒绝交换身份识别卡。");
            MessageBox.Show($"{name} 已拒绝交换身份识别卡。", "身份识别", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });

        _peer.EncryptionEstablished += (peerId, peerName) => Invoke(() =>
        {
            if (_chatBoxes.ContainsKey(peerId))
            {
                AppendToChat(peerId, "系统", $"已与 \"{peerName}\" 建立端对端加密 (ECDH + AES-256-GCM)", Color.DarkOrange);
            }
            else if (_conversations.TryGetValue(peerId, out var conv))
            {
                conv.BufferedMessages.Add(("系统", $"已与 \"{peerName}\" 建立端对端加密", Color.DarkOrange));
            }
            UpdateEncryptionLabel();
        });

        _peer.IdentityLookupResultReceived += (found, peerId, peerName) => Invoke(async () =>
        {
            if (!found || string.IsNullOrWhiteSpace(peerId) || string.IsNullOrWhiteSpace(peerName))
            {
                MessageBox.Show("该身份识别卡对应用户当前不在线。", "搜人结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show($"发现在线用户：{peerName}。\n是否建立联络？", "搜人结果", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;

            try
            {
                _pendingOutgoingRequests.Add(peerId);
                await _peer.RequestChatAsync(peerId);
                SetStatus($"已向 {peerName} 发起联络请求。");
            }
            catch (Exception ex)
            {
                SetStatus($"发起联络失败：{ex.Message}");
            }
        });

        _peer.ClientListUpdated += list => Invoke(() =>
        {
            _onlineUsers = list;
            RefreshUserList();
        });

        _peer.ServerShutdown += () => Invoke(() =>
        {
            AppendToAll("系统", "服务器已关闭", Color.Red);
            ClearAllConversations();
            UpdateUI(connected: false, chatting: false);
            SetStatus("服务器已关闭，连接已断开。");
        });

        _peer.PeerDisconnected += peerId => Invoke(() =>
        {
            SetIdentityState(peerId, IdentityExchangeState.Idle);
            string name = GetPeerDisplayName(peerId);
            if (_chatBoxes.ContainsKey(peerId))
                AppendToChat(peerId, "系统", $"\"{name}\" 已断开连接", Color.Red);
            RemoveConversationTab(peerId);
            _conversations.Remove(peerId);
            _identityStates.Remove(peerId);
            _peerSafetyNumbers.Remove(peerId);
            RefreshUserList();
            if (_chatTabs.Count == 0 && _joinedGroupIds.Count == 0)
            {
                lblEncryption.Text = "🔓 未加密";
                lblEncryption.ForeColor = Color.Red;
                UpdateUI(connected: true, chatting: false);
            }
            UpdateEncryptionLabel();
            SetStatus("检测到会话中断，已更新会话状态。");
        });

        _peer.ServerBroadcastReceived += msg => Invoke(() =>
        {
            if (_chatBoxes.Count > 0)
                AppendToAll("📢 服务器公告", msg, Color.Magenta);
            else
                SetStatus($"收到服务器公告：{msg}");
        });

        _peer.GroupMessageReceived += (groupId, sender, msg) => Invoke(() =>
        {
            string groupKey = $"group:{groupId}";
            if (!_conversations.ContainsKey(groupKey))
            {
                string gn = _groupNamesById.TryGetValue(groupId, out var n) ? n : groupId;
                _conversations[groupKey] = new ConversationInfo
                {
                    PeerId = groupKey,
                    DisplayName = $"👥 {gn}",
                    IsGroup = true,
                };
            }
            string senderDisplay = sender == "系统" ? sender : BuildGroupSenderDisplayName(sender);
            AppendToChat(groupKey, senderDisplay, msg, sender == "系统" ? Color.DarkOrange : Color.DodgerBlue);
            if (tabConversations.SelectedTab?.Tag as string != groupKey
                && _conversations.TryGetValue(groupKey, out var gconv))
            {
                gconv.UnreadCount++;
                if (_chatTabs.TryGetValue(groupKey, out var gtab))
                    gtab.Text = $"{gconv.DisplayName} ({gconv.UnreadCount})";
                RefreshUserList();
            }

            SetStatus(sender == "系统" ? msg : $"群聊收到 {sender} 的消息。");
        });

        _peer.GroupStarted += (groupId, groupName, _) => Invoke(() =>
        {
            _joinedGroupIds.Add(groupId);
            _groupNamesById[groupId] = groupName;
            string groupKey = $"group:{groupId}";
            _conversations[groupKey] = new ConversationInfo
            {
                PeerId = groupKey,
                DisplayName = $"👥 {groupName}",
                IsGroup = true,
            };
            OpenConversationTab(groupKey);
            AppendToChat(groupKey, "系统", $"已加入群聊 \"{groupName}\"", Color.DarkOrange);
            UpdateUI(connected: true, chatting: true);
            UpdateEncryptionLabel();
            RefreshUserList();
            SetStatus($"已进入群聊 {groupName}。");
        });

        _peer.GroupEncryptionEstablished += groupId => Invoke(() =>
        {
            string groupKey = $"group:{groupId}";
            AppendToChat(groupKey, "系统", "群聊端对端加密已建立 (ECDH + AES-256-GCM)", Color.DarkOrange);
            UpdateEncryptionLabel();
        });

        _peer.GroupListUpdated += list => Invoke(() =>
        {
            _availableGroups = list;
            RefreshUserList();
        });

        _peer.GroupClosed += (groupId, groupName, reason) => Invoke(() =>
        {
            string key = $"group:{groupId}";
            RemoveConversationTab(key);
            _conversations.Remove(key);
            _joinedGroupIds.Remove(groupId);
            _groupNamesById.Remove(groupId);

            RefreshUserList();
            UpdateEncryptionLabel();
            bool hasChats = _chatTabs.Count > 0;
            UpdateUI(connected: true, chatting: hasChats);
            if (!hasChats)
            {
                lblEncryption.Text = "🔓 未加密";
                lblEncryption.ForeColor = Color.Red;
            }

            SetStatus($"群聊 {groupName} 已注销：{reason}");
        });

        _peer.Disconnected += () => Invoke(() =>
        {
            _joinedGroupIds.Clear();
            _groupNamesById.Clear();
            lblEncryption.Text = "🔓 未加密";
            lblEncryption.ForeColor = Color.Red;
            ClearAllConversations();
            UpdateUI(connected: false, chatting: false);
            SetStatus("已与服务器断开连接。");
        });

        try
        {
            StartConnectingVisual();
            await _peer.ConnectAsync(host, port, username);
            UpdateUI(connected: true, chatting: false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("用户名重复"))
        {
            MessageBox.Show("用户名重复，请修改为新的用户名后重试。", "登录提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _peer?.Disconnect();
            UpdateUI(connected: false, chatting: false);
            SetStatus("用户名重复，请更换后重试。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "连接错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateUI(connected: false, chatting: false);
            SetStatus($"连接服务器失败：{ex.Message}");
        }
        finally
        {
            StopConnectingVisual();
        }
    }

    /// <summary>
    /// 「断开」按钮：断开与服务器的连接。
    /// </summary>
    private void btnDisconnect_Click(object sender, EventArgs e)
    {
        _peer?.Disconnect();
        UpdateUI(connected: false, chatting: false);
        SetStatus("已手动断开服务器连接。");
    }

    /// <summary>
    /// 「结束聊天」按钮：结束当前标签页的聊天但保持连接。
    /// </summary>
    private async void btnEndChat_Click(object sender, EventArgs e)
    {
        if (_peer is null) return;
        string? chatId = tabConversations.SelectedTab?.Tag as string;
        if (chatId is null) return;

        try
        {
            if (chatId.StartsWith("group:"))
            {
                // 结束聊天仅关闭标签页，不退出群聊
            }
            else
            {
                await _peer.EndChatAsync(chatId);
            }
        }
        catch { }

        RemoveConversationTab(chatId);
        _conversations.Remove(chatId);
        RefreshUserList();
        bool hasChats = _chatTabs.Count > 0;
        UpdateUI(connected: true, chatting: hasChats);
        UpdateEncryptionLabel();
        if (!hasChats)
        {
            lblEncryption.Text = "🔓 未加密";
            lblEncryption.ForeColor = Color.Red;
        }
        if (chatId.StartsWith("group:"))
            SetStatus("已关闭群聊标签页，你仍在群中。可双击群列表重新打开。");
        else
            SetStatus("已结束当前会话。");
    }

    /// <summary>
    /// 「创建群聊」按钮：输入群聊名称并创建。
    /// </summary>
    private async void btnCreateGroup_Click(object sender, EventArgs e)
    {
        if (_peer is null || !_peer.IsConnected) return;
        string? groupName = ShowInputDialog("请输入群聊名称：", "创建群聊");
        if (string.IsNullOrWhiteSpace(groupName)) return;
        if (groupName.Length > MaxGroupNameLength)
        {
            SetStatus($"群聊名称过长，最多 {MaxGroupNameLength} 个字符。");
            return;
        }
        try
        {
            await _peer.CreateGroupAsync(groupName);
            SetStatus($"群聊 {groupName} 创建请求已发送。");
        }
        catch (Exception ex)
        {
            SetStatus($"创建群聊失败：{ex.Message}");
        }
    }

    // ── 用户列表 ───────────────────────────────────────────

    /// <summary>
    /// 刷新左侧列表：Telegram 风格显示会话/在线用户/群聊。
    /// </summary>
    private void RefreshUserList()
    {
        lstUsers.Items.Clear();
        _listItems.Clear();

        // ─ 会话区域
        lstUsers.Items.Add($"{(_collapseConversations ? "▶" : "▼")} 💬 会话 ({_conversations.Count})");
        _listItems.Add(new(ListItemType.Header, "section:conversations"));
        if (!_collapseConversations)
        {
            if (_conversations.Count == 0)
            {
                lstUsers.Items.Add("   （暂无会话）");
                _listItems.Add(new(ListItemType.Placeholder));
            }
            else
            {
                foreach (var conv in _conversations.Values.OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                {
                    bool tabOpen = _chatTabs.ContainsKey(conv.PeerId);
                    string badge = conv.UnreadCount > 0 ? $"  ({conv.UnreadCount})" : "";
                    string icon = conv.IsGroup ? "👥" : tabOpen ? "💬" : "🔔";
                    string normalizedName = conv.IsGroup ? StripLeadingListEmoji(conv.DisplayName) : conv.DisplayName;
                    lstUsers.Items.Add($" {icon} {normalizedName}{badge}");
                    _listItems.Add(new(ListItemType.Conversation, conv.PeerId, conv.DisplayName));
                }
            }
        }

        // ─ 在线用户（保留已在会话中的用户，便于感知在线状态与快速返回会话）
        var availableUsers = _onlineUsers
            .OrderBy(u => u.Username, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        lstUsers.Items.Add($"{(_collapseOnlineUsers ? "▶" : "▼")} 👤 在线 ({availableUsers.Count})");
        _listItems.Add(new(ListItemType.Header, "section:online"));
        if (!_collapseOnlineUsers)
        {
            if (availableUsers.Count == 0)
            {
                lstUsers.Items.Add("   （暂无在线用户）");
                _listItems.Add(new(ListItemType.Placeholder));
            }
            else
            {
                foreach (var user in availableUsers)
                {
                    lstUsers.Items.Add($" 👤 {user.Username}");
                    _listItems.Add(new(ListItemType.OnlineUser, user.Id, user.Username));
                }
            }
        }

        // ─ 群聊
        lstUsers.Items.Add($"{(_collapseGroups ? "▶" : "▼")} 👥 群聊 ({_availableGroups.Count})");
        _listItems.Add(new(ListItemType.Header, "section:groups"));
        if (!_collapseGroups)
        {
            if (_availableGroups.Count == 0)
            {
                lstUsers.Items.Add("   （暂无可加入群聊）");
                _listItems.Add(new(ListItemType.Placeholder));
            }
            else
            {
                foreach (var group in _availableGroups.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    lstUsers.Items.Add($" 👥 [{group.MemberCount}人] {group.Name}");
                    _listItems.Add(new(ListItemType.Group, group.Id, group.Name));
                }
            }
        }

        SyncCurrentConversationSelection();
    }

    /// <summary>
    /// 单击会话项 → 打开/切换到对应标签页。
    /// </summary>
    private void lstUsers_Click(object? sender, EventArgs e)
    {
        int idx = lstUsers.SelectedIndex;
        if (idx < 0 || idx >= _listItems.Count) return;
        var item = _listItems[idx];
        if (item.Type == ListItemType.Header)
        {
            lstUsers.ClearSelected();
            switch (item.Id)
            {
                case "section:conversations":
                    _collapseConversations = !_collapseConversations;
                    break;
                case "section:online":
                    _collapseOnlineUsers = !_collapseOnlineUsers;
                    break;
                case "section:groups":
                    _collapseGroups = !_collapseGroups;
                    break;
            }
            RefreshUserList();
            return;
        }

        if (item.Type == ListItemType.Conversation && item.Id is not null)
        {
            OpenConversationTab(item.Id);
            UpdateUI(connected: true, chatting: true);
            return;
        }

        if (item.Type == ListItemType.OnlineUser && item.Id is not null && _conversations.ContainsKey(item.Id))
        {
            OpenConversationTab(item.Id);
            UpdateUI(connected: true, chatting: true);
        }
    }

    /// <summary>
    /// 双击在线用户 → 发起新聊天；双击群聊 → 加入。
    /// </summary>
    private async void lstUsers_DoubleClick(object? sender, EventArgs e)
    {
        int idx = lstUsers.SelectedIndex;
        if (idx < 0 || idx >= _listItems.Count) return;
        var item = _listItems[idx];
        if (item.Type is ListItemType.Header or ListItemType.Placeholder)
            return;

        switch (item.Type)
        {
            case ListItemType.OnlineUser when _peer is not null && item.Id is not null:
                if (_pendingOutgoingRequests.Contains(item.Id)) return;
                if (_conversations.ContainsKey(item.Id))
                {
                    OpenConversationTab(item.Id);
                    UpdateUI(connected: true, chatting: true);
                    return;
                }
                _pendingOutgoingRequests.Add(item.Id);
                SetStatus($"正在请求与 {item.Name} 聊天...");
                try
                {
                    await _peer.RequestChatAsync(item.Id);
                }
                catch (Exception ex)
                {
                    _pendingOutgoingRequests.Remove(item.Id);
                    SetStatus($"请求失败：{ex.Message}");
                }
                break;

            case ListItemType.Group when _peer is not null && item.Id is not null:
                if (_joinedGroupIds.Contains(item.Id))
                {
                    string gKey = $"group:{item.Id}";
                    if (!_conversations.ContainsKey(gKey))
                    {
                        string gn = _groupNamesById.TryGetValue(item.Id, out var n) ? n : item.Name ?? item.Id;
                        _conversations[gKey] = new ConversationInfo
                        {
                            PeerId = gKey,
                            DisplayName = $"👥 {gn}",
                            IsGroup = true,
                        };
                    }
                    OpenConversationTab(gKey);
                    UpdateUI(connected: true, chatting: true);
                    UpdateEncryptionLabel();
                    SetStatus($"已重新打开群聊。");
                    return;
                }
                try
                {
                    await _peer.JoinGroupAsync(item.Id);
                    SetStatus($"加入群聊 {item.Name} 请求已发送。");
                }
                catch (Exception ex)
                {
                    SetStatus($"加入群聊失败：{ex.Message}");
                }
                break;

            case ListItemType.Conversation when item.Id is not null:
                // 双击会话也能打开
                OpenConversationTab(item.Id);
                UpdateUI(connected: true, chatting: true);
                break;
        }
    }

    /// <summary>
    /// 自定义绘制列表项：分隔符用粗体，未读消息用加粗。
    /// </summary>
    private void lstUsers_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _listItems.Count) return;
        var item = _listItems[e.Index];
        string text = lstUsers.Items[e.Index]?.ToString() ?? "";
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        Color background = item.Type is ListItemType.Header or ListItemType.Placeholder
            ? Color.FromArgb(244, 245, 247)
            : selected ? Color.FromArgb(220, 235, 252) : Color.FromArgb(244, 245, 247);
        using (var bgBrush = new SolidBrush(background))
            e.Graphics.FillRectangle(bgBrush, e.Bounds);

        Font font;
        Color textColor;
        if (item.Type == ListItemType.Header)
        {
            font = new Font(e.Font ?? lstUsers.Font, FontStyle.Bold);
            textColor = Color.FromArgb(128, 134, 139);
        }
        else if (item.Type == ListItemType.Placeholder)
        {
            font = new Font(e.Font ?? lstUsers.Font, FontStyle.Italic);
            textColor = Color.FromArgb(128, 134, 139);
        }
        else if (item.Type == ListItemType.Conversation
            && item.Id is not null
            && _conversations.TryGetValue(item.Id, out var conv)
            && conv.UnreadCount > 0)
        {
            font = new Font(e.Font ?? lstUsers.Font, FontStyle.Bold);
            textColor = Color.FromArgb(51, 144, 236);
        }
        else
        {
            font = e.Font ?? lstUsers.Font;
            textColor = Color.FromArgb(24, 33, 43);
        }

        using var brush = new SolidBrush(textColor);
        if (item.Type is ListItemType.Header or ListItemType.Conversation or ListItemType.OnlineUser or ListItemType.Group
            && TrySplitPrefixWithEmoji(text, out var leadingPrefix, out var emojiPrefix, out var remainder))
        {
            float x = e.Bounds.Left + 2;
            float y = e.Bounds.Top + 3;
            if (!string.IsNullOrEmpty(leadingPrefix))
            {
                e.Graphics.DrawString(leadingPrefix, font, brush, x, y, StringFormat.GenericDefault);
                x += e.Graphics.MeasureString(leadingPrefix + " ", font).Width;
            }
            e.Graphics.DrawString(emojiPrefix, _emojiFont, brush, x, y, StringFormat.GenericDefault);
            float emojiWidth = e.Graphics.MeasureString(emojiPrefix + " ", _emojiFont).Width;
            e.Graphics.DrawString(remainder, font, brush, x + emojiWidth, y, StringFormat.GenericDefault);
        }
        else
        {
            e.Graphics.DrawString(text, font, brush, e.Bounds, StringFormat.GenericDefault);
        }
        e.DrawFocusRectangle();
    }

    private static bool TrySplitPrefixWithEmoji(string text, out string leadingPrefix, out string emojiPrefix, out string remainder)
    {
        leadingPrefix = "";
        emojiPrefix = "";
        remainder = text;

        string trimmed = text.TrimStart();
        if (trimmed.StartsWith("▼ ", StringComparison.Ordinal) || trimmed.StartsWith("▶ ", StringComparison.Ordinal))
        {
            leadingPrefix = trimmed[..1];
            trimmed = trimmed[2..].TrimStart();
        }

        int spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex <= 0)
            return false;

        string prefix = trimmed[..spaceIndex];
        if (prefix is not ("💬" or "🔔" or "👤" or "👥"))
            return false;

        emojiPrefix = prefix;
        remainder = trimmed[(spaceIndex + 1)..];
        return true;
    }

    private static string StripLeadingListEmoji(string value)
    {
        string trimmed = value.TrimStart();
        foreach (string prefix in new[] { "👥 ", "💬 ", "🔔 ", "👤 " })
        {
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
                return trimmed[prefix.Length..];
        }

        return value;
    }

    // ── 消息发送 ───────────────────────────────────────────

    /// <summary>
    /// 「发送」按钮：加密并发送消息到当前标签页的会话。
    /// </summary>
    private async void btnSend_Click(object sender, EventArgs e)
    {
        if (_peer is null || !_peer.IsConnected || string.IsNullOrWhiteSpace(txtMessage.Text))
            return;

        string? chatId = tabConversations.SelectedTab?.Tag as string;
        if (chatId is null) return;

        string msg = txtMessage.Text.Trim();
        if (msg.Length > MaxMessageLength)
        {
            AppendToChat(chatId, "系统", $"消息过长，最多 {MaxMessageLength} 个字符。", Color.Red);
            return;
        }
        txtMessage.Clear();

        try
        {
            if (chatId.StartsWith("group:"))
            {
                string groupId = chatId["group:".Length..];
                await _peer.SendGroupMessageAsync(groupId, msg);
                AppendToChat(chatId, "我", msg, Color.ForestGreen);
                SetStatus("群聊消息发送成功。");
            }
            else if (_peer.IsEncryptedWith(chatId))
            {
                await _peer.SendMessageAsync(chatId, msg);
                AppendToChat(chatId, "我", msg, Color.ForestGreen);
                SetStatus($"消息已发送给 {GetPeerDisplayName(chatId)}。");
            }
            else
            {
                AppendToChat(chatId, "系统", "加密通道尚未建立，请稍候...", Color.DarkOrange);
                SetStatus("发送失败：加密通道尚未建立。");
            }
        }
        catch (Exception ex)
        {
            AppendToChat(chatId, "系统", $"发送失败：{ex.Message}", Color.Red);
            SetStatus($"发送失败：{ex.Message}");
        }
    }

    /// <summary>
    /// Enter 发送消息。
    /// </summary>
    private void txtMessage_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.SuppressKeyPress = true;
            btnSend.PerformClick();
        }
    }

    private void InitializeEmojiMenu()
    {
        string[] emojis =
        [
            "😀", "😁", "😂", "😊", "😍", "😘", "😎", "🤔", "😭", "😡",
            "👍", "👎", "👏", "🙏", "💪", "🎉", "🔥", "❤️", "💔", "✅",
            "❌", "⭐", "⚠️", "👀", "💬", "🎁", "🍀", "☕", "🍻", "🌈"
        ];

        foreach (string emoji in emojis)
        {
            var item = new ToolStripMenuItem(emoji) { Font = _emojiFont };
            item.Click += (_, _) => AppendEmojiToInput(emoji);
            _emojiMenu.Items.Add(item);
        }
    }

    private void btnEmoji_Click(object sender, EventArgs e)
    {
        if (!btnEmoji.Enabled) return;
        _emojiMenu.Show(btnEmoji, new Point(0, -_emojiMenu.Height));
    }

    private void AppendEmojiToInput(string emoji)
    {
        int start = txtMessage.SelectionStart;
        txtMessage.Text = txtMessage.Text.Insert(start, emoji);
        txtMessage.SelectionStart = start + emoji.Length;
        txtMessage.Focus();
    }

    // ── 辅助方法 ───────────────────────────────────────────

    /// <summary>
    /// 简单输入对话框。
    /// </summary>
    private static string? ShowInputDialog(string prompt, string title)
    {
        using var form = new Form();
        form.Text = title;
        form.Size = new Size(350, 160);
        form.StartPosition = FormStartPosition.CenterParent;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        var label = new Label { Text = prompt, Left = 12, Top = 12, AutoSize = true };
        var textBox = new TextBox { Left = 12, Top = 40, Width = 300 };
        var btnOk = new Button { Text = "确定", Left = 146, Top = 76, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "取消", Left = 230, Top = 76, DialogResult = DialogResult.Cancel };
        form.Controls.AddRange([label, textBox, btnOk, btnCancel]);
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;
        return form.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text) ? textBox.Text.Trim() : null;
    }

    // ── 会话标签页管理 ───────────────────────────────────

    private static string GetGroupKey(string groupId) => $"group:{groupId}";

    private string GetPeerDisplayName(string peerId) =>
        _conversations.TryGetValue(peerId, out var conv) ? StripConversationStatus(conv.DisplayName) : peerId;

    /// <summary>
    /// 打开会话标签页：如果已存在则切换，否则创建并刷新缓存消息。
    /// </summary>
    private void OpenConversationTab(string chatId)
    {
        if (_chatTabs.ContainsKey(chatId))
        {
            tabConversations.SelectedTab = _chatTabs[chatId];
            return;
        }
        if (!_conversations.TryGetValue(chatId, out var conv)) return;

        CreateConversationTab(chatId, conv.DisplayName);

        // 刷新缓存消息
        if (_chatBoxes.TryGetValue(chatId, out var rtb))
        {
            foreach (var (sender, message, color) in conv.BufferedMessages)
                AppendToRichTextBox(rtb, sender, message, color);
        }
        conv.BufferedMessages.Clear();
        conv.UnreadCount = 0;
        RefreshUserList();
        UpdateEncryptionLabel();
    }

    private void CreateConversationTab(string chatId, string displayName)
    {
        if (_chatTabs.ContainsKey(chatId))
        {
            tabConversations.SelectedTab = _chatTabs[chatId];
            return;
        }
        var rtb = new RichTextBox
        {
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill,
            Font = new Font("微软雅黑", 10F),
            ReadOnly = true,
        };
        var tab = new TabPage(displayName) { Tag = chatId };
        tab.Controls.Add(rtb);
        tabConversations.TabPages.Add(tab);
        tabConversations.SelectedTab = tab;
        _chatTabs[chatId] = tab;
        _chatBoxes[chatId] = rtb;
    }

    private void RemoveConversationTab(string chatId)
    {
        if (_chatTabs.TryGetValue(chatId, out var tab))
        {
            tabConversations.TabPages.Remove(tab);
            tab.Dispose();
            _chatTabs.Remove(chatId);
            _chatBoxes.Remove(chatId);
        }
    }

    private void ClearAllConversations()
    {
        tabConversations.TabPages.Clear();
        foreach (var rtb in _chatBoxes.Values)
            rtb.Dispose();
        _chatTabs.Clear();
        _chatBoxes.Clear();
        _conversations.Clear();
        _identityStates.Clear();
        _peerSafetyNumbers.Clear();
        _pendingOutgoingRequests.Clear();
        _listItems.Clear();
    }

    private void tabConversations_SelectedIndexChanged(object? sender, EventArgs e)
    {
        string? chatId = tabConversations.SelectedTab?.Tag as string;
        if (chatId is not null && _conversations.TryGetValue(chatId, out var conv))
        {
            // 清除未读计数
            conv.UnreadCount = 0;
            if (_chatTabs.TryGetValue(chatId, out var tab))
                tab.Text = conv.DisplayName;
            RefreshUserList();
        }
        UpdateEncryptionLabel();
        bool hasCurrent = tabConversations.SelectedTab is not null;
        txtMessage.Enabled = hasCurrent;
        btnSend.Enabled = hasCurrent;
        btnEmoji.Enabled = hasCurrent;
        btnExchangeIdentity.Enabled = hasCurrent && chatId is not null && !chatId.StartsWith("group:");
    }

    // ── 消息显示 ───────────────────────────────────────

    private void AppendToChat(string chatId, string sender, string message, Color color)
    {
        if (_chatBoxes.TryGetValue(chatId, out var rtb))
            AppendToRichTextBox(rtb, sender, message, color);
    }

    private void AppendToAll(string sender, string message, Color color)
    {
        foreach (var rtb in _chatBoxes.Values)
            AppendToRichTextBox(rtb, sender, message, color);
    }

    /// <summary>
    /// 在指定 RichTextBox 中追加一条带时间戳和颜色的消息。
    /// </summary>
    private void AppendToRichTextBox(RichTextBox rtb, string sender, string message, Color color)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        bool isMine = string.Equals(sender, "我", StringComparison.Ordinal);

        rtb.SelectionStart = rtb.TextLength;
        rtb.SelectionAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        rtb.SelectionColor = Color.Gray;
        rtb.AppendText($"[{timestamp}] ");

        rtb.SelectionStart = rtb.TextLength;
        rtb.SelectionAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        rtb.SelectionColor = color;
        rtb.AppendText($"{sender}: ");

        rtb.SelectionStart = rtb.TextLength;
        rtb.SelectionAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        rtb.SelectionColor = ForeColor;
        rtb.AppendText($"{message}\n");

        rtb.SelectionAlignment = HorizontalAlignment.Left;

        rtb.ScrollToCaret();
    }

    /// <summary>
    /// 根据连接和聊天状态更新界面控件。
    /// </summary>
    private void UpdateUI(bool connected, bool chatting)
    {
        // 连接设置区
        txtUsername.Enabled = !connected;
        txtIP.Enabled = !connected;
        txtPort.Enabled = !connected;
        btnConnect.Enabled = !connected;
        btnDisconnect.Enabled = connected;
        btnEndChat.Enabled = connected && chatting;

        // 用户列表与群聊
        lstUsers.Enabled = connected;
        btnCreateGroup.Enabled = connected;
        if (!connected)
        {
            lstUsers.Items.Clear();
            _onlineUsers.Clear();
            _availableGroups.Clear();
            _joinedGroupIds.Clear();
            _groupNamesById.Clear();
            ClearAllConversations();
        }

        // 消息输入区
        bool hasCurrent = chatting && tabConversations.SelectedTab is not null;
        txtMessage.Enabled = hasCurrent;
        btnSend.Enabled = hasCurrent;
        btnEmoji.Enabled = hasCurrent;
        string? chatId = tabConversations.SelectedTab?.Tag as string;
        btnExchangeIdentity.Enabled = hasCurrent && chatId is not null && !chatId.StartsWith("group:");
    }

    /// <summary>
    /// 根据当前标签页更新加密状态标签。
    /// </summary>
    private void UpdateEncryptionLabel()
    {
        string? chatId = tabConversations.SelectedTab?.Tag as string;
        if (chatId is not null && chatId.StartsWith("group:"))
        {
            string gid = chatId["group:".Length..];
            string gname = _groupNamesById.TryGetValue(gid, out var n) ? n : gid;
            if (_peer?.IsGroupEncryptedWith(gid) == true)
            {
                lblEncryption.Text = $"🔒 群聊: \"{gname}\" 端对端加密";
                lblEncryption.ForeColor = Color.Green;
            }
            else
            {
                lblEncryption.Text = $"👥 群聊: \"{gname}\" (加密建立中...)";
                lblEncryption.ForeColor = Color.DarkOrange;
            }
        }
        else if (chatId is not null && _peer?.IsEncryptedWith(chatId) == true)
        {
            string name = GetPeerDisplayName(chatId);
            lblEncryption.Text = $"🔒 与 \"{name}\" 端对端加密";
            lblEncryption.ForeColor = Color.Green;
        }
        else if (chatId is not null)
        {
            string name = GetPeerDisplayName(chatId);
            lblEncryption.Text = $"🔑 与 \"{name}\" 加密建立中...";
            lblEncryption.ForeColor = Color.DarkOrange;
        }
        else
        {
            lblEncryption.Text = "🔓 未加密";
            lblEncryption.ForeColor = Color.Red;
        }
    }

    /// <summary>
    /// 窗体关闭时释放资源。
    /// </summary>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _connectAnimationTimer.Stop();
        _emojiListFont.Dispose();
        _peer?.Dispose();
        base.OnFormClosed(e);
    }

    private void StartConnectingVisual()
    {
        _isConnecting = true;
        _connectDotCount = 0;
        txtUsername.Enabled = false;
        txtIP.Enabled = false;
        txtPort.Enabled = false;
        btnConnect.Enabled = false;
        SetStatus("正在连接服务器...");
        _connectAnimationTimer.Start();
        AnimateConnectingState();
    }

    private void StopConnectingVisual()
    {
        _isConnecting = false;
        _connectAnimationTimer.Stop();
        btnConnect.Text = "连接";
    }

    private void AnimateConnectingState()
    {
        if (!_isConnecting) return;
        _connectDotCount = (_connectDotCount + 1) % 4;
        btnConnect.Text = $"连接中{new string('.', _connectDotCount)}";
    }

    private void SetStatus(string message)
    {
        lblStatus.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
    }

    private void AdjustLeftPanelWidthForTitle()
    {
        if (splitContainer.Width <= 0) return;

        int measured = TextRenderer.MeasureText(lblUsersTitle.Text, lblUsersTitle.Font).Width;
        int desired = measured + lblUsersTitle.Padding.Left + lblUsersTitle.Padding.Right + 28;
        desired = Math.Max(desired, 260);

        // 保证右侧聊天区至少可用
        int maxAllowed = Math.Max(260, splitContainer.Width - 420);
        int target = Math.Min(desired, maxAllowed);

        splitContainer.Panel1MinSize = Math.Min(target, Math.Max(220, splitContainer.Width - 320));
        if (splitContainer.SplitterDistance < target)
            splitContainer.SplitterDistance = target;
    }

    private string BuildConversationDisplayName(string peerName, string peerId)
    {
        if (_peerIdentityMap.TryGetValue(peerId, out var identityId)
            && _identityRemarks.TryGetValue(identityId, out var remark)
            && !string.IsNullOrWhiteSpace(remark))
            peerName = remark;

        if (GetIdentityState(peerId) == IdentityExchangeState.Verified
            && _peerIdentityMap.TryGetValue(peerId, out var id)
            && _trustedIdentities.ContainsKey(id))
            return IsPeerBlocked(peerId) ? $"{peerName}（已验证｜已屏蔽）" : $"{peerName}（已验证）";
        return IsPeerBlocked(peerId) ? $"{peerName}（未验证｜已屏蔽）" : $"{peerName}（未验证）";
    }

    private static string StripConversationStatus(string displayName)
    {
        return displayName
            .Replace("（已验证｜已屏蔽）", "")
            .Replace("（未验证｜已屏蔽）", "")
            .Replace("（已验证）", "")
            .Replace("（未验证）", "")
            .Trim();
    }

    private async void btnExchangeIdentity_Click(object sender, EventArgs e)
    {
        string? chatId = tabConversations.SelectedTab?.Tag as string;
        if (chatId is null || chatId.StartsWith("group:") || _peer is null) return;

        string peerName = GetPeerDisplayName(chatId);
        var result = MessageBox.Show(
            $"将与 \"{peerName}\" 交换身份识别卡，是否继续？",
            "身份识别卡交换确认",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);

        if (result != DialogResult.OK)
        {
            SetStatus("已取消交换身份识别卡。");
            return;
        }

        IdentityExchangeState state = GetIdentityState(chatId);
        string? identityId = _peerIdentityMap.TryGetValue(chatId, out var id) ? id : null;
        bool verified = state == IdentityExchangeState.Verified
            && identityId is not null
            && _trustedIdentities.ContainsKey(identityId);
        if (verified)
        {
            MessageBox.Show($"完整 IdentityId:\n{identityId}", "身份指纹", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_peer.IsEncryptedWith(chatId))
        {
            MessageBox.Show("请等待端对端加密建立后再交换身份识别卡。", "身份识别", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (state == IdentityExchangeState.Pending)
        {
            MessageBox.Show("身份识别请求已发送，请等待对方确认。", "身份识别", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await _peer.SendIdentityCardAsync(chatId);
        SetIdentityState(chatId, IdentityExchangeState.Pending);
        MessageBox.Show("已发送身份识别卡交换请求，等待对方确认。", "身份识别", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void btnSearchIdentity_Click(object? sender, EventArgs e)
    {
        if (_peer is null || !_peer.IsConnected)
        {
            MessageBox.Show("请先连接服务器。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var ofd = new OpenFileDialog
        {
            Title = "选择身份识别卡",
            InitialDirectory = _identityCardsDir,
            Filter = "Identity Card (*.umid)|*.umid|JSON (*.json)|*.json|All Files (*.*)|*.*"
        };

        if (ofd.ShowDialog() != DialogResult.OK) return;

        try
        {
            var card = JsonSerializer.Deserialize<IdentityCardFile>(File.ReadAllText(ofd.FileName));
            if (card is null || string.IsNullOrWhiteSpace(card.IdentityId))
                throw new InvalidOperationException("身份识别卡文件无效。");

            await _peer.LookupIdentityAsync(card.IdentityId);
            SetStatus($"正在搜索身份：{card.DisplayName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "识别卡错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void btnIdentitySearch_Click(object sender, EventArgs e)
        => btnSearchIdentity_Click(sender, e);

    private void btnProtocolSelfCheck_Click(object sender, EventArgs e)
    {
        string report = ProtocolSelfCheck.Run();
        MessageBox.Show(report, "安全协议自检", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SaveIdentityCardToFile(string displayName, string identityId)
    {
        try
        {
            var safeName = string.Concat(displayName.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch))).Trim();
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "peer";
            string fileName = $"{safeName}_{identityId[..Math.Min(8, identityId.Length)]}.umid";
            string fullPath = Path.Combine(_identityCardsDir, fileName);
            var card = new IdentityCardFile(displayName, identityId, DateTime.UtcNow);
            File.WriteAllText(fullPath, JsonSerializer.Serialize(card));
        }
        catch { }
    }

    private void UpsertIdentityTrust(string peerId, string peerName, string identityId)
    {
        _peerIdentityMap[peerId] = identityId;
        if (_conversations.TryGetValue(peerId, out var conv))
            conv.DisplayName = BuildConversationDisplayName(peerName, peerId);
        if (_chatTabs.TryGetValue(peerId, out var tab) && _conversations.TryGetValue(peerId, out var c))
            tab.Text = c.UnreadCount > 0 ? $"{c.DisplayName} ({c.UnreadCount})" : c.DisplayName;
    }

    private void SyncCurrentConversationSelection()
    {
        string? chatId = tabConversations.SelectedTab?.Tag as string;
        if (chatId is null)
        {
            lstUsers.ClearSelected();
            return;
        }

        for (int i = 0; i < _listItems.Count; i++)
        {
            var item = _listItems[i];
            if (item.Type == ListItemType.Conversation && item.Id == chatId)
            {
                lstUsers.SelectedIndex = i;
                return;
            }
        }

        lstUsers.ClearSelected();
    }

    private IdentityExchangeState GetIdentityState(string peerId)
        => _identityStates.TryGetValue(peerId, out var state) ? state : IdentityExchangeState.Idle;

    private void SetIdentityState(string peerId, IdentityExchangeState state)
        => _identityStates[peerId] = state;

    private static bool IsValidIdentityId(string identityId)
    {
        if (string.IsNullOrWhiteSpace(identityId) || identityId.Length != IdentityIdLength)
            return false;
        foreach (char ch in identityId)
        {
            if (!Uri.IsHexDigit(ch)) return false;
        }
        return true;
    }

    private static bool IsReservedUsername(string username)
    {
        string normalized = username.Trim();
        if (ReservedUsernames.Contains(normalized))
            return true;

        foreach (string keyword in ReservedUsernameKeywords)
        {
            if (normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private string _peerNamesFromConversations(string peerId)
        => _conversations.TryGetValue(peerId, out var conv) ? StripConversationStatus(conv.DisplayName) : peerId;

    private string BuildGroupSenderDisplayName(string senderUsername)
    {
        var peer = _onlineUsers.FirstOrDefault(u => string.Equals(u.Username, senderUsername, StringComparison.OrdinalIgnoreCase));
        if (peer is null)
            return senderUsername;

        if (_peerIdentityMap.TryGetValue(peer.Id, out var identityId)
            && _identityRemarks.TryGetValue(identityId, out var remark)
            && !string.IsNullOrWhiteSpace(remark))
            senderUsername = remark;

        if (_peerIdentityMap.TryGetValue(peer.Id, out var verifyIdentityId)
            && _trustedIdentities.ContainsKey(verifyIdentityId))
            return $"{senderUsername}（已验证）";

        return senderUsername;
    }

    private void InitializeUserListContextMenu()
    {
        var renameItem = new ToolStripMenuItem("修改备注") { Name = "renameItem" };
        renameItem.Click += (_, _) => ModifyPeerRemark();

        var blockItem = new ToolStripMenuItem("屏蔽用户") { Name = "blockItem" };
        blockItem.Click += (_, _) => TogglePeerBlock();

        var dissolveGroupItem = new ToolStripMenuItem("注销群聊") { Name = "dissolveGroupItem", Visible = false };
        dissolveGroupItem.Click += async (_, _) => await DissolveGroupFromMenuAsync();

        var leaveGroupItem = new ToolStripMenuItem("退出群聊") { Name = "leaveGroupItem", Visible = false };
        leaveGroupItem.Click += async (_, _) => await LeaveGroupFromMenuAsync();

        _userListMenu.Items.Add(renameItem);
        _userListMenu.Items.Add(new ToolStripSeparator());
        _userListMenu.Items.Add(blockItem);
        _userListMenu.Items.Add(new ToolStripSeparator { Name = "groupSeparator", Visible = false });
        _userListMenu.Items.Add(leaveGroupItem);
        _userListMenu.Items.Add(dissolveGroupItem);

        lstUsers.MouseUp += lstUsers_MouseUp;
    }

    private void lstUsers_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;

        int idx = lstUsers.IndexFromPoint(e.Location);
        if (idx < 0 || idx >= _listItems.Count) return;

        lstUsers.SelectedIndex = idx;
        var item = _listItems[idx];
        if (item.Id is null || item.Type is ListItemType.Header or ListItemType.Placeholder)
            return;

        _contextMenuPeerId = null;
        _contextMenuGroupId = null;

        bool isPeerItem = (item.Type is ListItemType.Conversation or ListItemType.OnlineUser)
            && !item.Id.StartsWith("group:", StringComparison.Ordinal);
        bool isGroupItem = item.Type == ListItemType.Group
            || (item.Type == ListItemType.Conversation && item.Id.StartsWith("group:", StringComparison.Ordinal));

        if (!isPeerItem && !isGroupItem)
            return;

        if (_userListMenu.Items.Find("renameItem", false).FirstOrDefault() is ToolStripMenuItem renameItem)
            renameItem.Visible = isPeerItem;
        if (_userListMenu.Items.Find("blockItem", false).FirstOrDefault() is ToolStripMenuItem blockItem)
        {
            blockItem.Visible = isPeerItem;
            if (isPeerItem)
                blockItem.Text = IsPeerBlocked(item.Id) ? "取消屏蔽" : "屏蔽用户";
        }

        string? targetGroupId = isGroupItem ? ResolveGroupIdForContextItem(item) : null;
        bool isJoinedGroup = isGroupItem && !string.IsNullOrWhiteSpace(targetGroupId) && _joinedGroupIds.Contains(targetGroupId);

        if (_userListMenu.Items.Find("groupSeparator", false).FirstOrDefault() is ToolStripSeparator groupSeparator)
            groupSeparator.Visible = isJoinedGroup;

        if (_userListMenu.Items.Find("dissolveGroupItem", false).FirstOrDefault() is ToolStripMenuItem dissolveItem)
            dissolveItem.Visible = isJoinedGroup;

        if (_userListMenu.Items.Find("leaveGroupItem", false).FirstOrDefault() is ToolStripMenuItem leaveItem)
            leaveItem.Visible = isJoinedGroup;

        if (isPeerItem)
        {
            _contextMenuPeerId = item.Id;
            if (_userListMenu.Items.Find("renameItem", false).FirstOrDefault() is ToolStripMenuItem rename)
                rename.Enabled = IsPeerIdentityVerified(item.Id);
        }
        else
        {
            _contextMenuGroupId = isJoinedGroup ? targetGroupId : null;
            if (_userListMenu.Items.Find("dissolveGroupItem", false).FirstOrDefault() is ToolStripMenuItem dissolve)
                dissolve.Enabled = _peer is not null && _peer.IsConnected && !string.IsNullOrWhiteSpace(_contextMenuGroupId);
            if (_userListMenu.Items.Find("leaveGroupItem", false).FirstOrDefault() is ToolStripMenuItem leave)
                leave.Enabled = _peer is not null && _peer.IsConnected && !string.IsNullOrWhiteSpace(_contextMenuGroupId);
        }

        _userListMenu.Show(lstUsers, e.Location);
    }

    private string? ResolveGroupIdForContextItem(ListItem item)
    {
        if (item.Type == ListItemType.Group)
            return item.Id;

        if (item.Type == ListItemType.Conversation && item.Id is not null && item.Id.StartsWith("group:", StringComparison.Ordinal))
            return item.Id["group:".Length..];

        return null;
    }

    private async Task DissolveGroupFromMenuAsync()
    {
        if (_peer is null || !_peer.IsConnected)
            return;
        if (string.IsNullOrWhiteSpace(_contextMenuGroupId))
        {
            MessageBox.Show("当前无法确定群聊标识。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var result = MessageBox.Show(
            "确认注销该群聊？该操作会将当前在线成员全部踢出。",
            "注销群聊",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
            return;

        try
        {
            await _peer.DissolveGroupAsync(_contextMenuGroupId);
            SetStatus("注销群聊请求已发送。仅创建者可执行成功。");
        }
        catch (Exception ex)
        {
            SetStatus($"注销群聊失败：{ex.Message}");
        }
    }

    private async Task LeaveGroupFromMenuAsync()
    {
        if (_peer is null || !_peer.IsConnected || string.IsNullOrWhiteSpace(_contextMenuGroupId))
            return;

        var result = MessageBox.Show(
            "确认退出该群聊？若你是管理员，将自动移交管理权。",
            "退出群聊",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
            return;

        try
        {
            string groupId = _contextMenuGroupId;
            await _peer.LeaveGroupAsync(groupId);
            string key = $"group:{groupId}";
            RemoveConversationTab(key);
            _conversations.Remove(key);
            _joinedGroupIds.Remove(groupId);
            _groupNamesById.Remove(groupId);
            RefreshUserList();
            UpdateEncryptionLabel();
            bool hasChats = _chatTabs.Count > 0;
            UpdateUI(connected: true, chatting: hasChats);
            SetStatus("已退出群聊。若你是管理员，已触发管理移交。");
        }
        catch (Exception ex)
        {
            SetStatus($"退出群聊失败：{ex.Message}");
        }
    }

    private void ModifyPeerRemark()
    {
        if (_contextMenuPeerId is null) return;
        string peerId = _contextMenuPeerId;

        if (!IsPeerIdentityVerified(peerId))
        {
            MessageBox.Show("需先完成身份识别卡交换后才能修改备注。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_peerIdentityMap.TryGetValue(peerId, out var identityId) || string.IsNullOrWhiteSpace(identityId))
        {
            MessageBox.Show("未找到该用户身份标识，无法修改备注。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string current = _identityRemarks.TryGetValue(identityId, out var remark) ? remark : GetPeerDisplayName(peerId);
        string? newRemark = ShowInputDialog($"请输入备注（当前：{current}）：", "修改备注");
        if (string.IsNullOrWhiteSpace(newRemark)) return;

        _identityRemarks[identityId] = newRemark.Trim();
        SaveIdentityStore();

        if (_conversations.TryGetValue(peerId, out var conv))
        {
            string baseName = StripConversationStatus(conv.DisplayName);
            conv.DisplayName = BuildConversationDisplayName(baseName, peerId);
        }

        RefreshUserList();
        SetStatus($"已更新 {GetPeerDisplayName(peerId)} 的备注。");
    }

    private void TogglePeerBlock()
    {
        if (_contextMenuPeerId is null) return;
        string peerId = _contextMenuPeerId;

        bool blockedNow;
        if (_blockedPeers.Contains(peerId))
        {
            _blockedPeers.Remove(peerId);
            blockedNow = false;
        }
        else
        {
            _blockedPeers.Add(peerId);
            blockedNow = true;
        }

        if (_conversations.TryGetValue(peerId, out var conv))
        {
            string baseName = StripConversationStatus(conv.DisplayName);
            conv.DisplayName = BuildConversationDisplayName(baseName, peerId);
        }

        RefreshUserList();
        SetStatus(blockedNow
            ? $"已屏蔽 {GetPeerDisplayName(peerId)}，其消息将被拦截。"
            : $"已取消屏蔽 {GetPeerDisplayName(peerId)}。");
    }

    private bool IsPeerIdentityVerified(string peerId)
        => GetIdentityState(peerId) == IdentityExchangeState.Verified
           && _peerIdentityMap.TryGetValue(peerId, out var identityId)
           && _trustedIdentities.ContainsKey(identityId);

    private bool IsPeerBlocked(string peerId)
        => _blockedPeers.Contains(peerId);

    private void LoadIdentityStore()
    {
        try
        {
            if (File.Exists(_identityStorePath))
            {
                var model = JsonSerializer.Deserialize<IdentityStoreModel>(File.ReadAllText(_identityStorePath));
                if (model is not null)
                {
                    _localIdentityId = string.IsNullOrWhiteSpace(model.LocalIdentityId) ? Guid.NewGuid().ToString("N") : model.LocalIdentityId;
                    foreach (var p in model.Peers)
                    {
                        _trustedIdentities[p.IdentityId] = p.DisplayName;
                        if (!string.IsNullOrWhiteSpace(p.Remark))
                            _identityRemarks[p.IdentityId] = p.Remark;
                    }
                    return;
                }
            }
        }
        catch { }

        _localIdentityId = Guid.NewGuid().ToString("N");
        SaveIdentityStore();
    }

    private void SaveIdentityStore()
    {
        try
        {
            var model = new IdentityStoreModel(
                _localIdentityId,
                _trustedIdentities.Select(kv =>
                {
                    _identityRemarks.TryGetValue(kv.Key, out var remark);
                    return new IdentityPeer(kv.Key, kv.Value, remark);
                }).ToList());
            File.WriteAllText(_identityStorePath, JsonSerializer.Serialize(model));
        }
        catch { }
    }

    private sealed record IdentityPeer(string IdentityId, string DisplayName, string? Remark = null);
    private sealed record IdentityStoreModel(string LocalIdentityId, List<IdentityPeer> Peers);
    private sealed record IdentityCardFile(string DisplayName, string IdentityId, DateTime SavedAtUtc);

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
