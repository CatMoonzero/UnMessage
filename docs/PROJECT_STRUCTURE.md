# 项目目录结构（当前）

本目录用于集中维护项目文档，避免说明散落在根目录与代码注释中。

当前文档版本：**v1.1.0**

## 根目录

- `README.md`：项目介绍、快速开始、功能说明
- `CHANGELOG.md`：版本更新日志
- `LICENSE`：GPL-3.0 许可证
- `Directory.Build.props`：跨项目通用构建配置
- `UnMessage.slnx`：解决方案入口

## 子项目

- `UnMessage/`：Windows 客户端（WinForms）
  - `Crypto/`：密码学实现
  - `Protocol/`：协议模型与自检
  - `Client/`：客户端网络与 UI（含现代化主题样式）
- `UnMessage.Server/`：Windows 服务端（WinForms）
  - `Crypto/`：服务端验签相关能力
  - `Network/`：中继转发与会话协调
  - `ServerForm*`：服务端管理 UI（含现代化主题样式）
  - `groups.json`：群组持久化存储（运行时生成）

## 脚本目录

- `scripts/build-all.ps1`：构建全部项目
- `scripts/build-android.ps1`：构建 Android 客户端（可选安装到设备）
- `scripts/run-server.ps1`：启动服务端
- `scripts/run-client.ps1`：启动 Windows 客户端

## 群注销事件链路（GroupClosed）

- `UnMessage.Server/Network/RelayServer.cs`
  - 处理创建者 `dissolve` 后下发 `MsgGroupClosed (0x33)`，并清理在线成员群状态。
- `UnMessage/Protocol/Models.cs`
  - 定义 `GroupClosedNotice(GroupId, GroupName, Reason)` 协议负载模型。
- `UnMessage/Client/ChatPeer.cs`
  - 接收 `MsgGroupClosed` 并触发 `GroupClosed` 事件。
- `UnMessage/Client/ChatForm.cs`
  - 响应 `GroupClosed` 事件，强制关闭群会话并清理本地群状态。

## 用户列表交互关键点（v1.1.0）

- 在线列表保留已建联对象，不因已有会话而隐藏。
- 对于已建联在线用户，点击“在线”列表项可直接返回既有会话。
- 右键群聊支持“注销群聊”和“退出群聊”，仅对已加入的群显示。
- 用户可同时加入多个群聊，每个群聊独立标签页与加密会话。
