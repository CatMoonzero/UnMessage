using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace UnMessage;

/// <summary>
/// 聊天客户端通信层：连接中继服务器，注册用户名，选择对方后进行端对端加密聊天。
/// 支持多群聊并发（每个群独立密钥与会话管理）。
/// </summary>
public sealed class ChatPeer : IDisposable
{
    // ── 协议常量 ──
    private const byte MsgChat           = 0x02;
    private const byte MsgRegister       = 0x10;
    private const byte MsgClientList     = 0x11;
    private const byte MsgChatRequest    = 0x12;
    private const byte MsgChatStart      = 0x13;
    private const byte MsgServerShutdown = 0x14;
    private const byte MsgPeerDisconnect = 0x15;
    private const byte MsgEndChat        = 0x16;
    private const byte MsgServerBroadcast = 0x17;
    private const byte MsgRegisterResult = 0x18;
    private const byte MsgRegisterForce  = 0x19;
    private const byte MsgIdentityLookup = 0x2A;
    private const byte MsgIdentityLookupResult = 0x2B;
    private const byte MsgGroupCreate    = 0x20;
    private const byte MsgGroupJoin      = 0x21;
    private const byte MsgGroupChat      = 0x23;
    private const byte MsgGroupList      = 0x24;
    private const byte MsgGroupStarted   = 0x25;
    private const byte MsgGroupKeyExchange = 0x26;
    private const byte MsgGroupKeyDeliver  = 0x27;
    private const byte MsgX3DHInit = 0x30;
    private const byte MsgX3DHResponse = 0x31;
    private const byte MsgGroupMemberSignedChange = 0x32;
    private const byte MsgGroupClosed = 0x33;
    private const int MaxFrameLength = 1024 * 1024;
    private const string IdentityMessagePrefix = "__UM_IDENTITY__:";
    private const string IdentityRejectPrefix = "__UM_IDENTITY_REJECT__";

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Dictionary<string, E2ECrypto> _cryptos = new();
    private readonly Dictionary<string, string> _peerNames = new();
    private readonly Dictionary<string, PeerBundle> _peerBundles = new();
    private readonly Dictionary<string, long> _sendCounters = new();
    private readonly Dictionary<string, long> _recvCounters = new();
    private readonly object _counterLock = new();
    private readonly HashSet<string> _recoveringPeers = new();
    private readonly object _recoverLock = new();
    private readonly Dictionary<string, byte[]> _pendingEphemeralPrivateKeys = new();
    private TaskCompletionSource<byte>? _registerTcs;
    private readonly Dictionary<string, byte[]> _groupKeys = new();
    private readonly HashSet<string> _groupKeyHolders = new();
    private readonly Dictionary<string, ECDiffieHellman> _pendingGroupEcdhs = new();
    private IdentityCrypto? _identityCrypto;
    private string _myUsername = "";

    /// <summary>收到对方加密消息时触发（参数=对方ID, 消息内容）。</summary>
    public event Action<string, string>? MessageReceived;
    public event Action<string, string, string>? IdentityReceived;
    public event Action<string>? IdentityRejected;
    public event Action<bool, string?, string?>? IdentityLookupResultReceived;
    public event Action<string, string>? IdentityHintReceived;
    public event Action<string, string>? SafetyNumberAvailable;
    public event Action<string>? HandshakeFailed;

    /// <summary>连接状态变更时触发。</summary>
    public event Action<string>? StatusChanged;

    /// <summary>加密通道建立完成时触发（参数=对方ID, 对方用户名）。</summary>
    public event Action<string, string>? EncryptionEstablished;

    /// <summary>连接断开时触发。</summary>
    public event Action? Disconnected;

    /// <summary>收到在线用户列表时触发（Id, Username）。</summary>
    public event Action<List<ClientEntry>>? ClientListUpdated;

    /// <summary>服务器关闭时触发。</summary>
    public event Action? ServerShutdown;

    /// <summary>对方断开聊天时触发（参数=对方ID）。</summary>
    public event Action<string>? PeerDisconnected;

    /// <summary>新私聊会话开始时触发（参数=对方ID, 对方用户名）。</summary>
    public event Action<string, string>? ChatStarted;

    /// <summary>收到服务器公告时触发。</summary>
    public event Action<string>? ServerBroadcastReceived;

    /// <summary>收到群聊消息时触发（群ID, 发送者, 消息内容）。</summary>
    public event Action<string, string, string>? GroupMessageReceived;

    /// <summary>进入群聊时触发（参数=群聊ID, 群聊名称, 是否为当前密钥持有者）。</summary>
    public event Action<string, string, bool>? GroupStarted;

    /// <summary>收到可用群聊列表时触发。</summary>
    public event Action<List<GroupEntry>>? GroupListUpdated;

    /// <summary>群聊加密建立完成时触发（参数=群ID）。</summary>
    public event Action<string>? GroupEncryptionEstablished;

    /// <summary>群聊被注销时触发（参数=群聊ID, 群聊名称, 提示）。</summary>
    public event Action<string, string, string>? GroupClosed;

    /// <summary>当前是否已连接到服务器。</summary>
    public bool IsConnected => _client?.Connected == true;
    public string LocalIdentityId { get; set; } = "";

