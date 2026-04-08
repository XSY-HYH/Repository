using System.Text;

namespace Repository.Services
{
    public class Logger : IDisposable
    {
        private readonly string _logDir = "logs";
        private readonly string _logFile;
        private readonly object _lockObject = new object();
        private readonly Queue<string> _logQueue = new Queue<string>();
        private readonly Timer _flushTimer;
        private bool _isFlushing = false;
        private int _sessionAccessCount = 0;
        private NotificationService? _notificationService;
        private ConfigManager? _configManager;

        public Logger()
        {
            if (!Directory.Exists(_logDir))
            {
                Directory.CreateDirectory(_logDir);
            }

            var now = DateTime.Now;
            var timestamp = now.ToString("yyyyMMdd_HHmmss");
            _logFile = Path.Combine(_logDir, $"repo_{timestamp}.log");

            Console.OutputEncoding = Encoding.UTF8;

            var header = $"{I18nService.Instance.T("log.header")}\n" +
                        $"{I18nService.Instance.T("log.startup_time", now.ToString("yyyy-MM-dd HH:mm:ss"))}\n" +
                        $"{I18nService.Instance.T("log.log_file", _logFile)}\n" +
                        $"========================\n\n";
            
            File.WriteAllText(_logFile, header, Encoding.UTF8);
            
            _flushTimer = new Timer(FlushLogs, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }
        
        public void SetNotificationService(NotificationService notificationService)
        {
            _notificationService = notificationService;
        }
        
        public void SetConfigManager(ConfigManager configManager)
        {
            _configManager = configManager;
        }

        public void LogInfo(string message)
        {
            var time = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{time}]{message}";
            Console.WriteLine(logEntry);
            
            lock (_lockObject)
            {
                _logQueue.Enqueue(logEntry);
            }
        }
        
        public void LogAccess(string ipAddress, string userAgent, string path, string method = "GET")
        {
            var time = DateTime.Now.ToString("HH:mm:ss");
            var deviceInfo = ParseUserAgent(userAgent);
            
            bool isApiAccess = path.StartsWith("/api") || 
                               path.StartsWith("/swagger") ||
                               path.StartsWith("/download/") ||
                               path.StartsWith("/admin/ws") ||
                               path.EndsWith("/ws");
            
            if (!isApiAccess)
            {
                _sessionAccessCount++;
                
                int accessCount = IncrementAccessCount();
                
                var logEntry = I18nService.Instance.T("access.count", 
                    accessCount, 
                    _sessionAccessCount, 
                    ipAddress, 
                    deviceInfo.deviceType, 
                    deviceInfo.browser, 
                    method, 
                    path);
                
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine(logEntry);
                Console.ResetColor();
                
                lock (_lockObject)
                {
                    _logQueue.Enqueue(logEntry);
                }
                
                _notificationService?.SendInfo(
                    I18nService.Instance.T("access.new_visit"), 
                    I18nService.Instance.T("access.visit_count", accessCount));
            }
            else
            {
                var logEntry = $"[{time}]{I18nService.Instance.T("access.api", ipAddress, deviceInfo.deviceType, deviceInfo.browser, method, path)}";
                Console.WriteLine(logEntry);
                
                lock (_lockObject)
                {
                    _logQueue.Enqueue(logEntry);
                }
            }
        }
        
        private int IncrementAccessCount()
        {
            if (_configManager == null)
                return _sessionAccessCount;
            
            var config = _configManager.GetConfig();
            config.AccessCount++;
            _configManager.SaveConfig(config);
            return config.AccessCount;
        }
        
        private (string deviceType, string browser) ParseUserAgent(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent))
                return (I18nService.Instance.T("device.unknown"), I18nService.Instance.T("browser.unknown"));
                
            var deviceType = I18nService.Instance.T("device.desktop");
            var browser = I18nService.Instance.T("browser.unknown");
            
