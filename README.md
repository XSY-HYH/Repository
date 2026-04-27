# Repository - File Server

> A project born out of sheer boredom.
>
> **简体中文**: [README-zh.md](./README-zh.md) | 一个闲得蛋疼诞生的项目，基于 ASP.NET Core 构建。

A project born out of sheer boredom, built on ASP.NET Core. You'll probably never use it in your lifetime.  
I just wanted to prove it could be done—but it's definitely not something you'd actually use.

## Core Features

### File Management
- **Directory Browsing**: Web interface with intuitive directory structure and breadcrumb navigation
- **File Preview**: Support for text, image, audio, video and other formats online preview
- **File Download**: Single file download with configurable size limits
- **File Upload**: File upload with configurable extensions and size limits

### Security Protection
- **IP Filtering**: Whitelist mode, only allow specified IPs to access
- **Blacklist Mechanism**: Path blacklist to block access to sensitive directories
- **Request Throttling**: Limit request frequency per IP to prevent malicious attacks
- **Path Security**: Prevent directory traversal attacks, ensure access scope is limited

### HTTPS Support
- Support HTTP and HTTPS running simultaneously
- Support HTTP to HTTPS automatic redirect
- Support PFX certificate configuration

### Admin Panel
- Web interface for repository file management
- CHAP protocol encrypted authentication（It is Chain Hash Authentication Protocol (CHAP)）
- WebSocket real-time communication
- Supported operations:
  - Directory browsing and navigation
  - Move files/directories to recycle bin
  - Move/rename files/directories
  - Create new directories
  - Upload files to current directory
- Security response header protection (XSS, clickjacking, etc.)

### Windows Notification
- Support Windows native Toast notification
- Real-time alerts for access events, warnings, errors
- Configurable via settings

## Project Structure

```
Repository/
├── Controllers/           # Controllers
│   ├── RepositoryController.cs    # Main page routing
│   ├── AdminController.cs         # Admin panel controller
│   ├── DirectoryController.cs     # Directory API
│   ├── FileController.cs          # File operation API
│   └── UploadController.cs        # Upload API
├── Services/              # Service Layer
│   ├── ConfigManager.cs           # Configuration management
│   ├── BlacklistService.cs        # Blacklist service
│   ├── RequestThrottlingService.cs   # Request throttling service
│   ├── ProxyProtocolService.cs    # PROXY Protocol parsing service
│   ├── ChapAuthService.cs         # CHAP authentication service
│   ├── AdminConnectionManager.cs  # Admin connection manager
│   ├── Logger.cs                  # Logging service
│   └── NotificationService.cs     # Notification service
├── Middleware/            # Middleware
│   ├── SecurityHeadersMiddleware.cs    # Security headers
│   ├── IPBlockingMiddleware.cs         # IP filtering
│   ├── RateLimitingMiddleware.cs       # Rate limiting
│   ├── ProxyProtocolConnectionHandler.cs # PROXY Protocol handler
│   └── SubdirectoryRoutingMiddleware.cs # Subdirectory routing
├── Models/                # Data Models
│   └── Config.cs                  # Configuration model
├── docs/                  # Documentation
│   ├── api/                       # API documentation
│   ├── security/                  # Security reports
│   └── updates/                   # Update logs
├── pytest/                # Python examples
├── Repository/            # Repository directory (file storage)
│   └── .keys/                     # Server key storage
├── Config.yml            # Main configuration file
└── help.txt               # Help documentation
```

## API Endpoints

### File Operations
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/files` | GET | Get directory listing |
| `/api/download/{path}` | GET | Download file |
| `/api/preview/{path}` | GET | Preview file |
| `/api/upload/{path}` | POST | Upload file |

### Admin Panel
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/admin` | GET | Admin panel page |
| `/admin/ws` | WebSocket | WebSocket communication |
| `/admin/api/upload` | POST | Upload file |

## Configuration

The main configuration file `Config.yml` supports the following options:

### Basic Configuration
| Option | Type | Default | Description |
|--------|------|---------|-------------|
| IP | string | "::" | Listen address |
| Port | int | 8000 | HTTP port |
| RepositoryPath | string | "./Repository" | Repository path |
| Notification | bool | false | Windows notification |