    /// <summary>私聊加密是否已就绪（任意对端）。</summary>
    public bool IsEncrypted => _cryptos.Values.Any(c => c.IsEstablished);

    /// <summary>与指定对端的加密是否已就绪。</summary>
    public bool IsEncryptedWith(string peerId) => _cryptos.TryGetValue(peerId, out var c) && c.IsEstablished;

    /// <summary>群聊加密是否已就绪。</summary>
    public bool IsGroupEncrypted => _groupKeys.Count > 0;

    /// <summary>指定群聊加密是否已就绪。</summary>
    public bool IsGroupEncryptedWith(string groupId) => _groupKeys.ContainsKey(groupId);

    private void EnsureIdentityMaterial()
    {
        _identityCrypto ??= new IdentityCrypto();
        if (string.IsNullOrWhiteSpace(LocalIdentityId))
            LocalIdentityId = IdentityCrypto.CreateIdentityId(_identityCrypto.IdentityPublicKey);
    }

    /// <summary>
    /// 连接到中继服务器并注册用户名。
    /// </summary>
    public async Task ConnectAsync(string host, int port, string username)
    {
        _myUsername = username;
        _cts = new CancellationTokenSource();
        _client = new TcpClient();
        await _client.ConnectAsync(host, port, _cts.Token);
        _stream = _client.GetStream();
        StatusChanged?.Invoke($"已连接到服务器 {host}:{port}");

        _ = ReceiveLoopAsync();

        EnsureIdentityMaterial();
        byte registerResult = await RegisterAsync(MsgRegister, BuildRegisterPayload(username));
        if (registerResult == 0x00)
        {
            StatusChanged?.Invoke($"已注册为 \"{username}\"，等待选择聊天对象...");
            return;
        }

        if (registerResult == 0x02)
            throw new InvalidOperationException("用户名重复，请修改为新的用户名后重试。");

        if (registerResult == 0x01)
        {
            Disconnect();
            throw new InvalidOperationException("用户名无效或为保留名称，请更换用户名。");
        }

        Disconnect();
        throw new InvalidOperationException("用户名已被占用，请更换用户名。");
    }

    public async Task ForceRegisterAsync(string username)
    {
        if (!IsConnected)
            throw new InvalidOperationException("未连接到服务器。");

        EnsureIdentityMaterial();
        byte registerResult = await RegisterAsync(MsgRegisterForce, BuildRegisterPayload(username));
        if (registerResult == 0x01)
        {
            Disconnect();
            throw new InvalidOperationException("用户名无效或为保留名称，请更换用户名。");
        }

        if (registerResult != 0x00)
        {
            Disconnect();
            throw new InvalidOperationException("用户名已被占用，请更换用户名。");
        }
        StatusChanged?.Invoke($"已注册为 \"{username}\"，等待选择聊天对象...");
    }

    public Task SendIdentityCardAsync(string peerId)
        => TrySendIdentityCardAsync(peerId);

    public Task SendIdentityRejectAsync(string peerId)
        => SendMessageAsync(peerId, IdentityRejectPrefix);

    public Task LookupIdentityAsync(string identityId)
        => SendFrameAsync(MsgIdentityLookup, Encoding.UTF8.GetBytes(identityId));

    /// <summary>
    /// 请求与指定用户开始聊天。
    /// </summary>
    public async Task RequestChatAsync(string targetId)
    {
        await SendFrameAsync(MsgChatRequest, Encoding.UTF8.GetBytes(targetId));
    }

    /// <summary>
    /// 发送一条加密聊天消息给指定对端。
    /// </summary>
    public async Task SendMessageAsync(string peerId, string message)
    {
        if (!_cryptos.TryGetValue(peerId, out var crypto) || !crypto.IsEstablished)
            throw new InvalidOperationException("加密通道尚未建立。");

        byte[] encrypted = crypto.Encrypt(message);
        long counter = NextSendCounter(peerId);
        var payloadModel = ToSecureMessage(counter, encrypted);
        byte[] securePayload = JsonSerializer.SerializeToUtf8Bytes(payloadModel);
        byte[] payload = new byte[8 + securePayload.Length];
        WritePeerId(payload, peerId);
        Buffer.BlockCopy(securePayload, 0, payload, 8, securePayload.Length);
        await SendFrameAsync(MsgChat, payload);
    }

    /// <summary>
    /// 主动结束指定私聊会话，但保持与服务器的连接。
    /// </summary>
    public async Task EndChatAsync(string peerId)
    {
        await SendFrameAsync(MsgEndChat, Encoding.UTF8.GetBytes(peerId));
        ClearPeerCryptoState(peerId);
        _peerNames.Remove(peerId);
    }

    /// <summary>
    /// 退出指定群聊（发送签名 leave 消息）。
    /// </summary>
    public async Task LeaveGroupAsync(string groupId)
    {
        EnsureIdentityMaterial();
        if (string.IsNullOrWhiteSpace(groupId)) return;
        string payloadB64 = Convert.ToBase64String(Array.Empty<byte>());
        var signed = BuildSignedGroupChange(groupId, "leave", payloadB64);
        await SendFrameAsync(MsgGroupMemberSignedChange, JsonSerializer.SerializeToUtf8Bytes(signed));
        ClearGroupKeyState(groupId);
    }

