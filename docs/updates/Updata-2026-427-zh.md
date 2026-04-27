# 更新日志 - 2026年4月27日

## Bug修复

### 1. WebSocket 连接被请求头过滤拦截
- 修复了 `/admin/ws` 的 WebSocket 连接因缺少 `Accept` 请求头而被拒绝的问题
- 浏览器 WebSocket API 在连接握手时不支持自定义请求头
- **解决方案**：对 WebSocket 端点（`/admin/ws`、`*/ws`）跳过请求头检查
- 修改 `RequestHeaderFilteringService.ValidateRequestHeaders()` 检测 WebSocket 路径

### 2. 临时封禁列表格式重构
- `TemporarilyBlockedIPs` 格式从 `"IP,时间戳|IP,时间戳"` 改为 `"IP1,IP2,IP3"`（仅逗号分隔 IP）
- 时间戳现在通过内存中的 `_tempBlockTimes` 并发字典管理
- **原因**：配置格式更简洁，时间戳是运行时数据而非配置项

### 3. 重启重置临时封禁计时器
- 服务器重启时，所有临时封禁 IP 的封禁时间重置为当前时间
- 确保行为一致：重启 = 所有临时封禁重新计时
- 通过服务启动时的 `InitializeTempBlockLists()` 方法实现

## 功能改进

### 1. 永久封禁升级机制
- 当已被临时封禁的 IP 继续超过 `MaximumRequestsPerSecond` 阈值时：
  - 该 IP 立即被永久封禁（添加到 `BlockedIPs`）
  - 从 `TemporarilyBlockedIPs` 中移除该 IP
  - 同时更新内存缓存和配置文件
- 新增 `RemoveFromTemporaryBlockList()` 方法处理清理逻辑
- 添加升级事件日志记录（`throttling.removed_from_temporary`）

### 2. 请求限速服务重写
- 完全重构临时封禁管理逻辑：
  - `_tempBlockTimes`：内存字典，追踪 IP → 封禁开始时间
  - `IsInTemporaryBlockList()`：根据内存时间戳和可配置时长判断是否仍在封禁期
  - `AddToTemporaryBlockList()`：配置只写入 IP，时间戳存入内存
  - `RemoveFromTemporaryBlockList()`：同时清理内存和配置
  - `CleanupExpiredData()`：定时清理，从两个存储中移除过期条目

## 配置变更

### TemporarilyBlockedIPs 格式变更
```
旧格式: "183.198.14.142,2026-04-27 16:24:50|192.168.1.1,2026-04-27 16:25:00"
新格式: "183.198.14.142,192.168.1.1"
```

**注意**：现有使用旧格式的配置需要手动清理，否则可能被识别为无效条目。

## 文件变更

### 修改文件
- `Services/RequestHeaderFilteringService.cs` - WebSocket 路径检测以绕过请求头过滤
- `Services/RequestThrottlingService.cs` - 完全重写临时封禁管理逻辑
- `lang.yml` - 添加 `throttling.*` 翻译键（5 个键 × 2 种语言）
- `help.txt` - 更新 `TemporarilyBlockedIPs` 文档说明

## 技术细节

### 新临时封禁流程
```
请求 → 检查频率 → ≥ 告警阈值？
    ↓ 是              ↓ 否
添加到临时列表      允许请求
（每次命中重置计时器）

在临时封禁期间，若继续请求：
    ↓ 超过最大阈值？
是 → 永久封禁 + 从临时列表移除
否  → 继续封禁（倒计时中）
```

### 内存与配置分离
| 数据 | 存储位置 | 重启后 |
|------|---------|--------|
| IP 地址 | 配置文件 (`TemporarilyBlockedIPs`) | 保留 |
| 封禁开始时间 | 内存 (`_tempBlockTimes`) | **重置** |
| 封禁时长 | 配置 (`BlockDurationMinutes`) | 可配置 |

## 已知问题

- 现有带旧时间戳格式的 `TemporarilyBlockedIPs` 条目可能需要手动迁移

---

## 功能移除：Secure 认证模式与目录保护

### 移除原因
Secure 认证模式（RSA/AES/HMAC 混合加密）设计过于复杂，实际使用场景有限，且维护成本高。目录保护功能（Protectionlock.json）与安全模型存在冗余。

### 删除的文件（8个）
| 文件 | 说明 |
|------|------|
| `Services/ProtectionService.cs` | 目录保护核心服务 |
| `Services/KeyManagementService.cs` | RSA 密钥管理服务 |
| `Services/SecureSessionService.cs` | 安全会话管理服务 |
| `Controllers/KeyManagementController.cs` | 密钥管理 API 控制器 |
| `Controllers/SecureServerHandler.cs` | Secure 协议处理器 |
| `Models/ProtectionLock.cs` | 保护锁数据模型 |
| `pytest/secure_client.py` | 安全认证 Python 客户端示例 |

### 新增文件（1个）
| 文件 | 说明 |
|------|------|
| `Services/PathSecurity.cs` | 保留 `.keys` 系统路径检查功能 |

