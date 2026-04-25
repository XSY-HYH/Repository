# 更新日志 - 2026年4月25日

## 新增功能

### 1. 限流保护
- 新增限流保护服务 (`RateLimitProtectionService`)
- 与 DDoS 防护不同（DDoS 防护是封禁 IP）
- 当请求频率超过阈值时：
  - 暂时暂停服务
  - 停止接受新请求
  - 在配置的时间后自动恢复
- 配置选项：
  - `RateLimitProtection` - 启用/禁用限流保护
  - `RateLimitRequestsPerSecond` - 每秒最大请求数阈值
  - `RateLimitPauseMinutes` - 暂停时长（分钟）

### 2. 管理后台服务器设置页面
- 在管理面板中添加服务器设置页面
- 快速配置，无需直接编辑配置文件
- 分类设置：
  - DDoS 防护设置
  - 限流保护设置
  - 文件上传设置
  - 文件预览与下载设置
  - 网络设置（HTTP/HTTPS）
  - 系统设置（通知、自动重启）
- 实时限流状态显示
- 服务暂停时可强制恢复

### 3. SHA256 哈希显示
- 文件和目录现在显示 SHA256 哈希值
- 哈希值以淡化样式显示在项目名称旁边
- 点击可复制哈希值到剪贴板
- 帮助验证文件完整性

### 4. 访问计数优化
- 访问计数不再实时写入
- 计数在内存中累积
- 仅在服务器退出时写入磁盘
- 提高性能并减少磁盘 I/O

### 5. 自动重启配置
- 添加自动重启配置选项
- `AutoRestart` - 启用/禁用崩溃后自动重启
- `MaxRestartAttempts` - 最大重启尝试次数
- `RestartCount` - 当前重启计数

## Bug修复

### 1. FileSystemWatcher 已释放对象错误
- 修复"无法访问已释放的对象"错误
- 错误发生在应用程序关闭期间
- 添加空值检查和已释放状态验证
- 在 FileSystemWatcher 操作周围添加 try-catch 块
- 确保无异常地优雅关闭

### 2. 管理面板缺少配置选项
- 在服务器设置中添加缺失的配置选项：
  - `AllowOverwrite` - 允许覆盖文件
  - `AllowRootUpload` - 允许上传到根目录
  - `PreviewEnabled` - 启用文件预览
  - `MaxDownloadSizeMB` - 最大下载大小
  - `HttpEnabled` - 启用 HTTP
  - `HttpsEnabled` - 启用 HTTPS
  - `HttpsRedirectEnabled` - 启用 HTTPS 重定向
  - `Notification` - 启用 Windows 通知
  - `MaxRestartAttempts` - 最大重启次数

## 国际化 (i18n)

### 管理面板完整 i18n 支持
- 将所有硬编码的中文字符串移动到 `lang.yml`
- 添加全面的管理面板专用翻译
- 提供英文和中文翻译
- 所有面向用户的字符串现在使用 `I18nService.Instance.T()`

### 新增 i18n 键
- 管理操作消息
- 字段名称翻译
- 错误消息
- 成功消息
- 日志消息

## 配置更新

### Config.json 新增字段
```json
{
  "RateLimitProtection": false,
  "RateLimitRequestsPerSecond": 50,
  "RateLimitPauseMinutes": 5,
  "AutoRestart": false,
  "MaxRestartAttempts": 3,
  "RestartCount": 0
}
```

## 文件变更

### 新增文件
- `Services/RateLimitProtectionService.cs` - 限流保护服务
- `Middleware/RateLimitProtectionMiddleware.cs` - 限流保护中间件

### 修改文件
- `Models/Config.cs` - 添加限流保护和自动重启属性
- `Services/ConfigManager.cs` - 修复 FileSystemWatcher 释放问题
- `Services/Logger.cs` - 修改访问计数在退出时写入
- `Controllers/AdminController.cs` - 添加服务器设置 API、i18n 支持
- `Controllers/DirectoryController.cs` - 添加 SHA256 哈希计算
- `Program.cs` - 添加限流保护服务和中间件
- `lang.yml` - 添加全面的管理面板 i18n 翻译
- `DirectoryListing.Html` - 添加 SHA256 哈希显示

## 技术细节

### 限流保护实现
```csharp
public class RateLimitProtectionService
{
    private int _requestCount = 0;
    private bool _isPaused = false;
    private DateTime _pauseEndTime;
    
    public void IncrementRequestCount()
    {
        Interlocked.Increment(ref _requestCount);
    }
    
    public void CheckAndPauseIfNeeded()
    {
        // 检查是否超过阈值
        // 如需要则暂停服务
        // 在配置的时间后恢复
    }
}
```

### FileSystemWatcher 修复
```csharp
public void SaveConfig(Config config)
{
    if (_disposed)
        return;

    if (_fileWatcher != null && !_disposed)
    {
        try
        {
            _fileWatcher.EnableRaisingEvents = false;
        }
        catch (ObjectDisposedException)
        {
            // 已释放，忽略
        }
    }
    // ... 保存配置 ...
}
```

### SHA256 哈希显示
```javascript
// 前端 JavaScript
function copyHash(hash) {
    navigator.clipboard.writeText(hash);
    // 显示复制成功反馈
}
```

## 升级说明

1. 新的限流保护功能默认禁用
2. 在 Config.json 或通过管理面板启用
3. 根据需要配置阈值和暂停时长
4. 服务器设置页面可从管理面板访问
5. 所有管理面板字符串现在支持 i18n

## 安全说明

- 限流保护有助于防止高频攻击
- 管理面板设置需要认证
- 所有配置更改都会记录日志
- 自动重启有助于保持服务可用性
