# 更新日志 - 2026年3月13日

## 更新1：自动SSL证书生成

### 新增功能
- 启用HTTPS但未配置证书路径时，程序自动生成自签名证书
- 新增 `Domain` 配置项，用于指定证书域名
  - 设置Domain：证书签发给指定域名
  - 未设置Domain：证书签发给 127.0.0.1
- 证书有效期：5年
- 包含SAN扩展，支持通配符域名

### 配置更新
```json
{
  "Domain": ""
}
```

### 文件变更
- `Services/CertificateGenerator.cs` - SSL证书生成服务
- `Models/Config.cs` - 添加Domain配置
- `Program.cs` - 集成证书生成逻辑
- `Config.json` - 添加Domain配置项
- `help.txt` - 更新配置说明

---

## 更新2：证书格式改为CRT

### 变更说明
- 证书格式从 PFX 改为 CRT/KEY (PEM格式)
- 证书文件命名格式：`webdata_{domain}.crt`
- 私钥文件命名格式：`webdata_{domain}.key`

### 文件格式
- **证书文件 (.crt)**：PEM格式的X.509证书
  ```
  -----BEGIN CERTIFICATE-----
  ...
  -----END CERTIFICATE-----
  ```

- **私钥文件 (.key)**：PEM格式的RSA私钥
  ```
  -----BEGIN RSA PRIVATE KEY-----
  ...
  -----END RSA PRIVATE KEY-----
  ```

### 优势
- CRT/KEY格式更通用，兼容Nginx、Apache等主流服务器
- PEM格式为文本文件，便于查看和传输
- 无需密码保护，简化配置

### 升级说明
1. 删除旧的 `.pfx` 证书文件
2. 程序会自动生成新的 `.crt` 和 `.key` 文件
3. 配置文件中的 `HttpsCertificatePath` 将自动更新为 `.crt` 文件路径
4. `HttpsCertificatePassword` 配置项不再需要（PEM格式无需密码）

### 文件变更
- `Services/CertificateGenerator.cs` - 改为生成PEM格式证书
- `Program.cs` - 更新证书路径处理逻辑
