# 更新日志 - 2026年4月26日

## 新增功能

### 1. PROXY Protocol 支持
- 新增 PROXY Protocol 解析服务 (`ProxyProtocolService`)
- 支持 PROXY Protocol v1 和 v2 格式
- 通过 frp 等反向代理获取真实客户端 IP
- 配置选项：
  - `ProxyProtocolEnabled` - 启用/禁用 PROXY Protocol 支持
- 与请求限速、熔断、请求头过滤等服务集成
- 调试模式下显示原始数据包和 PROXY 头解析信息

### 2. 调试模式
- 新增 `--debug` 启动参数
- 启用后显示 DEBUG 级别日志
- 日志输出包含：
  - 接收到的原始数据包（十六进制格式）
  - PROXY Protocol 头解析详情
  - 连接 ID 用于请求追踪
- 日志颜色：青色 (Cyan)

### 3. YAML 配置文件
- 配置格式从 JSON 迁移到 YAML
- 支持动态注释替换（根据 Language 设置）
- 默认英文注释，中文语言下自动替换为中文注释
- 配置文件：`Config.yml`
- 向后兼容：自动从旧配置迁移

## 功能优化

### 1. 配置管理器重构
- `ConfigManager` 支持 YAML 序列化/反序列化
- 移除重复的 ConfigManager 实例
- 修复配置变更节流逻辑
- 修复文件监视器重复触发问题

### 2. 日志系统优化
- 新增 `LogDebug` 方法
- 支持调试模式开关
- 修复重复日志显示问题
- 统一日志输出格式

### 3. PROXY Protocol 中间件
- 在 Kestrel 传输层处理 PROXY 头
- 正确剥离 PROXY 头后传递给 TLS/HTTP 处理器
- 修复 HTTPS 端口上的 TLS 握手问题
- 修复中间件注册顺序（PROXY处理在TLS解密之前）

## Bug 修复

### 1. PROXY Protocol 与 HTTPS 兼容性问题
- 修复中间件注册顺序导致的 TLS 握手失败
- 修复数据流处理逻辑
- 确保 PROXY 头正确剥离后传递给后续处理器

### 2. 配置注释替换问题
- 修复 YAML 反序列化时的命名约定不匹配
- 修复注释替换后配置值丢失问题
- 修复中英文注释切换时的格式问题

### 3. 端口绑定冲突
- 修复 PROXY Protocol 启用时的端口重复绑定
- 修复 Kestrel 配置与 URL 配置的冲突

## 配置变更

### 移除的配置项
- `UseXForwardedFor` - 已移除，由 PROXY Protocol 替代

### 新增的配置项
- `ProxyProtocolEnabled` - 启用 PROXY Protocol 支持（默认: false）

## 文档更新

- 更新 `help.txt` 添加 PROXY Protocol 配置说明
- 更新 `lang.yml` 添加相关翻译
- 创建 PROXY Protocol 安全测试报告

## 已知问题

- PROXY Protocol v2 在特定网络环境下可能存在数据包不完整的情况