# UnMessage 协议规范（UMP v1）

> 目标：提供接近 Signal 的端到端聊天协议能力（身份密钥 + 预密钥签名 + 初始握手 + 会话保密 + 重放防护）。

当前文档版本：**v1.1.0**

## 1. 设计目标

- 服务器只做中继与状态协调，不持有会话明文。
- 使用长期身份密钥（Ed25519）+ 签名预密钥（X25519）做身份绑定。
- 私聊使用 X3DH 风格的单次握手建立会话密钥。
- 消息使用 AES-256-GCM，并附带递增计数器抵御重放。
- 群聊采用“群主密钥持有者 + 点对点分发群密钥”的模式。

---

## 2. 传输层帧格式

所有消息均通过统一帧传输：

- `Length`：4 字节（Int32，小端），表示 `Type + Payload` 的总长度。
- `Type`：1 字节，消息类型。
- `Payload`：可变长字节数组。

约束：

- `MaxFrameLength = 1MB`
- 超长帧或无效长度应直接拒绝。

---

## 3. 消息类型（Type）

### 3.1 连接与会话

- `0x10` Register
- `0x18` RegisterResult
- `0x19` RegisterForce
- `0x11` ClientList
- `0x12` ChatRequest
- `0x13` ChatStart
- `0x15` PeerDisconnect
- `0x16` EndChat
- `0x14` ServerShutdown
- `0x17` ServerBroadcast

### 3.2 私聊加密

- `0x30` X3DHInit
- `0x31` X3DHResponse
- `0x02` Chat（密文）

### 3.3 身份检索

- `0x2A` IdentityLookup
- `0x2B` IdentityLookupResult

### 3.4 群聊

- `0x20` GroupCreate
- `0x21` GroupJoin
- `0x23` GroupChat
- `0x24` GroupList
- `0x25` GroupStarted
- `0x26` GroupKeyExchange
- `0x27` GroupKeyDeliver
- `0x32` GroupMemberSignedChange
- `0x33` GroupClosed

---

## 4. 密钥体系

## 4.1 身份材料

客户端注册时上报：

- `IdentityPublicKey`（Ed25519）
- `SignedPreKeyPublicKey`（X25519）
- `SignedPreKeySignature`（Identity 私钥签名）
- `IdentityId = SHA256(IdentityPublicKey)`

服务端/对端校验：

`VerifyRaw(IdentityPublicKey, SignedPreKeyPublicKey, SignedPreKeySignature) == true`

## 4.2 安全号码

安全号码由身份公钥哈希截断生成，用于人工比对。

---

## 5. 私聊握手（X3DH 风格）

## 5.1 预备阶段

`ChatStart` 下发对端身份包后，双方先验证签名预密钥。

## 5.2 发起方

1. 生成临时 X25519 密钥对 `ephA`。
2. 对 `ephA.public` 使用身份私钥签名。
3. 发送 `X3DHInit`：
   - `EphemeralPublicKey`
   - `SenderIdentityId`
   - `SenderIdentityPublicKey`
   - `Counter`
   - `Signature`

## 5.3 响应方

1. 校验发起方签名。
2. 使用本地 `SignedPreKeyPrivateKey` 与 `ephA.public` 派生共享密钥。
3. 建立会话并发送 `X3DHResponse`（附响应方临时公钥签名）。

## 5.4 发起方完成

发起方用 `ephA.private` 与对端 `SignedPreKeyPublicKey` 派生同一共享密钥，完成建立。

---

## 6. 聊天消息格式

私聊 `MsgChat` 的 Payload：

- 前 8 字节：目标对端 ID
- 后续：`SecureChatMessage` JSON

`SecureChatMessage` 字段：

- `Counter`：单调递增计数器
- `NonceBase64`
- `TagBase64`
- `CiphertextBase64`

安全要求：

- Counter 需严格递增；`counter <= last` 判定为重放并拒绝。
- 解密认证失败（Tag mismatch）触发自动重建会话。

---

## 7. 群聊协议

- 建群/加群使用 `GroupMemberSignedChange` 进行签名声明。
- 新增群注销操作：`ChangeType = dissolve`，仅群创建者（`CreatorIdentityId`）可成功执行。
- 新增群退出操作：`ChangeType = leave`，管理员退出时自动移交管理权。
- 所有群聊协议负载均包含 `GroupId` 字段，支持用户同时加入多个群聊。
- 群密钥由持有者生成（32 字节随机值）。
- 新成员通过 ECDH 请求点对点分发群密钥。
- 群消息使用群密钥 AES-256-GCM 加密后再广播中继。
- `MsgGroupStarted` 负载升级为 `GroupStartedInfo(GroupId, GroupName, IsKeyHolder)`，用于客户端确定当前群身份与密钥角色。
- 群注销完成后，服务端向在线成员发送 `MsgGroupClosed(GroupId, GroupName, Reason)`，客户端据此强制退出该群并清理群会话状态。

群生命周期规则：

- 群组创建后默认长期保留，不因在线人数为 0 自动删除。
- 服务端重启后从持久化存储恢复群组元数据。
- 仅创建者主动发起 `dissolve` 时删除群组。

---

## 8. 与 Signal 的相似与差异

相似点：

- 身份密钥 + 预密钥签名绑定。
- X3DH 风格初始握手。
- AEAD 加密与完整性验证。

差异点（当前版本）：

- 尚未实现 Double Ratchet（前向/后向保密窗口有限）。
- 群聊采用中心化密钥分发，而非 Sender Keys。
- 未实现预密钥池与离线消息完整投递策略。

---

## 9. 兼容性与版本化建议

建议后续加入：

- 帧头协议版本字段（`ProtocolVersion`）。
- 能力协商（`features: [x3dh, group-signed-change, ...]`）。
- 消息扩展字段保留（未知字段忽略）。