            if (userAgent.Contains("Mobile") || userAgent.Contains("Android") || userAgent.Contains("iPhone") || userAgent.Contains("iPad"))
            {
                deviceType = I18nService.Instance.T("device.mobile");
            }
            else if (userAgent.Contains("Tablet") || userAgent.Contains("Kindle") || userAgent.Contains("PlayBook"))
            {
                deviceType = I18nService.Instance.T("device.tablet");
            }
            else if (userAgent.Contains("Bot") || userAgent.Contains("Spider") || userAgent.Contains("Crawler"))
            {
                deviceType = I18nService.Instance.T("device.bot");
            }
            
            if (userAgent.Contains("Chrome") && !userAgent.Contains("Edg"))
            {
                browser = I18nService.Instance.T("browser.chrome");
            }
            else if (userAgent.Contains("Firefox"))
            {
                browser = I18nService.Instance.T("browser.firefox");
            }
            else if (userAgent.Contains("Safari") && !userAgent.Contains("Chrome"))
            {
                browser = I18nService.Instance.T("browser.safari");
            }
            else if (userAgent.Contains("Edg"))
            {
                browser = I18nService.Instance.T("browser.edge");
            }
            else if (userAgent.Contains("Opera") || userAgent.Contains("OPR"))
            {
                browser = I18nService.Instance.T("browser.opera");
            }
            else if (userAgent.Contains("MSIE") || userAgent.Contains("Trident"))
            {
                browser = I18nService.Instance.T("browser.ie");
            }
            
            return (deviceType, browser);
        }
        
        public void LogSeparator()
        {
            var separator = "——————————————————————————————";
            Console.WriteLine(separator);
            
            lock (_lockObject)
            {
                _logQueue.Enqueue(separator);
            }
        }

        public void LogWarning(string message)
        {
            var time = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{time}]{message}";
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(logEntry);
            Console.ResetColor();
            
            lock (_lockObject)
            {
                _logQueue.Enqueue(logEntry);
            }
            
            if (!message.Contains("blacklisted") && !message.Contains("黑名单"))
            {
                _notificationService?.SendWarning(I18nService.Instance.T("warning.prefix"), message);
            }
        }

        public void LogError(string message)
        {
            var time = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{time}]{message}";
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(logEntry);
            Console.ResetColor();
            
            lock (_lockObject)
            {
                _logQueue.Enqueue(logEntry);
            }
            
            if (!message.Contains("blacklisted") && !message.Contains("黑名单"))
            {
                _notificationService?.SendError(I18nService.Instance.T("error.prefix"), message);
            }
        }

        public void LogError(Exception ex, string? message = null)
        {
            var time = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{time}]{(message != null ? message + "\n" : "")}     {I18nService.Instance.T("error.exception")}: {ex.Message}\n     {I18nService.Instance.T("error.stack_trace")}: {ex.StackTrace}";
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(logEntry);
            Console.ResetColor();
            
            lock (_lockObject)
            {
                _logQueue.Enqueue(logEntry);
            }
        }

        public string GetLogFilePath()
        {
            return _logFile;
        }
        
        private void FlushLogs(object? state)
        {
            if (_isFlushing || _logQueue.Count == 0)
                return;
                
            _isFlushing = true;
            
            try
            {
                lock (_lockObject)
                {
                    if (_logQueue.Count > 0)
                    {
                        var logsToWrite = new List<string>();
                        while (_logQueue.Count > 0)
                        {
                            logsToWrite.Add(_logQueue.Dequeue());
                        }
                        
                        File.AppendAllLines(_logFile, logsToWrite, Encoding.UTF8);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(I18nService.Instance.T("log.flush_failed", ex.Message));
            }
            finally
            {
                _isFlushing = false;
            }
        }
        
        public void Dispose()
        {
            _flushTimer?.Dispose();
            FlushLogs(null);
        }
        
        private void WriteLog(string level, string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var logEntry = $"[{timestamp}] [{level}] {message}";
            
            try
            {
                lock (_lockObject)
                {
                    _logQueue.Enqueue(logEntry);
                }
                Console.WriteLine(logEntry);
            }
            catch (Exception ex)
            {
                Console.WriteLine(I18nService.Instance.T("log.write_failed", ex.Message));
            }
        }
    }
}