### Admin Configuration
| Option | Type | Default | Description |
|--------|------|---------|-------------|
| AdminEnabled | bool | false | Enable admin features |
| AdminUsername | string | "" | Admin username |
| AdminPassword | string | "" | Admin password |

### Security Configuration
| Option | Type | Default | Description |
|--------|------|---------|-------------|
| IPBlocking | bool | false | IP whitelist |
| IPBlockingList | string | "" | Whitelist entries |
| Blacklist | string | "" | Path blacklist |
| RequestThrottling | bool | true | Request throttling |
| MaxRequestsPerMinute | int | 100 | Max requests per minute |
| BlockDurationMinutes | int | 30 | Block duration |
| RateLimitProtection | bool | false | Rate limit protection |
| RateLimitRequestsPerSecond | int | 50 | Max requests per second |
| RateLimitPauseMinutes | int | 5 | Pause duration when rate limit exceeded |
| ProxyProtocolEnabled | bool | false | Enable PROXY Protocol support |

#### Path Blacklist Format
Use `|` (pipe) to separate multiple entries, supporting the following formats:

| Format | Example | Description |
|--------|---------|-------------|
| Exact path | `secret/data.txt` | Match exact path |
| Wildcard | `*.log` | Match all .log files |
| Directory wildcard | `temp/*` | Match all contents under temp |
| Subdirectory match | `%/node_modules` | Match node_modules in any subdirectory |
| File reference | `/path/to/blacklist.txt` | Read blacklist from file |

Example:
```
$RECYCLE.BIN|System Volume Information|*.log|%/node_modules|temp/*
```

### Upload Configuration
| Option | Type | Default | Description |
|--------|------|---------|-------------|
| UploadEnabled | bool | false | Enable upload |
| MaxUploadSizeMB | int | 50 | Max upload size |
| AllowedUploadExtensions | string | ... | Allowed extensions |
| AllowOverwrite | bool | false | Allow overwrite |
| AllowRootUpload | bool | false | Allow upload to root |
| MaxDiskUsagePercent | int | 0 | Disk usage limit (percentage) |

### Preview Configuration
| Option | Type | Default | Description |
|--------|------|---------|-------------|
| PreviewEnabled | bool | true | Enable preview |
| PreviewExtensions | string | ... | Text preview extensions |
| ImagePreviewExtensions | string | ... | Image preview extensions |
| AudioPreviewExtensions | string | ... | Audio preview extensions |
| VideoPreviewExtensions | string | ... | Video preview extensions |

### Access Control
| Option | Type | Default | Description |
|--------|------|---------|-------------|
| ForbiddenDownloadPaths | string | "" | Forbidden download paths |
| ForbiddenPreviewPaths | string | "" | Forbidden preview paths |
| HiddenPaths | string | "" | Hidden paths |
| ProtectEnabled | bool | true | Enable protection |
| ProtectPaths | string | "" | Protected paths |

### HTTPS Configuration
| Option | Type | Default | Description |
|--------|------|---------|-------------|
| HttpsEnabled | bool | false | Enable HTTPS |
| HttpsPort | int | 443 | HTTPS port |
| HttpsCertificatePath | string | "" | Certificate path (CRT/KEY format) |
| HttpsCertificatePassword | string | "" | Certificate password |
| HttpsRedirectEnabled | bool | false | HTTP redirect |
| HttpEnabled | bool | true | Enable HTTP |
| Domain | string | "" | Server domain (auto certificate signing) |

### Auto-Restart Configuration
| Option | Type | Default | Description |
|--------|------|---------|-------------|
| AutoRestart | bool | false | Enable auto-restart on crash |
| MaxRestartAttempts | int | 3 | Maximum restart attempts |

## Security Features

### Path Security
- Prevent directory traversal attacks (`../` etc.)
- Path normalization processing
- Access scope limited to repository directory

## Logging System

- Automatic logging of all access requests
- Support for warning and error logs
- Log files automatically split by date
- Stored in `logs/` directory

## Cross-Platform Support

- Support Windows, Linux, macOS
- Windows-specific features (window always on top, background running, notifications) are automatically disabled on other platforms
- Configuration file hot reload, no service restart required

## Notes

1. Some configurations require service restart after modification
2. Ensure read/write permissions for repository directory
3. HTTPS supports auto-generated self-signed certificates (CRT/KEY format)
4. Upload feature is disabled by default, pay attention to security when enabled
