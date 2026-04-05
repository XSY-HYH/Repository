# 更新日志 - 2026年3月12日

## 新增功能

### 1. Windows通知系统
- 新增 `Notification` 配置项
- 支持Windows原生Toast通知
- 访问事件、警告、错误实时提醒
- 非Windows系统自动禁用

### 2. Secure认证客户端示例
- 新增 `secure_client.py` 参数驱动客户端
- 支持命令行参数配置服务器URL、客户端ID、令牌等
- 完整实现Secure认证流程

### 3. 项目文档
- 新增 `README.md` 项目描述文档
- 包含核心功能、项目结构、API接口、配置说明等

## 安全改进

### 1. 受保护目录隐藏
- 受保护目录在目录列表中不再显示
- 直接访问受保护目录返回404，不暴露目录存在信息
- 防止攻击者通过页面行为探测受保护目录

### 2. 系统目录自动屏蔽
- `.keys` 目录自动隐藏，防止密钥泄露
- 无需手动配置黑名单

### 3. 路径规范化修复
- 修复路径比较时格式不一致问题
- 确保 `IsPathProtected` 正确识别受保护路径

## Bug修复

### 1. 时间戳处理
- 修复Secure认证中时间戳字节序问题
- 服务端和客户端统一使用大端序

### 2. Secure认证流程
- 修复客户端注册后Secure Handler未创建问题
- 添加 `RefreshSecureHandlerForClient` 方法

## 配置更新

### Config.json 新增字段
```json
{
  "Notification": false
}
```

## 文件变更

### 新增文件
- `Services/NotificationService.cs` - Windows通知服务
- `secure_client.py` - Secure认证客户端示例
- `README.md` - 项目文档

### 修改文件
- `Models/Config.cs` - 添加Notification配置
- `Program.cs` - 集成通知服务
- `Controllers/RepositoryController.cs` - 添加受保护目录检查
- `Controllers/DirectoryController.cs` - 添加系统目录过滤
- `Services/ProtectionService.cs` - 修复路径规范化
- `Config.json` - 添加Notification配置项
- `help.txt` - 更新配置说明

## 升级说明

1. 更新程序后首次运行会自动生成新的配置项
2. 如需启用Windows通知，设置 `Notification: true`
3. 现有的 `Protectionlock.json` 配置无需修改
4. 受保护目录现在会自动隐藏，无需额外配置
