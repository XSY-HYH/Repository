# 更新日志 - 2026年4月6日

## 新增功能

### 1. 管理页面（重大更新）
- 新增管理员后台管理界面 `/admin`
- 使用 CHAP（Challenge-Handshake Authentication Protocol）协议加密认证
- WebSocket 实时通信，支持以下操作：
  - 目录浏览和导航
  - 文件/目录移动到回收站
  - 文件/目录移动重命名
  - 创建新目录
  - **文件上传到当前目录**
- AES-256-CBC 加密所有通信数据
- 会话管理和密钥轮换

### 2. 管理员配置
- `AdminEnabled` - 启用/禁用管理功能
- `AdminUsername` - 管理员用户名
- `AdminPassword` - 管理员密码

### 3. 管理员连接管理
- 新增 `AdminConnectionManager` 服务
- 跟踪所有活跃的管理员 WebSocket 连接
- 支持 Ctrl+C 优雅关闭：
  - 强制中断所有 WebSocket 连接
  - 立即停止应用程序

### 4. 启动端口检查
- 启动前检查配置的端口是否被占用
- 支持 IPv4 和 IPv6 端口检查
- 支持 dual-stack 模式（`::` 同时监听 IPv4/IPv6）
- 端口被占用时显示红色错误提示
- 等待 5 秒后自动退出

## 功能改进

### 1. 文件删除改为移动到回收站
- 管理页面删除文件/目录不再直接删除
- 改为移动到 Windows 回收站
- 前端按钮文字从"删除"改为"回收站"
- 确认提示相应更新

### 2. 访问日志优化
- API 端点访问不再计入访问计数
- 不触发 Windows 通知
- 排除的路径：
  - `/api/*` - API 端点
  - `/swagger/*` - Swagger 文档
  - `/download/*` - 下载端点
  - `/admin/ws` - WebSocket 端点
  - `*/ws` - 其他 WebSocket 端点
- API 访问仍会记录到日志（标记为"API访问"）

## Bug修复

### 1. 路由问题
- 修复 `/admin` 路径被错误路由到仓库目录的问题
- `SubdirectoryRoutingMiddleware` 排除 `/admin` 路径

### 2. WebSocket 连接问题
- 修复 HTTP 方法不匹配（CONNECT vs GET）
- 修复 JSON 序列化问题（PascalCase vs camelCase）
- 修复无限循环问题（重复消息处理）

### 3. 路径处理问题
- 修复路径规范化问题
- 正确处理 Windows 文件系统路径
- 移除路径前导斜杠避免绝对路径问题

### 4. WebSocket 关闭异常
- 修复客户端主动关闭后服务端再次关闭导致的异常
- 添加 WebSocket 状态检查
- 安全关闭方法捕获异常避免崩溃

### 5. HTTP 安全响应头缺失
- 修复缺少关键安全响应头的问题
- 新增 7 个安全响应头防护
- 防止点击劫持、XSS、MIME混淆等攻击
- 详见 HRVU-20260001 漏洞报告

### 6. 日志接口安全加固
- 禁用公开的 `/api/logs` 接口（可能泄露敏感信息）
- 将日志功能移至管理面板 WebSocket
- 需要管理员认证才能访问日志文件

## 配置更新

### Config.json 新增字段
```json
{
  "AdminEnabled": true,
  "AdminUsername": "admin",
  "AdminPassword": "your_password"
}
```

## 文件变更

### 新增文件
- `Services/AdminConnectionManager.cs` - 管理员连接管理服务
- `Services/ChapAuthService.cs` - CHAP 认证服务
- `Middleware/SecurityHeadersMiddleware.cs` - 安全响应头中间件
- `STR-2026-40601.md` - 安全响应头测试报告
- `HRVU-20260001.md` - HTTP 安全响应头缺失漏洞报告
- `HRVU-20260002.md` - 日志接口敏感信息泄露漏洞报告

### 修改文件
- `Models/Config.cs` - 添加管理员配置属性
- `Program.cs` - 添加端口检查、Ctrl+C 处理、注册服务、安全响应头中间件
- `Controllers/AdminController.cs` - 管理页面控制器（新增）、添加日志接口、添加文件上传接口
- `Controllers/SharedController.cs` - 移除公开日志接口
- `Middleware/SubdirectoryRoutingMiddleware.cs` - 排除 /admin 路径
- `Services/Logger.cs` - 优化访问日志记录
- `help.txt` - 添加管理员配置说明
- `Config.json` - 添加管理员配置
- `DirectoryListing.Html` - 添加 favicon 链接

## 技术细节

### CHAP 认证流程
1. 客户端生成随机密钥（SHA256）
2. 发送加密的用户名
3. 服务端验证后返回会话ID和新密钥
4. 后续通信使用会话密钥加密

### 端口检查实现
```csharp
private static bool IsPortInUse(int port, string listenIP)
{
    // 使用 Socket 直接绑定检测端口
    // 支持 IPv4/IPv6 dual-stack
    // 设置 IPv6Only = 0 匹配 Kestrel 默认行为
}
```

### Ctrl+C 处理流程
1. 用户按下 Ctrl+C
2. 取消默认终止行为
3. 强制中断所有 WebSocket 连接
4. 立即停止应用程序

### 回收站实现
```csharp
// 使用 Microsoft.VisualBasic.FileIO
FileSystem.DeleteFile(fullPath, 
    UIOption.OnlyErrorDialogs, 
    RecycleOption.SendToRecycleBin);
```

## 升级说明

1. 更新程序后需要配置管理员账户
2. 在 `Config.json` 中设置：
   - `AdminEnabled`: true
   - `AdminUsername`: 管理员用户名
   - `AdminPassword`: 管理员密码
3. 访问 `https://your-server:port/admin` 进入管理页面
4. 管理页面删除操作行为变更（移动到回收站而非永久删除）
5. 启动时如端口被占用会提示并退出

## 安全说明

- 管理页面使用 CHAP 协议，密码不在网络中传输
- 所有通信使用 AES-256-CBC 加密
- 会话密钥每次操作后轮换
- 建议使用 HTTPS 访问管理页面