    public async Task DissolveGroupAsync(string groupId)
    {
        EnsureIdentityMaterial();
        if (string.IsNullOrWhiteSpace(groupId)) return;

        string payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(groupId));
        var signed = BuildSignedGroupChange(groupId, "dissolve", payloadB64);
        await SendFrameAsync(MsgGroupMemberSignedChange, JsonSerializer.SerializeToUtf8Bytes(signed));
        ClearGroupKeyState(groupId);
    }

    /// <summary>
    /// 创建群聊。
    /// </summary>
    public async Task CreateGroupAsync(string groupName)
    {
        EnsureIdentityMaterial();
        string payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(groupName));
        var signed = BuildSignedGroupChange(string.Empty, "create", payloadB64);
        await SendFrameAsync(MsgGroupCreate, JsonSerializer.SerializeToUtf8Bytes(signed));
    }

    /// <summary>
    /// 加入群聊。
    /// </summary>
    public async Task JoinGroupAsync(string groupId)
    {
        EnsureIdentityMaterial();
        string payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(groupId));
        var signed = BuildSignedGroupChange(groupId, "join", payloadB64);
        await SendFrameAsync(MsgGroupJoin, JsonSerializer.SerializeToUtf8Bytes(signed));
    }

    /// <summary>
    /// 发送加密群聊消息。
    /// </summary>
    public async Task SendGroupMessageAsync(string groupId, string message)
    {
        if (!_groupKeys.TryGetValue(groupId, out var groupKey))
            throw new InvalidOperationException("群聊密钥尚未建立。");
        byte[] encrypted = E2ECrypto.EncryptWithKey(groupKey, Encoding.UTF8.GetBytes(message));
        var payload = new GroupChatSendPayload(groupId, Convert.ToBase64String(encrypted));
        await SendFrameAsync(MsgGroupChat, JsonSerializer.SerializeToUtf8Bytes(payload));
    }

    private sealed record GroupChatSendPayload(string GroupId, string Data);

    /// <summary>
    /// 主动断开连接。
    /// </summary>
    public void Disconnect()
    {
        _cts?.Cancel();
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
        foreach (var crypto in _cryptos.Values)
            crypto.Dispose();
        _cryptos.Clear();
        _peerNames.Clear();
        lock (_counterLock)
        {
            _sendCounters.Clear();
            _recvCounters.Clear();
        }
        ClearAllGroupKeyStates();
        StatusChanged?.Invoke("已断开连接");
        Disconnected?.Invoke();
    }

    public void Dispose()
    {
        Disconnect();
        _cts?.Dispose();
        _sendLock.Dispose();
    }

    // ── 消息接收循环 ────────────────────────────────────────

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (_client?.Connected == true && _cts?.IsCancellationRequested == false)
            {
                var (type, payload) = await ReadFrameAsync();

                switch (type)
                {
                    case MsgClientList:
                        // 服务器推送在线用户列表
                        var list = JsonSerializer.Deserialize<List<ClientEntry>>(payload) ?? [];
                        ClientListUpdated?.Invoke(list);
                        break;

                    case MsgChatStart:
                        // 服务器通知聊天开始 → 进行密钥交换
                        var chatStartInfo = JsonSerializer.Deserialize<ChatStartInfo>(payload);
                        if (chatStartInfo is not null)
                        {
                            _peerNames[chatStartInfo.PeerId] = chatStartInfo.PeerName;
                            if (_cryptos.TryGetValue(chatStartInfo.PeerId, out var existingCrypto))
                                existingCrypto.Dispose();
                            var newCrypto = new E2ECrypto();
                            _cryptos[chatStartInfo.PeerId] = newCrypto;
                            if (!string.IsNullOrWhiteSpace(chatStartInfo.IdentityPublicKey)
                                && !string.IsNullOrWhiteSpace(chatStartInfo.SignedPreKeyPublicKey)
                                && !string.IsNullOrWhiteSpace(chatStartInfo.SignedPreKeySignature)
                                && !string.IsNullOrWhiteSpace(chatStartInfo.IdentityId))
                            {
                                var bundle = new PeerBundle(
                                    chatStartInfo.PeerId,
                                    chatStartInfo.PeerName,
                                    chatStartInfo.IdentityId,
                                    chatStartInfo.IdentityPublicKey,
                                    chatStartInfo.SignedPreKeyPublicKey,
                                    chatStartInfo.SignedPreKeySignature);
                                if (!ValidatePeerBundle(bundle))
                                {
                                    StatusChanged?.Invoke($"{chatStartInfo.PeerName} 身份签名验证失败，已拒绝本次握手。");
                                    HandshakeFailed?.Invoke(chatStartInfo.PeerId);
                                    break;
                                }

                                _peerBundles[chatStartInfo.PeerId] = bundle;
                                string safety = BuildSafetyNumber(bundle.IdentityPublicKey);
                                SafetyNumberAvailable?.Invoke(chatStartInfo.PeerId, safety);
                            }

                            if (!string.IsNullOrWhiteSpace(chatStartInfo.IdentityId))
                                IdentityHintReceived?.Invoke(chatStartInfo.PeerId, chatStartInfo.IdentityId);
                            ChatStarted?.Invoke(chatStartInfo.PeerId, chatStartInfo.PeerName);
                            StatusChanged?.Invoke($"正在与 \"{chatStartInfo.PeerName}\" 建立加密通道...");
                            if (ShouldInitiateHandshake(chatStartInfo.PeerId))
                                await StartX3DHAsync(chatStartInfo.PeerId);
                            else
                            {
                                StatusChanged?.Invoke($"等待 {chatStartInfo.PeerName} 发起握手...");
                                ScheduleHandshakeFallback(chatStartInfo.PeerId, chatStartInfo.PeerName);
                            }
                        }
                        break;

                    case MsgX3DHInit:
                        await HandleX3DHInitAsync(payload);
                        break;

                    case MsgX3DHResponse:
                        await HandleX3DHResponseAsync(payload);
                        break;

                    case MsgChat:
                        // 收到加密聊天消息（含发送者 ID）
                        if (payload.Length > 8)
                        {
                            string msgPeerId = Encoding.UTF8.GetString(payload, 0, 8);
                            byte[] encPayload = payload[8..];
                            if (_cryptos.TryGetValue(msgPeerId, out var msgCrypto) && msgCrypto.IsEstablished)
                            {
                                try
                                {
                                    var secure = JsonSerializer.Deserialize<SecureChatMessage>(encPayload);
                                    if (secure is null)
                                        throw new InvalidDataException("无效安全消息格式。");

                                    byte[] rawEncrypted = FromSecureMessage(secure);
                                    ValidateReplay(msgPeerId, secure.Counter);
                                    string message = msgCrypto.Decrypt(rawEncrypted);
                                    if (message == IdentityRejectPrefix)
                                        IdentityRejected?.Invoke(msgPeerId);
                                    else if (TryParseIdentityMessage(message, out var card))
                                        IdentityReceived?.Invoke(msgPeerId, card.Username, card.IdentityId);
                                    else
                                        MessageReceived?.Invoke(msgPeerId, message);
                                }
                                catch (Exception ex)
                                {
                                    if (IsAuthTagMismatch(ex))
                                    {
                                        _ = RecoverPeerEncryptionAsync(msgPeerId);
                                    }
                                    else
                                    {
                                        StatusChanged?.Invoke($"已丢弃来自 {msgPeerId} 的异常消息：{ex.Message}");
                                    }
                                }
                            }
                        }
                        break;

                    case MsgServerShutdown:
                        // 服务器关闭通知
                        ServerShutdown?.Invoke();
                        Disconnect();
                        return;

                    case MsgPeerDisconnect:
                        // 对方断开（含对方 ID）
                        string dcPeerId = Encoding.UTF8.GetString(payload);
                        ClearPeerCryptoState(dcPeerId);
                        _peerBundles.Remove(dcPeerId);
                        lock (_counterLock)
                        {
                            _sendCounters.Remove(dcPeerId);
                            _recvCounters.Remove(dcPeerId);
                        }
                        _peerNames.Remove(dcPeerId);
                        PeerDisconnected?.Invoke(dcPeerId);
                        break;

                    case MsgRegisterResult:
                        _registerTcs?.TrySetResult(payload.Length > 0 ? payload[0] : (byte)0xFF);
                        break;

                    case MsgIdentityLookupResult:
                        var lookup = JsonSerializer.Deserialize<IdentityLookupResult>(payload);
                        if (lookup is not null)
                            IdentityLookupResultReceived?.Invoke(lookup.Found, lookup.PeerId, lookup.PeerName);
                        break;

                    case MsgServerBroadcast:
                        ServerBroadcastReceived?.Invoke(Encoding.UTF8.GetString(payload));
                        break;

                    case MsgGroupChat:
                        var groupMsg = JsonSerializer.Deserialize<GroupChatMessage>(payload);
                        if (groupMsg is not null)
                        {
                            if (groupMsg.Data is not null && _groupKeys.TryGetValue(groupMsg.GroupId, out var gKey))
                            {
                                if (!TryDecodeBase64(groupMsg.Data, out var encData))
                                    break;
                                byte[] pt = E2ECrypto.DecryptWithKey(gKey, encData);
                                GroupMessageReceived?.Invoke(groupMsg.GroupId, groupMsg.Sender, Encoding.UTF8.GetString(pt));
                            }
                            else if (groupMsg.Message is not null)
                            {
                                GroupMessageReceived?.Invoke(groupMsg.GroupId, groupMsg.Sender, groupMsg.Message);
                            }
                        }
                        break;

                    case MsgGroupStarted:
                        var started = JsonSerializer.Deserialize<GroupStartedInfo>(payload);
                        if (started is null || string.IsNullOrWhiteSpace(started.GroupId) || string.IsNullOrWhiteSpace(started.GroupName))
                            break;

                        if (started.IsKeyHolder)
                        {
                            var gkey = new byte[32];
                            RandomNumberGenerator.Fill(gkey);
                            _groupKeys[started.GroupId] = gkey;
                            _groupKeyHolders.Add(started.GroupId);
                            GroupStarted?.Invoke(started.GroupId, started.GroupName, true);
                            GroupEncryptionEstablished?.Invoke(started.GroupId);
                        }
                        else
                        {
                            _groupKeyHolders.Remove(started.GroupId);
                            GroupStarted?.Invoke(started.GroupId, started.GroupName, false);
                            await RequestGroupKeyAsync(started.GroupId);
                        }
                        break;

                    case MsgGroupKeyExchange:
                        var keyExFwd = JsonSerializer.Deserialize<GroupKeyExchangeForward>(payload);
                        if (keyExFwd is not null
                            && _groupKeyHolders.Contains(keyExFwd.GroupId)
                            && _groupKeys.ContainsKey(keyExFwd.GroupId))
                        {
                            await DistributeGroupKeyAsync(keyExFwd.GroupId, keyExFwd.JoinerId, keyExFwd.PublicKey);
                        }
                        break;

                    case MsgGroupKeyDeliver:
                        var keyDel = JsonSerializer.Deserialize<GroupKeyDeliverResponse>(payload);
                        if (keyDel is not null && _pendingGroupEcdhs.ContainsKey(keyDel.GroupId))
                            ProcessGroupKeyDelivery(keyDel);
                        break;

                    case MsgGroupList:
                        var groups = JsonSerializer.Deserialize<List<GroupEntry>>(payload) ?? [];
                        GroupListUpdated?.Invoke(groups);
                        break;

                    case MsgGroupClosed:
                        var closed = JsonSerializer.Deserialize<GroupClosedNotice>(payload);
                        if (closed is null)
                            break;

                        ClearGroupKeyState(closed.GroupId);

                        GroupClosed?.Invoke(closed.GroupId, closed.GroupName, closed.Reason);
                        break;
                }
            }
        }
        catch when (_cts?.IsCancellationRequested == true)
        {
            _registerTcs?.TrySetResult(0xFF);
        }
        catch
        {
            _registerTcs?.TrySetResult(0xFF);
            Disconnected?.Invoke();
        }
    }

    private async Task<byte> RegisterAsync(byte registerType, byte[] payload)
    {
        _registerTcs = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendFrameAsync(registerType, payload);
        byte result = await _registerTcs.Task;
        _registerTcs = null;
        return result;
    }

    private byte[] BuildRegisterPayload(string username)
    {
        var identityCrypto = _identityCrypto!;
        return JsonSerializer.SerializeToUtf8Bytes(new RegisterPayload(
            username,
            LocalIdentityId,
            Convert.ToBase64String(identityCrypto.IdentityPublicKey),
            Convert.ToBase64String(identityCrypto.SignedPreKeyPublicKey),
            Convert.ToBase64String(identityCrypto.SignedPreKeySignature)));
    }

    private async Task StartX3DHAsync(string peerId)
    {
        EnsureIdentityMaterial();
        if (!_peerBundles.TryGetValue(peerId, out _) || _stream is null) return;
        if (_pendingEphemeralPrivateKeys.ContainsKey(peerId)) return;
        if (_cryptos.TryGetValue(peerId, out var existing) && existing.IsEstablished) return;

        var (ephPriv, ephPub) = IdentityCrypto.GenerateX25519KeyPair();
        _pendingEphemeralPrivateKeys[peerId] = ephPriv;
        byte[] sig = IdentityCrypto.SignRaw(_identityCrypto!.IdentityPrivateKey, ephPub);

        var init = new X3DHInit(
            peerId,
            Convert.ToBase64String(ephPub),
            LocalIdentityId,
            Convert.ToBase64String(_identityCrypto.IdentityPublicKey),
            1,
            Convert.ToBase64String(sig));

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(init);
        byte[] payload = new byte[8 + json.Length];
        WritePeerId(payload, peerId);
        Buffer.BlockCopy(json, 0, payload, 8, json.Length);
        await SendFrameAsync(MsgX3DHInit, payload);
    }

    private async Task HandleX3DHInitAsync(byte[] payload)
    {
        if (payload.Length <= 8 || _identityCrypto is null) return;
        string peerId = Encoding.UTF8.GetString(payload, 0, 8);
        var init = JsonSerializer.Deserialize<X3DHInit>(payload[8..]);
        if (init is null || !_peerBundles.TryGetValue(peerId, out _)) return;

        if (_cryptos.TryGetValue(peerId, out var establishedCrypto) && establishedCrypto.IsEstablished)
            return;

        if (_pendingEphemeralPrivateKeys.ContainsKey(peerId))
        {
            if (ShouldInitiateHandshake(peerId))
            {
                // 指定由本端发起，忽略对端重复发起。
                return;
            }

            // 指定由对端发起，放弃本端兜底发起，转为响应对端握手。
            _pendingEphemeralPrivateKeys.Remove(peerId);
        }

        if (!TryDecodeBase64(init.SenderIdentityPublicKey, out var senderIdPub)
            || !TryDecodeBase64(init.EphemeralPublicKey, out var senderEphPub)
            || !TryDecodeBase64(init.Signature, out var senderSig))
        {
            HandshakeFailed?.Invoke(peerId);
            return;
        }

        if (!IdentityCrypto.VerifyRaw(senderIdPub, senderEphPub, senderSig))
        {
            HandshakeFailed?.Invoke(peerId);
            return;
        }

        byte[] mySignedPrePriv = _identityCrypto.SignedPreKeyPrivateKey;
        byte[] shared = IdentityCrypto.DeriveSharedKeyX25519(mySignedPrePriv, senderEphPub);
        if (_cryptos.TryGetValue(peerId, out var crypto))
        {
            crypto.SetSharedKey(shared);
            string kpName = _peerNames.GetValueOrDefault(peerId, peerId);
            EncryptionEstablished?.Invoke(peerId, kpName);
        }

        var (_, myEphPub) = IdentityCrypto.GenerateX25519KeyPair();
        byte[] rspSig = IdentityCrypto.SignRaw(_identityCrypto.IdentityPrivateKey, myEphPub);
        var rsp = new X3DHResponse(peerId, Convert.ToBase64String(myEphPub), init.Counter + 1, Convert.ToBase64String(rspSig));
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(rsp);
        byte[] outPayload = new byte[8 + json.Length];
        WritePeerId(outPayload, peerId);
        Buffer.BlockCopy(json, 0, outPayload, 8, json.Length);
        await SendFrameAsync(MsgX3DHResponse, outPayload);
    }

    private Task HandleX3DHResponseAsync(byte[] payload)
    {
        if (payload.Length <= 8 || _identityCrypto is null) return Task.CompletedTask;
        string peerId = Encoding.UTF8.GetString(payload, 0, 8);
        var rsp = JsonSerializer.Deserialize<X3DHResponse>(payload[8..]);
        if (rsp is null || !_peerBundles.TryGetValue(peerId, out var bundle)) return Task.CompletedTask;

        if (_cryptos.TryGetValue(peerId, out var establishedCrypto) && establishedCrypto.IsEstablished)
            return Task.CompletedTask;

        if (!TryDecodeBase64(bundle.IdentityPublicKey, out var peerIdPub)
            || !TryDecodeBase64(rsp.EphemeralPublicKey, out var peerEphPub)
            || !TryDecodeBase64(rsp.Signature, out var sig))
        {
            HandshakeFailed?.Invoke(peerId);
            return Task.CompletedTask;
        }

        if (!IdentityCrypto.VerifyRaw(peerIdPub, peerEphPub, sig))
        {
            HandshakeFailed?.Invoke(peerId);
            return Task.CompletedTask;
        }

        if (_pendingEphemeralPrivateKeys.TryGetValue(peerId, out var myEphPriv)
            && _cryptos.TryGetValue(peerId, out var crypto))
        {
            if (!TryDecodeBase64(bundle.SignedPreKeyPublicKey, out var signedPreKeyPub))
            {
                HandshakeFailed?.Invoke(peerId);
                return Task.CompletedTask;
            }

            byte[] shared = IdentityCrypto.DeriveSharedKeyX25519(myEphPriv, signedPreKeyPub);
            crypto.SetSharedKey(shared);
            _pendingEphemeralPrivateKeys.Remove(peerId);
            string kpName = _peerNames.GetValueOrDefault(peerId, peerId);
            EncryptionEstablished?.Invoke(peerId, kpName);
        }
        else
        {
            HandshakeFailed?.Invoke(peerId);
        }

        return Task.CompletedTask;
    }

    // ── 群聊密钥交换 ──────────────────────────────────────────

    private async Task RequestGroupKeyAsync(string groupId)
    {
        if (_pendingGroupEcdhs.TryGetValue(groupId, out var existing))
            existing.Dispose();
        var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        _pendingGroupEcdhs[groupId] = ecdh;
        byte[] pubKey = ecdh.PublicKey.ExportSubjectPublicKeyInfo();
        var request = new GroupKeyExchangeRequest(groupId, Convert.ToBase64String(pubKey));
        await SendFrameAsync(MsgGroupKeyExchange, JsonSerializer.SerializeToUtf8Bytes(request));
    }

    private async Task DistributeGroupKeyAsync(string groupId, string targetId, string joinerPubKeyBase64)
    {
        if (!_groupKeys.TryGetValue(groupId, out var groupKey)) return;
        if (!TryDecodeBase64(joinerPubKeyBase64, out var joinerPubKeyBytes))
            return;

        using var tempEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var joinerEcdh = ECDiffieHellman.Create();
        joinerEcdh.ImportSubjectPublicKeyInfo(joinerPubKeyBytes, out _);
        byte[] pairwiseKey = tempEcdh.DeriveKeyFromHash(joinerEcdh.PublicKey, HashAlgorithmName.SHA256);
        byte[] encryptedGroupKey = E2ECrypto.EncryptWithKey(pairwiseKey, groupKey);
        byte[] myPubKey = tempEcdh.PublicKey.ExportSubjectPublicKeyInfo();
        var response = new GroupKeyDeliverRequest(groupId, targetId, Convert.ToBase64String(myPubKey), Convert.ToBase64String(encryptedGroupKey));
        await SendFrameAsync(MsgGroupKeyDeliver, JsonSerializer.SerializeToUtf8Bytes(response));
        CryptographicOperations.ZeroMemory(pairwiseKey);
    }

    private void ProcessGroupKeyDelivery(GroupKeyDeliverResponse delivery)
    {
        if (!_pendingGroupEcdhs.TryGetValue(delivery.GroupId, out var pending)) return;
        if (!TryDecodeBase64(delivery.PublicKey, out var holderPubKeyBytes)
            || !TryDecodeBase64(delivery.EncryptedKey, out var encryptedData))
            return;

        using var holderEcdh = ECDiffieHellman.Create();
        holderEcdh.ImportSubjectPublicKeyInfo(holderPubKeyBytes, out _);
        byte[] pairwiseKey = pending.DeriveKeyFromHash(holderEcdh.PublicKey, HashAlgorithmName.SHA256);
        _groupKeys[delivery.GroupId] = E2ECrypto.DecryptWithKey(pairwiseKey, encryptedData);
        CryptographicOperations.ZeroMemory(pairwiseKey);
        pending.Dispose();
        _pendingGroupEcdhs.Remove(delivery.GroupId);
        GroupEncryptionEstablished?.Invoke(delivery.GroupId);
        StatusChanged?.Invoke("群聊端对端加密已建立 (ECDH + AES-256-GCM)");
    }

    private void ClearGroupKeyState(string groupId)
    {
        if (_groupKeys.TryGetValue(groupId, out var key))
        {
            CryptographicOperations.ZeroMemory(key);
            _groupKeys.Remove(groupId);
        }
        _groupKeyHolders.Remove(groupId);
        if (_pendingGroupEcdhs.TryGetValue(groupId, out var pending))
        {
            pending.Dispose();
            _pendingGroupEcdhs.Remove(groupId);
        }
    }

    private void ClearAllGroupKeyStates()
    {
        foreach (var gid in _groupKeys.Keys.ToList())
            ClearGroupKeyState(gid);
        _groupKeyHolders.Clear();
    }

    // ── 帧读写 ──────────────────────────────────────────────

    private async Task SendFrameAsync(byte type, byte[] payload)
    {
        if (_stream is null) return;

        int totalLen = 1 + payload.Length;
        byte[] header = new byte[5];
        BitConverter.TryWriteBytes(header.AsSpan(0, 4), totalLen);
        header[4] = type;

        await _sendLock.WaitAsync();
        try
        {
            await _stream.WriteAsync(header);
            await _stream.WriteAsync(payload);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<(byte type, byte[] payload)> ReadFrameAsync()
    {
        if (_stream is null) throw new InvalidOperationException("未连接。");

        byte[] lenBuf = new byte[4];
        await ReadExactAsync(_stream, lenBuf);
        int totalLen = BitConverter.ToInt32(lenBuf);
        if (totalLen <= 0 || totalLen > MaxFrameLength)
            throw new InvalidDataException($"无效帧长度: {totalLen}");

        byte[] typeBuf = new byte[1];
        await ReadExactAsync(_stream, typeBuf);
        byte type = typeBuf[0];

        int payloadLen = totalLen - 1;
        if (payloadLen <= 0)
            return (type, []);

        byte[] payload = new byte[payloadLen];
        await ReadExactAsync(_stream, payload);

        return (type, payload);
    }

    private static void WritePeerId(byte[] payload, string peerId)
    {
        payload.AsSpan(0, 8).Clear();
        ReadOnlySpan<char> idChars = peerId.AsSpan();
        if (idChars.Length > 8)
            idChars = idChars[..8];
        Encoding.UTF8.GetBytes(idChars, payload.AsSpan(0, 8));
    }

    private async Task TrySendIdentityCardAsync(string peerId)
    {
        if (string.IsNullOrWhiteSpace(LocalIdentityId) || string.IsNullOrWhiteSpace(_myUsername)) return;
        if (!IsEncryptedWith(peerId)) return;

        string payload = IdentityMessagePrefix + JsonSerializer.Serialize(new IdentityCard(_myUsername, LocalIdentityId));
        try
        {
            await SendMessageAsync(peerId, payload);
        }
        catch { }
    }

    private static bool TryParseIdentityMessage(string message, out IdentityCard card)
    {
        card = default!;
        if (!message.StartsWith(IdentityMessagePrefix, StringComparison.Ordinal))
            return false;

        string json = message[IdentityMessagePrefix.Length..];
        var parsed = JsonSerializer.Deserialize<IdentityCard>(json);
        if (parsed is null || string.IsNullOrWhiteSpace(parsed.Username) || string.IsNullOrWhiteSpace(parsed.IdentityId))
            return false;

        card = parsed;
        return true;
    }

    private sealed record IdentityCard(string Username, string IdentityId);
    private sealed record IdentityLookupResult(bool Found, string? PeerId, string? PeerName);
    private sealed record X3DHInit(string PeerId, string EphemeralPublicKey, string SenderIdentityId, string SenderIdentityPublicKey, long Counter, string Signature);
    private sealed record X3DHResponse(string PeerId, string EphemeralPublicKey, long Counter, string Signature);

    private long NextSendCounter(string peerId)
    {
        lock (_counterLock)
        {
            long next = _sendCounters.TryGetValue(peerId, out var current) ? current + 1 : 1;
            _sendCounters[peerId] = next;
            return next;
        }
    }

    private void ValidateReplay(string peerId, long counter)
    {
        lock (_counterLock)
        {
            long last = _recvCounters.TryGetValue(peerId, out var current) ? current : 0;
            if (counter <= last)
                throw new CryptographicException("检测到重放消息。已拒绝。");
            _recvCounters[peerId] = counter;
        }
    }

    private static SecureChatMessage ToSecureMessage(long counter, byte[] encrypted)
    {
        byte[] nonce = encrypted[..12];
        byte[] tag = encrypted[12..28];
        byte[] cipher = encrypted[28..];
        return new SecureChatMessage(counter, Convert.ToBase64String(cipher), Convert.ToBase64String(nonce), Convert.ToBase64String(tag));
    }

    private static byte[] FromSecureMessage(SecureChatMessage secure)
    {
        byte[] nonce = Convert.FromBase64String(secure.NonceBase64);
        byte[] tag = Convert.FromBase64String(secure.TagBase64);
        byte[] cipher = Convert.FromBase64String(secure.CiphertextBase64);
        byte[] combined = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, combined, nonce.Length + tag.Length, cipher.Length);
        return combined;
    }

    private static bool ValidatePeerBundle(PeerBundle bundle)
    {
        if (!TryDecodeBase64(bundle.IdentityPublicKey, out var idPub)
            || !TryDecodeBase64(bundle.SignedPreKeyPublicKey, out var spkPub)
            || !TryDecodeBase64(bundle.SignedPreKeySignature, out var sig))
            return false;

        return IdentityCrypto.VerifyRaw(idPub, spkPub, sig);
    }

    private static string BuildSafetyNumber(string identityPublicKeyBase64)
    {
        byte[] idPub = Convert.FromBase64String(identityPublicKeyBase64);
        byte[] hash = SHA256.HashData(idPub);
        return Convert.ToHexString(hash)[..24];
    }

    private GroupMemberSignedChange BuildSignedGroupChange(string groupId, string changeType, string payloadBase64)
    {
        var identityCrypto = _identityCrypto!;
        string canonical = $"{groupId}|{LocalIdentityId}|{changeType}|{payloadBase64}";
        byte[] data = Encoding.UTF8.GetBytes(canonical);
        byte[] sig = IdentityCrypto.SignRaw(identityCrypto.IdentityPrivateKey, data);
        return new GroupMemberSignedChange(groupId, LocalIdentityId, changeType, payloadBase64, Convert.ToBase64String(sig));
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset));
            if (read == 0) throw new IOException("连接已关闭。");
            offset += read;
        }
    }

    private bool ShouldInitiateHandshake(string peerId)
    {
        if (!_peerBundles.TryGetValue(peerId, out var bundle))
            return true;

        if (string.IsNullOrWhiteSpace(LocalIdentityId) || string.IsNullOrWhiteSpace(bundle.IdentityId))
            return true;

        return string.CompareOrdinal(LocalIdentityId, bundle.IdentityId) < 0;
    }

    private void ScheduleHandshakeFallback(string peerId, string peerName)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            if (_cts?.IsCancellationRequested == true || !IsConnected)
                return;
            if (IsEncryptedWith(peerId) || _pendingEphemeralPrivateKeys.ContainsKey(peerId))
                return;

            try
            {
                await StartX3DHAsync(peerId);
                StatusChanged?.Invoke($"握手兜底：已主动向 \"{peerName}\" 发起握手");
            }
            catch { }
        });
    }

    private void ClearPeerCryptoState(string peerId)
    {
        if (_cryptos.TryGetValue(peerId, out var crypto))
        {
            crypto.Dispose();
            _cryptos.Remove(peerId);
        }
        _pendingEphemeralPrivateKeys.Remove(peerId);
        lock (_counterLock)
        {
            _sendCounters.Remove(peerId);
            _recvCounters.Remove(peerId);
        }
    }

    private static bool TryDecodeBase64(string value, out byte[] decoded)
    {
        try
        {
            decoded = Convert.FromBase64String(value);
            return true;
        }
        catch
        {
            decoded = [];
            return false;
        }
    }

    private static bool IsAuthTagMismatch(Exception ex)
        => ex is CryptographicException ce
           && ce.Message.Contains("authentication tag", StringComparison.OrdinalIgnoreCase);

    private async Task RecoverPeerEncryptionAsync(string peerId)
    {
        lock (_recoverLock)
        {
            if (_recoveringPeers.Contains(peerId))
                return;
            _recoveringPeers.Add(peerId);
        }

        try
        {
            StatusChanged?.Invoke($"检测到与 {peerId} 的会话密钥不同步，正在自动重建加密通道...");

            ClearPeerCryptoState(peerId);
            _cryptos[peerId] = new E2ECrypto();

            var peerName = _peerNames.GetValueOrDefault(peerId, peerId);

            if (ShouldInitiateHandshake(peerId))
                await StartX3DHAsync(peerId);
            else
                ScheduleHandshakeFallback(peerId, peerName);
        }
        catch { }
        finally
        {
            lock (_recoverLock)
                _recoveringPeers.Remove(peerId);
        }
    }
}

