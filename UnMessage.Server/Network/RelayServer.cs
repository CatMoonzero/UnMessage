using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace UnMessage.Server;

public sealed class RelayServer : IDisposable
{
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
    private const int MaxBroadcastLength = 1024;
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

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, ConnectedClient> _clients = new();
    private readonly ConcurrentDictionary<string, ChatGroup> _groups = new();
    private static readonly ConcurrentDictionary<NetworkStream, SemaphoreSlim> _streamSendLocks = new();
    private readonly string _groupStorePath = Path.Combine(AppContext.BaseDirectory, "groups.json");

    public event Action<string>? Log;
    public event Action<int>? OnlineCountChanged;
    public event Action<int>? ActivePairsChanged;
    public bool IsRunning { get; private set; }

    public RelayServer()
    {
        LoadGroupStore();
    }

    public async Task StartAsync(int port)
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        IsRunning = true;
        Log?.Invoke($"服务器已启动，监听端口 {port}");
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var tcp = await _listener.AcceptTcpClientAsync(_cts.Token);
                var client = new ConnectedClient(tcp);
                _clients[client.Id] = client;
                Log?.Invoke($"新连接: {tcp.Client.RemoteEndPoint} (ID: {client.Id})");
                OnlineCountChanged?.Invoke(_clients.Count);
                _ = HandleClientAsync(client);
            }
        }
        catch (OperationCanceledException) { }
        finally { IsRunning = false; }
    }

    public void Stop()
    {
        if (!IsRunning) return;
        foreach (var c in _clients.Values)
        { try { SendFrameSync(c.Stream, MsgServerShutdown, []); } catch { } }
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
        foreach (var c in _clients.Values) c.Dispose();
        _clients.Clear();
        foreach (var g in _groups.Values)
        {
            g.Members.Clear();
            g.KeyHolderId = null;
        }
        IsRunning = false;
        OnlineCountChanged?.Invoke(0);
        ActivePairsChanged?.Invoke(0);
        Log?.Invoke("服务器已停止");
    }

    public void Dispose() { Stop(); _cts?.Dispose(); }

    private async Task HandleClientAsync(ConnectedClient client)
    {
        try
        {
            while (client.Tcp.Connected && _cts?.IsCancellationRequested == false)
            {
                var (type, payload) = await ReadFrameAsync(client.Stream);
                client.LastInboundMessageType = type;
                client.LastInboundAtUtc = DateTime.UtcNow;
                switch (type)
                {
                    case MsgRegister:
                        var register = ParseRegisterPayload(payload);
                        string requestedName = register.Username;
                        bool invalidName = string.IsNullOrWhiteSpace(requestedName)
                            || requestedName.Length > MaxUsernameLength
                            || IsReservedUsername(requestedName);
                        var duplicateClient = _clients.Values.FirstOrDefault(c => c.Id != client.Id
                            && !string.IsNullOrEmpty(c.Username)
                            && string.Equals(c.Username, requestedName, StringComparison.OrdinalIgnoreCase));
                        if (invalidName)
                        {
                            await SendFrameAsync(client.Stream, MsgRegisterResult, [0x01]);
                            Log?.Invoke($"注册被拒绝（用户名无效/保留，长度需 1-{MaxUsernameLength}）: {requestedName}");
                        }
                        else if (duplicateClient is not null)
                        {
                            await SendFrameAsync(client.Stream, MsgRegisterResult, [0x02]);
                            Log?.Invoke($"用户名重复，需客户端更换新用户名: {requestedName}");
                        }
                        else
                        {
                            client.Username = requestedName;
                            client.IdentityId = register.IdentityId;
                            client.IdentityPublicKey = register.IdentityPublicKey;
                            client.SignedPreKeyPublicKey = register.SignedPreKeyPublicKey;
                            client.SignedPreKeySignature = register.SignedPreKeySignature;
                            await SendFrameAsync(client.Stream, MsgRegisterResult, [0x00]);
                            Log?.Invoke($"用户已注册: {client.Username} ({client.Id})");
                            await BroadcastClientListAsync();
                        }
                        break;
                    case MsgRegisterForce:
                        await HandleForceRegisterAsync(client, payload);
                        break;
                    case MsgIdentityLookup:
                        await HandleIdentityLookupAsync(client, Encoding.UTF8.GetString(payload).Trim());
                        break;
                    case MsgChatRequest:
                        await HandleChatRequestAsync(client, Encoding.UTF8.GetString(payload));
                        break;
                    case MsgEndChat:
                        await HandleEndChatAsync(client, payload);
                        break;
                    case MsgGroupCreate:
                        await HandleGroupCreateAsync(client, payload);
                        break;
                    case MsgGroupJoin:
                        await HandleGroupJoinAsync(client, payload);
                        break;
                    case MsgGroupChat:
                        await HandleGroupChatAsync(client, payload);
                        break;
                    case MsgGroupKeyExchange:
                        await HandleGroupKeyExchangeAsync(client, payload);
                        break;
                    case MsgGroupKeyDeliver:
                        await HandleGroupKeyDeliverAsync(client, payload);
                        break;
                    case MsgGroupMemberSignedChange:
                        await HandleSignedGroupChangeAsync(client, payload);
                        break;
                    case MsgChat:
                    case MsgX3DHInit:
                    case MsgX3DHResponse:
                        if (payload.Length > 8)
                        {
                            string targetId = Encoding.UTF8.GetString(payload, 0, 8);
                            byte[] actualPayload = payload[8..];
                            if (client.PairedPeers.ContainsKey(targetId) && _clients.TryGetValue(targetId, out var peer))
                            {
                                byte[] forwarded = new byte[8 + actualPayload.Length];
                                WritePeerId(forwarded, client.Id);
                                Buffer.BlockCopy(actualPayload, 0, forwarded, 8, actualPayload.Length);
                                await SendFrameAsync(peer.Stream, type, forwarded);
                            }
                        }
                        break;
                }
            }
        }
        catch when (_cts?.IsCancellationRequested == true) { }
        catch (Exception ex)
        {
            if (IsExpectedDisconnect(ex))
            {
                // 客户端主动断开属于正常流程，不记为异常。
                return;
            }

            string lastType = client.LastInboundMessageType.HasValue
                ? DescribeMessageType(client.LastInboundMessageType.Value)
                : "(无)";
            string lastAt = client.LastInboundAtUtc == default ? "(未知)" : client.LastInboundAtUtc.ToLocalTime().ToString("HH:mm:ss");
            string user = string.IsNullOrWhiteSpace(client.Username) ? "(未注册)" : client.Username;
            Log?.Invoke($"连接异常断开: {user} ({client.Id})，最后消息={lastType} @ {lastAt}，异常={ex.GetType().Name}: {ex.Message}");
        }
        finally { await RemoveClientAsync(client); }
    }

    private async Task HandleForceRegisterAsync(ConnectedClient client, byte[] payload)
    {
        var register = ParseRegisterPayload(payload);
        string requestedName = register.Username;
        bool invalidName = string.IsNullOrWhiteSpace(requestedName)
            || requestedName.Length > MaxUsernameLength
            || IsReservedUsername(requestedName);
        if (invalidName)
        {
            await SendFrameAsync(client.Stream, MsgRegisterResult, [0x01]);
            return;
        }

        var duplicateClient = _clients.Values.FirstOrDefault(c => c.Id != client.Id
            && !string.IsNullOrEmpty(c.Username)
            && string.Equals(c.Username, requestedName, StringComparison.OrdinalIgnoreCase));

        if (duplicateClient is not null)
        {
            await SendFrameAsync(client.Stream, MsgRegisterResult, [0x02]);
            Log?.Invoke($"拒绝强制注册（用户名重复），需更换新用户名: {requestedName}");
            return;
        }

        client.Username = requestedName;
        client.IdentityId = register.IdentityId;
        client.IdentityPublicKey = register.IdentityPublicKey;
        client.SignedPreKeyPublicKey = register.SignedPreKeyPublicKey;
        client.SignedPreKeySignature = register.SignedPreKeySignature;
        await SendFrameAsync(client.Stream, MsgRegisterResult, [0x00]);
        Log?.Invoke($"用户已注册: {client.Username} ({client.Id})");
        await BroadcastClientListAsync();
    }

    private async Task HandleIdentityLookupAsync(ConnectedClient requester, string identityId)
    {
        if (string.IsNullOrWhiteSpace(identityId))
        {
            await SendFrameAsync(requester.Stream, MsgIdentityLookupResult, JsonSerializer.SerializeToUtf8Bytes(new IdentityLookupResult(false, null, null)));
            return;
        }

        var found = _clients.Values.FirstOrDefault(c => c.Id != requester.Id
            && !string.IsNullOrWhiteSpace(c.Username)
            && string.Equals(c.IdentityId, identityId, StringComparison.OrdinalIgnoreCase));

        var result = found is null
            ? new IdentityLookupResult(false, null, null)
            : new IdentityLookupResult(true, found.Id, found.Username);

        await SendFrameAsync(requester.Stream, MsgIdentityLookupResult, JsonSerializer.SerializeToUtf8Bytes(result));
    }

    private static RegisterPayload ParseRegisterPayload(byte[] payload)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<RegisterPayload>(payload);
            if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.Username))
                return new RegisterPayload(
                    parsed.Username.Trim(),
                    parsed.IdentityId?.Trim() ?? "",
                    parsed.IdentityPublicKey?.Trim() ?? "",
                    parsed.SignedPreKeyPublicKey?.Trim() ?? "",
                    parsed.SignedPreKeySignature?.Trim() ?? "");
        }
        catch { }

        string username = Encoding.UTF8.GetString(payload).Trim();
        return new RegisterPayload(username, "", "", "", "");
    }

    private async Task HandleChatRequestAsync(ConnectedClient requester, string targetId)
    {
        if (!_clients.TryGetValue(targetId, out var target) || targetId == requester.Id)
        {
            Log?.Invoke($"聊天请求失败: {requester.Username} -> {targetId}");
            return;
        }
        if (requester.PairedPeers.ContainsKey(targetId))
        {
            Log?.Invoke($"已配对，忽略重复请求: {requester.Username} -> {target.Username}");
            return;
        }
        requester.PairedPeers[target.Id] = true;
        target.PairedPeers[requester.Id] = true;
        Log?.Invoke($"已配对: {requester.Username} <-> {target.Username}");
        NotifyActivePairs();
        var startForRequester = new ChatStartInfo(
            target.Id,
            target.Username,
            target.IdentityId,
            target.IdentityPublicKey,
            target.SignedPreKeyPublicKey,
            target.SignedPreKeySignature);
        var startForTarget = new ChatStartInfo(
            requester.Id,
            requester.Username,
            requester.IdentityId,
            requester.IdentityPublicKey,
            requester.SignedPreKeyPublicKey,
            requester.SignedPreKeySignature);
        await SendFrameAsync(requester.Stream, MsgChatStart, JsonSerializer.SerializeToUtf8Bytes(startForRequester));
        await SendFrameAsync(target.Stream, MsgChatStart, JsonSerializer.SerializeToUtf8Bytes(startForTarget));
        await BroadcastClientListAsync();
    }

    private async Task RemoveClientAsync(ConnectedClient client)
    {
        _clients.TryRemove(client.Id, out _);
        foreach (var peerId in client.PairedPeers.Keys)
        {
            if (_clients.TryGetValue(peerId, out var peer))
            {
                peer.PairedPeers.TryRemove(client.Id, out _);
                try { await SendFrameAsync(peer.Stream, MsgPeerDisconnect, Encoding.UTF8.GetBytes(client.Id)); } catch { }
            }
        }
        foreach (var gid in client.JoinedGroups.Keys.ToArray())
            await RemoveFromGroupAsync(client, gid);
        client.Dispose();
        string lastType = client.LastInboundMessageType.HasValue
            ? DescribeMessageType(client.LastInboundMessageType.Value)
            : "(无)";
        string lastAt = client.LastInboundAtUtc == default ? "(未知)" : client.LastInboundAtUtc.ToLocalTime().ToString("HH:mm:ss");
        string user = string.IsNullOrWhiteSpace(client.Username) ? "(未注册)" : client.Username;
        Log?.Invoke($"用户断开连接: {user} ({client.Id})，最后消息={lastType} @ {lastAt}");
        OnlineCountChanged?.Invoke(_clients.Count);
        NotifyActivePairs();
        await BroadcastClientListAsync();
    }

    private static string DescribeMessageType(byte type) => type switch
    {
        MsgChat => "0x02 Chat",
        MsgRegister => "0x10 Register",
        MsgClientList => "0x11 ClientList",
        MsgChatRequest => "0x12 ChatRequest",
        MsgChatStart => "0x13 ChatStart",
        MsgServerShutdown => "0x14 ServerShutdown",
        MsgPeerDisconnect => "0x15 PeerDisconnect",
        MsgEndChat => "0x16 EndChat",
        MsgServerBroadcast => "0x17 ServerBroadcast",
        MsgRegisterResult => "0x18 RegisterResult",
        MsgRegisterForce => "0x19 RegisterForce",
        MsgGroupCreate => "0x20 GroupCreate",
        MsgGroupJoin => "0x21 GroupJoin",
        MsgGroupChat => "0x23 GroupChat",
        MsgGroupList => "0x24 GroupList",
        MsgGroupStarted => "0x25 GroupStarted",
        MsgGroupKeyExchange => "0x26 GroupKeyExchange",
        MsgGroupKeyDeliver => "0x27 GroupKeyDeliver",
        MsgIdentityLookup => "0x2A IdentityLookup",
        MsgIdentityLookupResult => "0x2B IdentityLookupResult",
        MsgX3DHInit => "0x30 X3DHInit",
        MsgX3DHResponse => "0x31 X3DHResponse",
        MsgGroupMemberSignedChange => "0x32 GroupMemberSignedChange",
        MsgGroupClosed => "0x33 GroupClosed",
        _ => $"0x{type:X2} Unknown"
    };

    private static bool IsExpectedDisconnect(Exception ex)
    {
        if (ex is IOException ioEx && ioEx.Message.Contains("Connection closed", StringComparison.OrdinalIgnoreCase))
            return true;

        if (ex is SocketException sockEx && (sockEx.SocketErrorCode is SocketError.ConnectionReset or SocketError.Shutdown))
            return true;

        return false;
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

    private async Task BroadcastClientListAsync()
    {
        var registeredClients = _clients.Values
            .Where(c => !string.IsNullOrEmpty(c.Username))
            .OrderBy(c => c.Username, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var allClientEntries = registeredClients
            .Select(c => new ClientListEntry(c.Id, c.Username))
            .ToArray();

        var groupList = _groups.Values
            .Select(g => new GroupListEntry(g.Id, g.Name, g.Members.Count))
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        byte[] groupJson = JsonSerializer.SerializeToUtf8Bytes(groupList);

        foreach (var client in registeredClients)
        {
            var others = new ClientListEntry[Math.Max(0, allClientEntries.Length - 1)];
            int idx = 0;
            foreach (var entry in allClientEntries)
            {
                if (entry.Id == client.Id) continue;
                others[idx++] = entry;
            }

            byte[] json = JsonSerializer.SerializeToUtf8Bytes(others);
            try
            {
                await SendFrameAsync(client.Stream, MsgClientList, json);
                await SendFrameAsync(client.Stream, MsgGroupList, groupJson);
            }
            catch { }
        }
    }

    private async Task HandleEndChatAsync(ConnectedClient client, byte[] payload)
    {
        if (payload.Length >= 8)
        {
            string targetId = Encoding.UTF8.GetString(payload, 0, 8);
            client.PairedPeers.TryRemove(targetId, out _);
            if (_clients.TryGetValue(targetId, out var peer))
            {
                peer.PairedPeers.TryRemove(client.Id, out _);
                try { await SendFrameAsync(peer.Stream, MsgPeerDisconnect, Encoding.UTF8.GetBytes(client.Id)); } catch { }
            }
            Log?.Invoke($"用户 {client.Username} 结束了与 {targetId} 的私聊");
            NotifyActivePairs();
        }
        await BroadcastClientListAsync();
    }

    private async Task RemoveFromGroupAsync(ConnectedClient client, string groupId)
    {
        if (!client.JoinedGroups.ContainsKey(groupId)) return;
        if (!_groups.TryGetValue(groupId, out var group)) return;

        group.Members.TryRemove(client.Id, out _);
        client.JoinedGroups.TryRemove(groupId, out _);
        Log?.Invoke($"用户 {client.Username} 退出了群聊 \"{group.Name}\"");

        bool adminLeft = string.Equals(group.CreatorIdentityId, client.IdentityId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(group.CreatorUsername, client.Username, StringComparison.OrdinalIgnoreCase);

        if (adminLeft)
        {
            var successor = group.Members.Values.FirstOrDefault();
            if (successor is not null)
            {
                group.CreatorIdentityId = successor.IdentityId;
                group.CreatorUsername = successor.Username;
                Log?.Invoke($"群聊 \"{group.Name}\" 管理员已转移至 {successor.Username}");

                var adminNotice = JsonSerializer.SerializeToUtf8Bytes(
                    new GroupChatMessage(group.Id, "系统", $"你已成为群聊 \"{group.Name}\" 的管理员。"));
                try { await SendFrameAsync(successor.Stream, MsgGroupChat, adminNotice); } catch { }
            }
            else
            {
                group.CreatorIdentityId = "";
                group.CreatorUsername = "";
            }
            SaveGroupStore();
        }

        if (group.KeyHolderId == client.Id)
        {
            var next = group.Members.Values.FirstOrDefault();
            group.KeyHolderId = next?.Id;
            if (next is not null)
                Log?.Invoke($"密钥持有者已转移至 {next.Username}");
        }
        var notification = JsonSerializer.SerializeToUtf8Bytes(
            new GroupChatMessage(group.Id, "系统", $"\"{client.Username}\" 退出了群聊"));
        foreach (var member in group.Members.Values)
        {
            try { await SendFrameAsync(member.Stream, MsgGroupChat, notification); } catch { }
        }

        if (group.Members.IsEmpty)
            group.KeyHolderId = null;
    }

    private async Task HandleGroupCreateAsync(ConnectedClient client, byte[] payloadBytes)
    {
        var signed = ParseSignedGroupChange(payloadBytes);
        if (signed is null || !VerifySignedGroupChange(client, signed, "create")) return;
        if (!TryDecodeBase64(signed.PayloadBase64, out var decodedPayload)) return;
        string groupName = Encoding.UTF8.GetString(decodedPayload);
        groupName = groupName.Trim();
        if (string.IsNullOrWhiteSpace(groupName) || groupName.Length > MaxGroupNameLength) return;

        var group = new ChatGroup
        {
            Name = groupName,
            CreatorIdentityId = client.IdentityId,
            CreatorUsername = client.Username,
            KeyHolderId = client.Id
        };
        group.Members[client.Id] = client;
        client.JoinedGroups[group.Id] = true;
        _groups[group.Id] = group;
        SaveGroupStore();
        Log?.Invoke($"群聊 \"{groupName}\" 由 {client.Username} 创建 (ID: {group.Id})");
        var started = new GroupStartedInfo(group.Id, group.Name, true);
        await SendFrameAsync(client.Stream, MsgGroupStarted, JsonSerializer.SerializeToUtf8Bytes(started));
        await BroadcastClientListAsync();
    }

    private async Task HandleGroupJoinAsync(ConnectedClient client, byte[] payloadBytes)
    {
        var signed = ParseSignedGroupChange(payloadBytes);
        if (signed is null || !VerifySignedGroupChange(client, signed, "join")) return;
        if (!TryDecodeBase64(signed.PayloadBase64, out var decodedPayload)) return;
        string groupId = Encoding.UTF8.GetString(decodedPayload);
        if (!_groups.TryGetValue(groupId, out var group)) return;
        if (client.JoinedGroups.ContainsKey(groupId)) return;

        if (string.IsNullOrWhiteSpace(group.CreatorIdentityId) || string.IsNullOrWhiteSpace(group.CreatorUsername))
        {
            group.CreatorIdentityId = client.IdentityId;
            group.CreatorUsername = client.Username;
            Log?.Invoke($"群聊 \"{group.Name}\" 管理员已由 {client.Username} 接任");
            SaveGroupStore();

            var adminNotice = JsonSerializer.SerializeToUtf8Bytes(
                new GroupChatMessage(group.Id, "系统", $"你已成为群聊 \"{group.Name}\" 的管理员。"));
            try { await SendFrameAsync(client.Stream, MsgGroupChat, adminNotice); } catch { }
        }

        group.Members[client.Id] = client;
        client.JoinedGroups[group.Id] = true;
        bool isKeyHolder = false;
        if (string.IsNullOrWhiteSpace(group.KeyHolderId))
        {
            group.KeyHolderId = client.Id;
            isKeyHolder = true;
        }

        Log?.Invoke($"用户 {client.Username} 加入了群聊 \"{group.Name}\"");
        var notification = JsonSerializer.SerializeToUtf8Bytes(
            new GroupChatMessage(group.Id, "系统", $"\"{client.Username}\" 加入了群聊"));
        foreach (var member in group.Members.Values.Where(m => m.Id != client.Id))
        {
            try { await SendFrameAsync(member.Stream, MsgGroupChat, notification); } catch { }
        }
        var started = new GroupStartedInfo(group.Id, group.Name, isKeyHolder);
        await SendFrameAsync(client.Stream, MsgGroupStarted, JsonSerializer.SerializeToUtf8Bytes(started));
        await BroadcastClientListAsync();
    }

    private async Task HandleGroupChatAsync(ConnectedClient sender, byte[] payload)
    {
        var chatPayload = JsonSerializer.Deserialize<GroupChatSendPayload>(payload);
        if (chatPayload is null || string.IsNullOrWhiteSpace(chatPayload.GroupId)) return;
        if (!sender.JoinedGroups.ContainsKey(chatPayload.GroupId)) return;
        if (!_groups.TryGetValue(chatPayload.GroupId, out var group)) return;
        var chatMsg = JsonSerializer.SerializeToUtf8Bytes(
            new GroupChatMessage(group.Id, sender.Username, Data: chatPayload.Data));
        foreach (var member in group.Members.Values.Where(m => m.Id != sender.Id))
        {
            try { await SendFrameAsync(member.Stream, MsgGroupChat, chatMsg); } catch { }
        }
    }

    private record GroupChatSendPayload(string GroupId, string Data);

    private async Task HandleGroupKeyExchangeAsync(ConnectedClient client, byte[] payload)
    {
        var request = JsonSerializer.Deserialize<GroupKeyExchangeRequest>(payload);
        if (request is null || string.IsNullOrWhiteSpace(request.GroupId)) return;
        if (!client.JoinedGroups.ContainsKey(request.GroupId)) return;
        if (!_groups.TryGetValue(request.GroupId, out var group)) return;
        if (group.KeyHolderId is null || !_clients.TryGetValue(group.KeyHolderId, out var keyHolder)) return;
        var forward = new GroupKeyExchangeForward(request.GroupId, client.Id, request.PublicKey);
        byte[] forwardPayload = JsonSerializer.SerializeToUtf8Bytes(forward);
        try { await SendFrameAsync(keyHolder.Stream, MsgGroupKeyExchange, forwardPayload); } catch { }
    }

    private async Task HandleGroupKeyDeliverAsync(ConnectedClient client, byte[] payload)
    {
        var request = JsonSerializer.Deserialize<GroupKeyDeliverRequest>(payload);
        if (request is null) return;
        if (!_clients.TryGetValue(request.TargetId, out var target)) return;
        var response = new GroupKeyDeliverResponse(request.GroupId, request.PublicKey, request.EncryptedKey);
        byte[] responsePayload = JsonSerializer.SerializeToUtf8Bytes(response);
        try { await SendFrameAsync(target.Stream, MsgGroupKeyDeliver, responsePayload); } catch { }
    }

    private async Task HandleSignedGroupChangeAsync(ConnectedClient client, byte[] payload)
    {
        var signed = ParseSignedGroupChange(payload);
        if (signed is null) return;
        if (!VerifySignedGroupChange(client, signed, signed.ChangeType)) return;

        if (string.Equals(signed.ChangeType, "leave", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(signed.GroupId))
                await RemoveFromGroupAsync(client, signed.GroupId);
            await BroadcastClientListAsync();
        }

        if (string.Equals(signed.ChangeType, "dissolve", StringComparison.OrdinalIgnoreCase))
            await HandleGroupDissolveAsync(client, signed);
    }

    private async Task HandleGroupDissolveAsync(ConnectedClient client, GroupMemberSignedChange signed)
    {
        if (string.IsNullOrWhiteSpace(signed.GroupId)) return;
        if (!_groups.TryGetValue(signed.GroupId, out var group)) return;
        bool creatorIdentityMatch = !string.IsNullOrWhiteSpace(group.CreatorIdentityId)
            && string.Equals(group.CreatorIdentityId, client.IdentityId, StringComparison.OrdinalIgnoreCase);
        bool creatorUsernameMatch = !string.IsNullOrWhiteSpace(group.CreatorUsername)
            && string.Equals(group.CreatorUsername, client.Username, StringComparison.OrdinalIgnoreCase);
        if (!creatorIdentityMatch || !creatorUsernameMatch)
        {
            var tip = JsonSerializer.SerializeToUtf8Bytes(new GroupChatMessage(group.Id, "系统", "仅群组创建者可注销群聊。"));
            try { await SendFrameAsync(client.Stream, MsgGroupChat, tip); } catch { }
            return;
        }

        _groups.TryRemove(group.Id, out _);
        SaveGroupStore();

        var notice = JsonSerializer.SerializeToUtf8Bytes(new GroupChatMessage(group.Id, "系统", $"群聊 \"{group.Name}\" 已被创建者注销。"));
        var closed = JsonSerializer.SerializeToUtf8Bytes(new GroupClosedNotice(group.Id, group.Name, "群聊已被创建者注销。"));
        foreach (var member in group.Members.Values)
        {
            member.JoinedGroups.TryRemove(group.Id, out _);
            try { await SendFrameAsync(member.Stream, MsgGroupChat, notice); } catch { }
            try { await SendFrameAsync(member.Stream, MsgGroupClosed, closed); } catch { }
        }

        Log?.Invoke($"群聊 \"{group.Name}\" 已由创建者 {client.Username} 注销");
        await BroadcastClientListAsync();
    }

    private static GroupMemberSignedChange? ParseSignedGroupChange(byte[] payload)
    {
        try { return JsonSerializer.Deserialize<GroupMemberSignedChange>(payload); }
        catch { return null; }
    }

    private void SaveGroupStore()
    {
        try
        {
            var snapshot = _groups.Values
                .Select(g => new GroupStoreItem(g.Id, g.Name, g.CreatorIdentityId, g.CreatorUsername))
                .ToArray();
            string json = JsonSerializer.Serialize(snapshot);
            File.WriteAllText(_groupStorePath, json, Encoding.UTF8);
        }
        catch { }
    }

    private void LoadGroupStore()
    {
        try
        {
            if (!File.Exists(_groupStorePath))
                return;

            string json = File.ReadAllText(_groupStorePath, Encoding.UTF8);
            var groups = JsonSerializer.Deserialize<List<GroupStoreItem>>(json);
            if (groups is null) return;

            foreach (var item in groups)
            {
                if (string.IsNullOrWhiteSpace(item.Id)
                    || string.IsNullOrWhiteSpace(item.Name))
                    continue;

                _groups[item.Id] = new ChatGroup
                {
                    Id = item.Id,
                    Name = item.Name,
                    CreatorIdentityId = item.CreatorIdentityId,
                    CreatorUsername = item.CreatorUsername,
                    KeyHolderId = null
                };
            }
        }
        catch { }
    }

    private bool VerifySignedGroupChange(ConnectedClient client, GroupMemberSignedChange signed, string expectedType)
    {
        if (!string.Equals(signed.ChangeType, expectedType, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(signed.ActorIdentityId, client.IdentityId, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(client.IdentityPublicKey) || string.IsNullOrWhiteSpace(signed.Signature)) return false;
        if (!TryDecodeBase64(client.IdentityPublicKey, out var pub)) return false;
        byte[] data = Encoding.UTF8.GetBytes($"{signed.GroupId}|{signed.ActorIdentityId}|{signed.ChangeType}|{signed.PayloadBase64}");
        if (!TryDecodeBase64(signed.Signature, out var sig)) return false;
        return IdentityCryptoVerifier.VerifyRaw(pub, data, sig);
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

    public async Task SendBroadcastAsync(string message)
    {
        message = message.Trim();
        if (string.IsNullOrWhiteSpace(message)) return;
        if (message.Length > MaxBroadcastLength)
            message = message[..MaxBroadcastLength];

        byte[] payload = Encoding.UTF8.GetBytes(message);
        foreach (var client in _clients.Values.Where(c => !string.IsNullOrEmpty(c.Username)))
        {
            try { await SendFrameAsync(client.Stream, MsgServerBroadcast, payload); } catch { }
        }
        Log?.Invoke($"公告已发送: {message}");
    }

    private void NotifyActivePairs()
    {
        int pairs = _clients.Values.Sum(c => c.PairedPeers.Count) / 2;
        ActivePairsChanged?.Invoke(pairs);
    }

    private static async Task SendFrameAsync(NetworkStream stream, byte type, byte[] payload)
    {
        int totalLen = 1 + payload.Length;
        byte[] header = new byte[5];
        BitConverter.TryWriteBytes(header.AsSpan(0, 4), totalLen);
        header[4] = type;

        var sendLock = _streamSendLocks.GetOrAdd(stream, static _ => new SemaphoreSlim(1, 1));
        await sendLock.WaitAsync();
        try
        {
            await stream.WriteAsync(header);
            await stream.WriteAsync(payload);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private static void SendFrameSync(NetworkStream stream, byte type, byte[] payload)
    {
        int totalLen = 1 + payload.Length;
        byte[] header = new byte[5];
        BitConverter.TryWriteBytes(header.AsSpan(0, 4), totalLen);
        header[4] = type;

        var sendLock = _streamSendLocks.GetOrAdd(stream, static _ => new SemaphoreSlim(1, 1));
        sendLock.Wait();
        try
        {
            stream.Write(header);
            stream.Write(payload);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private static async Task<(byte type, byte[] payload)> ReadFrameAsync(NetworkStream stream)
    {
        byte[] lenBuf = new byte[4];
        await ReadExactAsync(stream, lenBuf);
        int totalLen = BitConverter.ToInt32(lenBuf);
        if (totalLen <= 0 || totalLen > MaxFrameLength)
            throw new InvalidDataException($"无效帧长度: {totalLen}");

        byte[] typeBuf = new byte[1];
        await ReadExactAsync(stream, typeBuf);
        byte type = typeBuf[0];

        int payloadLen = totalLen - 1;
        if (payloadLen <= 0)
            return (type, []);

        byte[] payload = new byte[payloadLen];
        await ReadExactAsync(stream, payload);
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

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset));
            if (read == 0) throw new IOException("Connection closed.");
            offset += read;
        }
    }

    private record ClientListEntry(string Id, string Username);
    private record GroupListEntry(string Id, string Name, int MemberCount);
    private record GroupChatMessage(string GroupId, string Sender, string? Message = null, string? Data = null);
    private record GroupKeyExchangeRequest(string GroupId, string PublicKey);
    private record GroupKeyExchangeForward(string GroupId, string JoinerId, string PublicKey);
    private record GroupKeyDeliverRequest(string GroupId, string TargetId, string PublicKey, string EncryptedKey);
    private record GroupKeyDeliverResponse(string GroupId, string PublicKey, string EncryptedKey);
    private record GroupStartedInfo(string GroupId, string GroupName, bool IsKeyHolder);
    private record GroupClosedNotice(string GroupId, string GroupName, string Reason);
    private record ChatStartInfo(
        string PeerId,
        string PeerName,
        string? IdentityId = null,
        string? IdentityPublicKey = null,
        string? SignedPreKeyPublicKey = null,
        string? SignedPreKeySignature = null);
    private record RegisterPayload(string Username, string IdentityId, string IdentityPublicKey, string SignedPreKeyPublicKey, string SignedPreKeySignature);
    private record IdentityLookupResult(bool Found, string? PeerId, string? PeerName);
    private record GroupMemberSignedChange(string GroupId, string ActorIdentityId, string ChangeType, string PayloadBase64, string Signature);
    private record GroupStoreItem(string Id, string Name, string CreatorIdentityId, string CreatorUsername);

    private sealed class ChatGroup
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
        public required string Name { get; init; }
        public string CreatorIdentityId { get; set; } = "";
        public string CreatorUsername { get; set; } = "";
        public string? KeyHolderId { get; set; }
        public ConcurrentDictionary<string, ConnectedClient> Members { get; } = new();
    }

    private sealed class ConnectedClient(TcpClient tcp) : IDisposable
    {
        public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
        public string Username { get; set; } = "";
        public string IdentityId { get; set; } = "";
        public string IdentityPublicKey { get; set; } = "";
        public string SignedPreKeyPublicKey { get; set; } = "";
        public string SignedPreKeySignature { get; set; } = "";
        public byte? LastInboundMessageType { get; set; }
        public DateTime LastInboundAtUtc { get; set; }
        public TcpClient Tcp { get; } = tcp;
        public NetworkStream Stream { get; } = tcp.GetStream();
        public ConcurrentDictionary<string, bool> PairedPeers { get; } = new();
        public ConcurrentDictionary<string, bool> JoinedGroups { get; } = new();
        public bool HasAnyPair => !PairedPeers.IsEmpty;
        public bool IsInGroup => !JoinedGroups.IsEmpty;
        public bool IsAvailable => true;
        public void Dispose() => Tcp.Dispose();
    }
}
