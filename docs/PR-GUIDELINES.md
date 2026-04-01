# UnMessage PR 规范

> 目标：让每个 PR 可审查、可回滚、可验证。

当前文档版本：**v1.1.0**

## 1. 分支与粒度

- 分支命名：`feature/*`、`fix/*`、`refactor/*`、`docs/*`。
- 单个 PR 只解决一个主题（功能 / 修复 / 重构不要混合）。
- 建议控制在 **300 行核心变更以内**（自动生成文件除外）。

## 2. 提交信息（建议 Conventional Commits）

示例：

- `feat(protocol): add signed group member change verification`
- `fix(client): reject replayed secure chat messages`
- `docs(readme): rewrite architecture and quickstart`

## 3. PR 标题格式

`<type>(<scope>): <summary>`

- type: `feat|fix|refactor|docs|test|chore`
- scope: `crypto|protocol|client|server|ui|build|docs`

## 4. PR 描述模板（必填）

- 背景：为什么要改
- 变更点：做了什么
- 影响范围：crypto / protocol / client / server
- 兼容性：是否破坏协议或数据
- 验证方式：如何构建、如何测试
- 风险与回滚：失败时如何回退

## 5. 合并前检查清单

- [ ] `dotnet build .\UnMessage.slnx` 通过
- [ ] 关键路径手工验证（连接、注册、私聊、群聊）
- [ ] 如涉及 UI，已验证浅色主题下按钮状态（普通/悬停/禁用）、页签选中态、输入区可用性（客户端与服务端）
- [ ] 如涉及用户列表，已验证“在线可见性”与“在线列表点击返回会话”行为
- [ ] 如涉及群聊，已验证多群并发、退出群聊、结束聊天后重新打开、管理员移交等场景
- [ ] 协议字段变更已更新 `docs/PROTOCOL.md`
- [ ] README 与文档同步更新
- [ ] 无明文密钥、无调试后门、无硬编码敏感信息

## 6. 评审重点

## 6.1 Crypto 模块

- 是否使用安全随机数
- 是否正确校验签名 / Base64 / 长度
- 失败路径是否默认拒绝

## 6.2 Protocol 模块

- 消息类型与字段是否保持兼容
- 是否限制帧长度与输入上限
- 是否处理未知字段与异常帧

## 6.3 Client/Server 模块

- UI 线程与网络线程边界是否清晰
- 异常是否被记录并可观测
- 断线重连、握手恢复是否稳定

## 7. 禁止事项

- 禁止把重构与功能耦合在一个 PR。
- 禁止未更新文档的协议破坏性修改。
- 禁止在 PR 中提交与任务无关的大规模格式化。
