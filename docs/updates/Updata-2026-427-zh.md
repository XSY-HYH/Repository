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
