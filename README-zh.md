# Repository - 文件服务器

一个功能完善的跨平台文件服务器，基于 ASP.NET Core 构建，提供文件浏览、预览、上传、下载等功能，支持多种安全保护机制。

## 核心功能

### 文件管理
- **目录浏览**：Web 界面直观展示目录结构，支持面包屑导航
- **文件预览**：支持文本、图片、音频、视频等多种格式在线预览
- **文件下载**：支持单文件下载，可配置下载大小限制
- **文件上传**：支持文件上传，可配置允许的扩展名和大小限制

### 安全防护
- **IP 过滤**：支持白名单模式，仅允许指定 IP 访问
- **黑名单机制**：支持路径黑名单，阻止访问敏感目录
- **DDoS 防护**：限制单个 IP 的请求频率，防止恶意攻击
- **路径安全**：防止目录遍历攻击，确保访问范围受限

### 目录保护
支持两种保护模式：

#### Token 模式（简单模式）
- 通过 URL 参数传递访问令牌
- 适合一般场景，客户端实现简单
- 支持 SHA256 哈希验证

#### Secure 模式（高级安全模式）
- RSA + AES 混合加密
- 双向身份认证
- 防重放攻击（Nonce + 时间戳）
- 前向安全（每次会话生成新密钥）
- 适合企业级安全需求

### HTTPS 支持
- 支持 HTTP 和 HTTPS 同时运行
- 支持 HTTP 到 HTTPS 自动重定向
- 支持 PFX 证书配置

### 管理页面
- Web 界面管理仓库文件
- CHAP 协议加密认证
- WebSocket 实时通信
- 支持操作：
  - 目录浏览和导航
  - 文件/目录移动到回收站
  - 文件/目录移动重命名
  - 创建新目录
  - 文件上传到当前目录
- 安全响应头防护（XSS、点击劫持等）

### Windows 通知
- 支持 Windows 原生 Toast 通知
- 访问事件、警告、错误实时提醒
- 可通过配置开关控制

## 项目结构

```
Repository/
├── Controllers/           # 控制器
│   ├── RepositoryController.cs    # 主页面路由
│   ├── AdminController.cs         # 管理页面控制器
│   ├── DirectoryController.cs     # 目录 API
│   ├── FileController.cs          # 文件操作 API
│   ├── UploadController.cs        # 上传 API
│   ├── KeyManagementController.cs # 密钥管理 API
│   └── SecureServerHandler.cs     # Secure 验证处理
├── Services/              # 服务层
│   ├── ConfigManager.cs           # 配置管理
│   ├── ProtectionService.cs       # 目录保护服务
│   ├── BlacklistService.cs        # 黑名单服务
│   ├── DDoSProtectionService.cs   # DDoS 防护服务
│   ├── KeyManagementService.cs    # 密钥管理服务
│   ├── SecureSessionService.cs    # 安全会话服务
│   ├── ChapAuthService.cs         # CHAP 认证服务
│   ├── AdminConnectionManager.cs  # 管理员连接管理
│   ├── Logger.cs                  # 日志服务
│   └── NotificationService.cs     # 通知服务
├── Middleware/            # 中间件
│   ├── SecurityHeadersMiddleware.cs    # 安全响应头
│   ├── IPBlockingMiddleware.cs         # IP 过滤
│   ├── RateLimitingMiddleware.cs       # 速率限制
│   └── SubdirectoryRoutingMiddleware.cs # 子目录路由
├── Models/                # 数据模型
│   └── Config.cs                  # 配置模型
├── docs/                  # 文档
│   ├── api/                       # API 文档
│   ├── security/                  # 安全报告
│   └── updates/                   # 更新日志
├── pytest/                # Python 示例
├── Repository/            # 仓库目录（文件存储）
│   └── .keys/                     # 服务端密钥存储
├── Config.json            # 主配置文件
└── help.txt               # 帮助文档
```

## API 接口

### 文件操作
| 接口 | 方法 | 说明 |
|------|------|------|
| `/api/files` | GET | 获取目录列表 |
| `/api/download/{path}` | GET | 下载文件 |
| `/api/preview/{path}` | GET | 预览文件 |
| `/api/upload/{path}` | POST | 上传文件 |

### 密钥管理（Secure 模式）
| 接口 | 方法 | 说明 |
|------|------|------|
| `/api/keys/server` | GET | 获取服务器公钥 |
| `/api/keys/register` | POST | 注册客户端公钥 |
| `/api/keys/verify` | POST | 验证加密请求 |
| `/api/keys/client/{id}` | DELETE | 移除客户端注册 |

### 管理页面
| 接口 | 方法 | 说明 |
|------|------|------|
| `/admin` | GET | 管理页面 |
| `/admin/ws` | WebSocket | WebSocket 通信 |
| `/admin/api/logs` | GET | 获取日志文件列表 |
| `/admin/api/logs/{file}` | GET | 获取日志文件内容 |
| `/admin/api/upload` | POST | 上传文件 |

## 配置说明

