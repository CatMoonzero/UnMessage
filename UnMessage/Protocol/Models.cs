namespace UnMessage;

/// <summary>
/// 在线用户条目（JSON 反序列化用）。
/// </summary>
public record ClientEntry(string Id, string Username);

/// <summary>
/// 可用群聊条目（JSON 反序列化用）。
/// </summary>
public record GroupEntry(string Id, string Name, int MemberCount);

/// <summary>
/// 私聊开始信息（含对方 ID 和用户名）。
/// </summary>
record ChatStartInfo(
    string PeerId,
    string PeerName,
    string? IdentityId = null,
    string? IdentityPublicKey = null,
    string? SignedPreKeyPublicKey = null,
    string? SignedPreKeySignature = null);

/// <summary>
/// 群聊消息。Message 用于明文系统通知，Data 用于 Base64 编码的加密用户消息。
/// </summary>
record GroupChatMessage(string GroupId, string Sender, string? Message = null, string? Data = null);

/// <summary>群聊密钥交换：加入者发送 ECDH 公钥。</summary>
record GroupKeyExchangeRequest(string GroupId, string PublicKey);

/// <summary>群聊密钥交换：服务器转发给密钥持有者（含加入者 ID）。</summary>
record GroupKeyExchangeForward(string GroupId, string JoinerId, string PublicKey);

/// <summary>群聊密钥分发：密钥持有者发送加密后的群聊密钥（含目标 ID）。</summary>
record GroupKeyDeliverRequest(string GroupId, string TargetId, string PublicKey, string EncryptedKey);

/// <summary>群聊密钥分发：服务器转发给加入者（不含目标 ID）。</summary>
record GroupKeyDeliverResponse(string GroupId, string PublicKey, string EncryptedKey);

/// <summary>进入群聊通知。</summary>
record GroupStartedInfo(string GroupId, string GroupName, bool IsKeyHolder);

/// <summary>群聊被注销通知。</summary>
record GroupClosedNotice(string GroupId, string GroupName, string Reason);

record RegisterPayload(string Username, string IdentityId, string IdentityPublicKey, string SignedPreKeyPublicKey, string SignedPreKeySignature);

record PeerBundle(string PeerId, string PeerName, string IdentityId, string IdentityPublicKey, string SignedPreKeyPublicKey, string SignedPreKeySignature);

record X3DHInit(string PeerId, string EphemeralPublicKey, string SenderIdentityId, string SenderIdentityPublicKey, long Counter, string Signature);

record X3DHResponse(string PeerId, string EphemeralPublicKey, long Counter, string Signature);

record SecureChatMessage(long Counter, string CiphertextBase64, string NonceBase64, string TagBase64);

record GroupMemberSignedChange(string GroupId, string ActorIdentityId, string ChangeType, string PayloadBase64, string Signature);