### 核心变更
- **7 个 Controller** 移除 `ProtectionService` 依赖注入
- **Program.cs** 移除 3 个 DI 注册 + 服务初始化
- **Config / ConfigManager** 移除 `ProtectEnabled`、`ProtectPaths` 配置项
- **README.md / README-zh.md / help.txt** 清理所有保护功能文档
- **lang.yml** 清理全部保护相关翻译键（`protection.*`、`secure_session.*`、`key.*` 等）

### 统计
20 文件变更，+39 行，-1911 行，净减 1872 行代码。

---

## 功能升级：CHAP → CHAP-IEM（ID Encryption Mode）

### 协议概述
CHAP-IEM 是标准 CHAP 协议的衍生变体。核心差异在于：
- **标准 CHAP**：始终使用预共享密钥 K 进行加密
- **CHAP-IEM**：登录阶段使用 K 验证身份，之后切换为 **ID 链加密模式**——每次操作的加密密钥自动轮转

### 工作流程
```
登录阶段（与标准 CHAP 相同）：
  客户端 ──[K加密用户名]──→ 服务器验证身份
  服务器 ──[K加密响应+ID_1]──→ 客户端获得 ID_1

正常操作（ID 链模式）：
  操作1: 客户端[ID_1加密]──→ 服务器[ID_1解密]──→ 执行操作，生成ID_2
         服务器 ──[ID_1加密结果+ID_2]──→ 客户端获得ID_2，密钥轮转到ID_2

  操作2: 客户端[ID_2加密]──→ 服务器[ID_2解密]──→ 执行操作，生成ID_3
         服务器 ──[ID_2加密结果+ID_3]──→ 客户端获得ID_3，密钥轮转到ID_3

  密钥链: K → ID_1 → ID_2 → ID_3 → ...
```

### 异常恢复机制
当客户端与服务器的 ID 链不同步时（如丢包）：
1. 服务器检测到解密失败，尝试用 K 解析（恢复通道）
2. 若成功，使用 K 加密恢复包（包含当前有效 ID）
3. 客户端用 K 解密恢复包，获取当前 ID 并重新同步
4. **安全性**：K 仅用于恢复通道，不参与正常操作加密；具备前向保密性

### 后端变更（ChapAuthService.cs）

| 变更项 | 标准 CHAP | CHAP-IEM |
|--------|-----------|----------|
| 加密密钥 | 始终使用 K | 登录后切换为 ID 链 |
| 密钥存储 | `_sessionIdToKey`（密码哈希） | `_sessionCurrentKeys`（每会话独立 ID 密钥） |
| 操作解密 | 用 K 直接解密 | 遍历所有会话的当前 ID 密钥匹配 |
| 响应加密 | 用 K 加密 | 用**旧 ID** 加密（携带新 ID） |
| 恢复机制 | 无 | K 作为恢复通道 |

**新增方法：**
- `GenerateId()` / `GenerateIdBytes()` — 生成 32 字节随机 ID
- `HandleRecoveryRequest()` — 主动发送恢复包
- `GetK()` — 延迟获取预共享密钥

### 控制器变更（AdminController.cs）
- 登录成功后：`sessionKey = Convert.FromBase64String(response.NewId)` （从 K 切换到 ID_1）
- 操作响应：使用 `encryptKey`（旧 ID）而非新 ID 加密
- 每次操作成功后：`sessionKey` 自动轮转为新 ID

### 前端变更（Admin.JS）
- **变量拆分**：`k`（预共享密钥，仅用于登录和恢复）+ `currentKey`（当前 ID 密钥，用于操作加解密）
- **新增工具函数**：`base64ToBytes()` — Base64 字符串转 Uint8Array
- **sendOperation**：使用 `currentKey` 加密请求、解密响应，收到 NewId 后自动轮转

### 翻译键新增
| 键名 | 英文 | 中文 |
|------|------|------|
| `chap.session_mismatch` | Session ID mismatch | 会话ID不匹配 |
| `chap.recovery_ack_log` | CHAP-IEM recovery acknowledgment received, client IP: {0} | CHAP-IEM恢复确认已接收，客户端IP: {0} |
| `chap.resync_success` | ID chain resynchronized successfully | ID链已重新同步 |
| `chap.recovery_sent_log` | CHAP-IEM recovery packet sent, client IP: {0} | CHAP-IEM恢复包已发送，客户端IP: {0} |

### 安全性提升
| 特性 | 标准 CHAP | CHAP-IEM |
|------|-----------|----------|
| 密钥固定性 | K 长期不变 | 每次操作自动轮转 |
| 单包泄露影响 | 可解密后续所有通信 | 仅影响当前单次操作 |
| 前向保密 | 不支持（K 泄露则全部暴露） | 支持（旧密钥无法推导新密钥） |

### 参考文档
详见 [CHAP-IEM 技术文档](../api/CHAP-IEM.md) | [中文版](../api/CHAP-IEM-zh.md)