主配置文件 `Config.json` 支持以下配置项：

### 基础配置
| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| IP | string | "::" | 监听地址 |
| Port | int | 8000 | HTTP 端口 |
| RepositoryPath | string | "./Repository" | 仓库路径 |
| Notification | bool | false | Windows 通知 |

### 管理员配置
| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| AdminEnabled | bool | false | 启用管理功能 |
| AdminUsername | string | "" | 管理员用户名 |
| AdminPassword | string | "" | 管理员密码 |

### 安全配置
| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| IPBlocking | bool | false | IP 白名单 |
| IPBlockingList | string | "" | 白名单列表 |
| Blacklist | string | "" | 路径黑名单 |
| DDoSProtection | bool | true | DDoS 防护 |
| MaxRequestsPerMinute | int | 100 | 每分钟最大请求数 |
| BlockDurationMinutes | int | 30 | 封禁时长 |

#### 路径黑名单格式
使用 `|` 竖线分隔多个条目，支持以下格式：

| 格式 | 示例 | 说明 |
|------|------|------|
| 精确路径 | `secret/data.txt` | 匹配指定路径 |
| 通配符 | `*.log` | 匹配所有 .log 文件 |
| 目录通配 | `temp/*` | 匹配 temp 目录下所有内容 |
| 子目录匹配 | `%/node_modules` | 匹配任意子目录中的 node_modules |
| 文件引用 | `/path/to/blacklist.txt` | 从文件读取黑名单列表 |

示例：
```
$RECYCLE.BIN|System Volume Information|*.log|%/node_modules|temp/*
```

### 上传配置
| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| UploadEnabled | bool | false | 启用上传 |
| MaxUploadSizeMB | int | 50 | 最大上传大小 |
| AllowedUploadExtensions | string | ... | 允许的扩展名 |
| AllowOverwrite | bool | false | 允许覆盖 |
| AllowRootUpload | bool | false | 允许上传到根目录 |
| MaxDiskUsagePercent | int | 0 | 磁盘占用限制（百分比） |

### 预览配置
| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| PreviewEnabled | bool | true | 启用预览 |
| PreviewExtensions | string | ... | 文本预览扩展名 |
| ImagePreviewExtensions | string | ... | 图片预览扩展名 |
| AudioPreviewExtensions | string | ... | 音频预览扩展名 |
| VideoPreviewExtensions | string | ... | 视频预览扩展名 |

### 访问控制
| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| ForbiddenDownloadPaths | string | "" | 禁止下载路径 |
| ForbiddenPreviewPaths | string | "" | 禁止预览路径 |
| HiddenPaths | string | "" | 隐藏路径 |
| ProtectEnabled | bool | true | 启用保护功能 |
| ProtectPaths | string | "" | 受保护路径 |

### HTTPS 配置
| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| HttpsEnabled | bool | false | 启用 HTTPS |
| HttpsPort | int | 443 | HTTPS 端口 |
| HttpsCertificatePath | string | "" | 证书路径（CRT/KEY 格式） |
| HttpsCertificatePassword | string | "" | 证书密码 |
| HttpsRedirectEnabled | bool | false | HTTP 重定向 |
| HttpEnabled | bool | true | 启用 HTTP |
| Domain | string | "" | 服务器域名（自动证书签名） |

## 目录保护配置

在需要保护的目录下创建 `Protectionlock.json` 文件：

### Token 模式
```json
{
  "auth_method": "token",
  "token": "your_password_here"
}
```

访问方式：`/api/files?path=protected_dir&token=sha256_hash`

### Secure 模式
```json
{
  "auth_method": "secure",
  "client_id": "unique_client_id",
  "shared_token": "shared_secret_token"
}
```

需要使用支持 RSA/AES/HMAC 的专业客户端完成验证流程。

## 安全特性

### 受保护目录隐藏
- 受保护目录在目录列表中不显示
- 直接访问受保护目录返回 404，不暴露目录存在信息
- `.keys` 目录自动隐藏，防止密钥泄露

### 防重放攻击
- Nonce 缓存机制，防止请求重放
- 时间戳验证，限制请求有效期（±60秒）

### 路径安全
- 防止目录遍历攻击（`../` 等）
- 路径规范化处理
- 访问范围限制在仓库目录内

## 日志系统

- 自动记录所有访问请求
- 支持警告和错误日志
- 日志文件按日期自动分割
- 存储在 `logs/` 目录下

## 跨平台支持

- 支持 Windows、Linux、macOS
- Windows 特有功能（窗口置顶、后台运行、通知）在其他平台自动禁用
- 配置文件热重载，无需重启服务

## 注意事项

1. 修改配置文件后部分配置需要重启服务
2. 确保仓库目录有读写权限
3. Secure 模式需要专业客户端支持
4. HTTPS 支持自动生成自签名证书（CRT/KEY 格式）
5. 上传功能默认禁用，启用后注意安全性
6. ProtectEnabled 禁用后可加快启动速度，但受保护目录将无需验证即可访问
