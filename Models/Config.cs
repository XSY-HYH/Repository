namespace Repository.Models
{
    public class Config
    {
        public string IP { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 8000;
        public string RepositoryPath { get; set; } = "./Repository";
        
        // IP封锁配置
        public bool IPBlocking { get; set; } = false;
        public string IPBlockingList { get; set; } = "";
        public string Blacklist { get; set; } = "";
        
        // 系统配置
        public bool GenerateHelp { get; set; } = true;
        public bool PintoTop { get; set; } = true;
        public bool Background { get; set; } = false;
        
        // 请求限速配置
        public bool RequestThrottling { get; set; } = true;
        public int MaximumRequestsPerSecond { get; set; } = 50;
        public int AlarmRequestsPerSecond { get; set; } = 30;
        public string BlockedIPs { get; set; } = "";
        public string TemporarilyBlockedIPs { get; set; } = "";
        public int BlockDurationMinutes { get; set; } = 30;
        
        // 请求头过滤配置
        public bool RequestHeaderFiltering { get; set; } = false;
        public bool RequireUserAgent { get; set; } = true;
        public bool RequireAccept { get; set; } = true;
        public string AllowedBrowsers { get; set; } = "Chrome,Firefox,Safari,Edg,Opera,OPR,MSIE,Trident";
        
        // 文件上传配置
        public bool UploadEnabled { get; set; } = false;
        public int MaxUploadSizeMB { get; set; } = 50;
        public string AllowedUploadExtensions { get; set; } = ".txt,.md,.json,.xml,.html,.css,.js,.cs,.py,.java,.c,.cpp,.h,.hpp,.sh,.bat,.ini,.log,.csv,.tsv,.jpg,.jpeg,.png,.gif,.bmp,.webp,.svg,.ico,.tiff,.tif,.mp3,.wav,.ogg,.flac,.aac,.m4a,.wma,.mp4,.avi,.mov,.wmv,.flv,.webm,.mkv,.m4v,.3gp,.ogv,.zip,.rar,.7z,.tar,.gz,.pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx";
        public bool AllowOverwrite { get; set; } = false;
        public bool AllowRootUpload { get; set; } = false;
        public int MaxDiskUsagePercent { get; set; } = 0;
        
        // 文件下载配置
        public int MaxDownloadSizeMB { get; set; } = 100;
        
        // 文件预览配置
        public bool PreviewEnabled { get; set; } = true;
        public string PreviewExtensions { get; set; } = ".txt,.md,.json,.xml,.html,.css,.js,.cs,.py,.java,.c,.cpp,.h,.hpp,.sh,.bat,.ini,.log,.csv,.tsv";
        public string ImagePreviewExtensions { get; set; } = ".jpg,.jpeg,.png,.gif,.bmp,.webp,.svg,.ico,.tiff,.tif";
        public string AudioPreviewExtensions { get; set; } = ".mp3,.wav,.ogg,.flac,.aac,.m4a,.wma";
        public string VideoPreviewExtensions { get; set; } = ".mp4,.avi,.mov,.wmv,.flv,.webm,.mkv,.m4v,.3gp,.ogv";
        
        // 文件访问控制配置
        public string ForbiddenDownloadPaths { get; set; } = "";
        public string ForbiddenPreviewPaths { get; set; } = "";
        public string HiddenPaths { get; set; } = "";
        public bool ProtectEnabled { get; set; } = true;
        public string ProtectPaths { get; set; } = "";
        
        // HTTPS配置
        public bool HttpsEnabled { get; set; } = false;
        public int HttpsPort { get; set; } = 443;
        public string HttpsCertificatePath { get; set; } = "";
        public string HttpsCertificatePassword { get; set; } = "";
        public bool HttpsRedirectEnabled { get; set; } = false;
        public string Domain { get; set; } = "";

        // HTTP配置
        public bool HttpEnabled { get; set; } = true;
        
        // PROXY Protocol配置
        public bool ProxyProtocolEnabled { get; set; } = false;
        
        // 通知配置（仅Windows有效）
        public bool Notification { get; set; } = false;
        
        // 管理员配置
        public bool AdminEnabled { get; set; } = false;
        public string AdminUsername { get; set; } = "admin";
        public string AdminPassword { get; set; } = "";
        
        // 语言配置
        public string Language { get; set; } = "en";
        
        // 访问计数
        public int AccessCount { get; set; } = 0;
        
        // 自动重启配置
        public bool AutoRestart { get; set; } = false;
        public int MaxRestartAttempts { get; set; } = 3;
        public int RestartCount { get; set; } = 0;
    }
}