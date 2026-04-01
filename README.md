# UnMessage

UnMessage 是一个基于 **.NET 10** 的端到端加密聊天工程，包含桌面客户端与中继服务器。

- `UnMessage`：WinForms 客户端
- `UnMessage.Server`：WinForms 管理面板 + TCP 中继服务

项目目标：在“服务器只转发”的前提下，实现接近 Signal 思路的私聊/群聊安全通信。

当前文档版本：**v1.1.0**

---

## 1. 功能概览

- 私聊与群聊（支持同时加入多个群聊，每个群独立加密会话）
- 身份卡交换与在线身份检索
- X3DH 风格初始握手（签名校验）
- AES-256-GCM 消息加密
- 消息计数器与重放防护
- 群成员变更签名（create / join / leave / dissolve）
- 本地信任、备注、屏蔽与安全提示
- 客户端 / 服务端 UI 现代化主题（统一配色、扁平按钮、圆角与悬停反馈）
- 群组管理员机制：创建者为管理员，退出时自动移交，成为管理员时有系统提示
- 群聊右键菜单：支持“退出群聊”和“注销群聊”（仅管理员）
- 结束聊天仅关闭群聊标签页，双击可重新打开
- 在线用户列表保留已建联对象，支持快速返回既有会话

---

## 2. 工程模块（已拆分）

代码按职责拆分为三个核心模块：

- **crypto**：密码学实现（签名、验签、密钥派生、AEAD）
- **protocol**：协议模型与消息语义
- **client**：连接状态机、会话管理、UI 交互

当前目录关键结构：

- `UnMessage/Crypto/*`
- `UnMessage/Protocol/*`
- `UnMessage/Client/*`
- `UnMessage.Server/Crypto/*`
- `UnMessage.Server/Network/*`

详见：`docs/ARCHITECTURE.md`

---

## 3. 协议设计

协议采用统一帧格式：`Length(4) + Type(1) + Payload(n)`，并定义了注册、会话、加密握手、群聊与身份检索消息类型。

核心安全机制：

- 注册时上报身份包（Identity + SignedPreKey + Signature）
- `0x30/0x31` 完成 X3DH 风格握手
- 私聊密文使用 AES-256-GCM + Counter
- `counter <= last` 直接拒绝，防重放

完整协议文档：`docs/PROTOCOL.md`

---

## 4. 构建与运行

### 4.1 构建

```powershell
dotnet build .\UnMessage.slnx
```

### 4.2 运行

1. 启动 `UnMessage.Server`
2. 启动 `UnMessage`
3. 在客户端输入服务器地址与端口并连接

---

## 5. 协作规范

仓库提供统一 PR 规范（分支命名、提交格式、描述模板、合并前检查清单）：

- `docs/PR-GUIDELINES.md`

---

## 6. 免责声明

- `docs/DISCLAIMER.md`

---

## 7. 许可证

本项目采用 **GNU General Public License v3.0**。

- SPDX: `GPL-3.0-only`
