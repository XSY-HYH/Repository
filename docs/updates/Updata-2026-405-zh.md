# 更新日志 - 2026年4月5日

## 新增功能

### 1. 磁盘占用限制
- 新增 `MaxDiskUsagePercent` 配置项
- 限制仓库所在磁盘的占用百分比
- 上传前检查预计占用率，超限则拒绝上传
- 返回 HTTP 507 状态码提示磁盘空间不足

### 2. 保护功能开关
- 新增 `ProtectEnabled` 配置项
- 可禁用保护功能以加快启动速度
- 禁用后受保护目录无需验证即可访问
- "不可视"功能不受影响

### 3. 根目录上传控制
- 新增 `AllowRootUpload` 配置项
- 默认禁止上传到仓库根目录
- 可根据需要开启

## 安全改进

### 1. 安全测试扩展
- 扩展 `security_test.py` 测试脚本
- 新增13类高级攻击向量测试
- 共计200个测试用例，全部通过
- 生成 STR202631202 安全测试报告

### 2. 路径黑名单格式说明
- 完善黑名单配置文档
- 支持精确路径、通配符、子目录匹配等格式
- 修正 help.txt 中错误的黑名单说明

## Bug修复

### 1. 文本预览换行问题
- 修复预览文本文件时换行符丢失问题
- 添加 `white-space: pre-wrap` CSS样式
- 使用等宽字体显示代码文件

### 2. 配置文档完善
- README.md 补充缺失的配置项说明
- 修正证书格式说明（PFX → CRT/KEY）
- 更新 help.txt 配置项编号

## 配置更新

### Config.json 新增字段
```json
{
  "ProtectEnabled": true,
  "AllowRootUpload": false,
  "MaxDiskUsagePercent": 0
}
```

## 文件变更

### 新增文件
- `STR202631202.md` - 扩展安全测试报告

### 修改文件
- `Models/Config.cs` - 添加新配置项
- `Controllers/UploadController.cs` - 添加磁盘占用检查
- `Services/ProtectionService.cs` - 添加保护功能开关
- `DirectoryListing.Css` - 修复文本预览样式
- `help.txt` - 更新配置说明
- `README.md` - 完善配置文档
- `Config.json` - 添加新配置项

## 安全测试结果

| 项目 | 结果 |
|------|------|
| 测试类别 | 27类 |
| 总测试数 | 200 |
| 通过 | 200 |
| 失败 | 0 |
| 发现漏洞 | 0 |
| 通过率 | 100% |

### 测试覆盖
- HTTP方法篡改
- Unicode编码绕过
- 双重扩展名攻击
- 空字节注入
- CRLF注入
- 参数污染攻击
- 备份文件访问
- 敏感文件访问
- 超长路径攻击
- 恶意文件名
- Content-Type绕过
- Zip Slip攻击
- Token篡改攻击

## 升级说明

1. 更新程序后首次运行会自动生成新的配置项
2. `MaxDiskUsagePercent` 默认为0（不限制），建议设置为80-90
3. `ProtectEnabled` 默认为true，如需加快启动可设为false
4. 现有配置无需修改，新功能默认关闭或保持兼容
