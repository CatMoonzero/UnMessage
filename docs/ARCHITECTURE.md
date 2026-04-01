# UnMessage 工程架构

当前代码按职责拆分为三个核心模块：`crypto / protocol / client`。

当前文档版本：**v1.1.0**

## 1. 模块说明

- **crypto**
  - 密钥生成、签名、验签、共享密钥派生、AEAD 加解密
  - 典型文件：`IdentityCrypto.cs`、`E2ECrypto.cs`、`IdentityCryptoVerifier.cs`

- **protocol**
  - 协议模型、帧语义、消息结构、自检逻辑
  - 典型文件：`Models.cs`、`ProtocolSelfCheck.cs`

- **client**
  - 连接管理、会话状态机、事件分发、UI 交互
  - 典型文件：`ChatPeer.cs`、`ChatForm.cs`
  - 当前 UI 层已引入统一主题样式（按钮分级、页签高亮、输入区与状态栏视觉统一）

## 2. 目录结构（当前）

- `UnMessage/Crypto/*`
- `UnMessage/Protocol/*`
- `UnMessage/Client/*`
- `UnMessage.Server/Crypto/*`
- `UnMessage.Server/Network/*`

## 3. 依赖方向

推荐保持单向依赖：

`client -> protocol -> crypto`

服务器端同理：

`network(server runtime) -> protocol -> crypto`

避免反向依赖（crypto 不依赖上层）。

## 4. UI 层现状

- 客户端与服务端均采用统一现代化主题（浅色基调、按钮分级、禁用态反馈）。
- UI 美化不影响协议层与密码学层行为，属于表现层改进。
- 客户端用户列表保留在线可见性：已建联对象仍显示在“在线”列表，可直接点击返回既有会话。

## 5. 群组生命周期

- 群组元数据由服务端持久化存储（`groups.json`）。
- 群组管理员为创建者（按 `CreatorIdentityId` 识别）。
- 群组不会因成员为 0 或服务端重启自动消失，仅创建者可注销。
- 客户端通过用户列表右键菜单触发“注销群聊”或“退出群聊”，结束聊天仅用于关闭标签页。
- 群注销时服务端会广播群关闭事件，在线成员被强制踢出并清理本地群状态。
- 支持用户同时加入多个群聊，每个群聊独立加密密钥与会话管理。
