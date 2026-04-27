using Microsoft.Extensions.FileProviders;
using Repository.Services;
using Repository.Middleware;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Repository
{
    public static class WindowHelper
    {
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetConsoleWindow();
        
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;
        
        public static void SetConsoleTopMost()
        {
            var consoleHandle = GetConsoleWindow();
            if (consoleHandle != IntPtr.Zero)
            {
                SetWindowPos(consoleHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE);
            }
        }
        
        public static void HideConsole()
        {
            var consoleHandle = GetConsoleWindow();
            if (consoleHandle != IntPtr.Zero)
            {
                ShowWindow(consoleHandle, SW_HIDE);
            }
        }
        
        public static void ShowConsole()
        {
            var consoleHandle = GetConsoleWindow();
            if (consoleHandle != IntPtr.Zero)
            {
                ShowWindow(consoleHandle, SW_SHOW);
            }
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            bool isDebug = args.Contains("--debug");
            
            if (ShouldAutoRestart())
            {
                AutoRestart(args);
                return;
            }
            
            var language = ReadLanguageConfig();
            I18nService.Initialize(language);
            
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllersWithViews();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            builder.Services.AddSingleton<Logger>();
            builder.Services.AddSingleton<ConfigManager>();
            builder.Services.AddSingleton<ProxyProtocolService>();
            builder.Services.AddSingleton<ClientIPService>();
            builder.Services.AddSingleton<IPBlockingService>();
            builder.Services.AddSingleton<RequestThrottlingService>();
            builder.Services.AddSingleton<RequestHeaderFilteringService>();
            builder.Services.AddSingleton<BlacklistService>();
            builder.Services.AddSingleton<FileWatcherService>();
            builder.Services.AddSingleton<TemporaryLinkService>();
            builder.Services.AddSingleton<VideoStreamingService>();
            builder.Services.AddSingleton<NotificationService>();
            builder.Services.AddSingleton<ChapAuthService>();
            builder.Services.AddSingleton<AdminConnectionManager>();

            var tempConfigManager = new ConfigManager(new Logger());
            var tempConfig = tempConfigManager.GetConfig();
            
            if (tempConfig.HttpsEnabled && string.IsNullOrEmpty(tempConfig.HttpsCertificatePath))
            {
                var tempLogger = new Logger();
                var certGenerator = new CertificateGenerator(tempLogger);
                var (certPath, keyPath) = certGenerator.GenerateSelfSignedCertificate(
                    tempConfig.Domain, 
                    Directory.GetCurrentDirectory());
                
                if (!string.IsNullOrEmpty(certPath) && !string.IsNullOrEmpty(keyPath))
                {
                    tempConfig.HttpsCertificatePath = certPath.Replace('\\', '/');
                    tempConfig.HttpsCertificatePassword = "";
                    tempConfigManager.SaveConfig(tempConfig);
                }
            }
            
            builder.Services.AddHttpsRedirection(options =>
            {
                options.HttpsPort = tempConfig.HttpsPort;
            });

            var proxyProtocolLogger = new Logger();
            proxyProtocolLogger.SetDebugMode(isDebug);
            var proxyProtocolService = new ProxyProtocolService(proxyProtocolLogger);
            builder.Services.AddSingleton(proxyProtocolService);

            if (tempConfig.ProxyProtocolEnabled)
            {
                builder.WebHost.ConfigureKestrel(options =>
                {
                    bool shouldEnableHttp = tempConfig.HttpEnabled;
                    if (!tempConfig.HttpsEnabled)
                    {
                        shouldEnableHttp = true;
                    }
                    
                    if (shouldEnableHttp)
                    {
                        options.ListenAnyIP(tempConfig.Port, listenOptions =>
                        {
                            listenOptions.Use(next =>
                            {
                                return async context =>
                                {
                                    var input = context.Transport.Input;
                                    var result = await input.ReadAsync();
                                    var buffer = result.Buffer;

                                    if (!buffer.IsEmpty)
                                    {
                                        var data = buffer.First.ToArray();
                                        proxyProtocolLogger.LogDebug($"[{context.ConnectionId}] Received {data.Length} bytes: {BitConverter.ToString(data)}");
                                        
                                        var (sourceIP, bytesConsumed) = proxyProtocolService.ParseHeader(data);

                                        if (bytesConsumed > 0 && sourceIP != null)
                                        {
                                            proxyProtocolLogger.LogDebug($"[{context.ConnectionId}] PROXY Protocol detected: {sourceIP}, header size: {bytesConsumed} bytes");
                                            proxyProtocolService.ConnectionIPs[context.ConnectionId] = sourceIP;
                                            input.AdvanceTo(buffer.GetPosition(bytesConsumed), buffer.End);
                                        }
                                        else
                                        {
                                            input.AdvanceTo(buffer.Start);
                                        }
                                    }

                                    await next(context);
                                };
                            });
                        });
                    }
                    
                    if (tempConfig.HttpsEnabled)
                    {
                        var certPath = tempConfig.HttpsCertificatePath;
                        var finalKeyPath = Path.ChangeExtension(certPath, ".key");
                        
                        if (!string.IsNullOrEmpty(certPath) && !Path.IsPathRooted(certPath))
                        {
                            certPath = Path.GetFullPath(certPath);
                            finalKeyPath = Path.GetFullPath(finalKeyPath);
                        }
                        
                        options.ListenAnyIP(tempConfig.HttpsPort, listenOptions =>
                        {
                            listenOptions.Use(next =>
                            {
                                return async context =>
                                {
                                    var input = context.Transport.Input;
                                    var result = await input.ReadAsync();
                                    var buffer = result.Buffer;

                                    if (!buffer.IsEmpty)
                                    {
                                        var data = buffer.First.ToArray();
                                        proxyProtocolLogger.LogDebug($"[{context.ConnectionId}] Received {data.Length} bytes: {BitConverter.ToString(data)}");
                                        
                                        var (sourceIP, bytesConsumed) = proxyProtocolService.ParseHeader(data);

                                        if (bytesConsumed > 0 && sourceIP != null)
                                        {
                                            proxyProtocolLogger.LogDebug($"[{context.ConnectionId}] PROXY Protocol detected: {sourceIP}, header size: {bytesConsumed} bytes");
                                            proxyProtocolService.ConnectionIPs[context.ConnectionId] = sourceIP;
                                            input.AdvanceTo(buffer.GetPosition(bytesConsumed));
                                        }
                                        else
                                        {
                                            input.AdvanceTo(buffer.Start);
                                        }
                                    }

                                    await next(context);
                                };
                            });

                            if (!string.IsNullOrEmpty(certPath) && File.Exists(certPath) && File.Exists(finalKeyPath))
                            {
                                var certPem = File.ReadAllText(certPath);
                                var keyPem = File.ReadAllText(finalKeyPath);
                                var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(
                                    System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPem(certPem, keyPem).Export(
                                        System.Security.Cryptography.X509Certificates.X509ContentType.Pfx), null);
                                listenOptions.UseHttps(cert);
                            }
                        });
                    }
                });
            }

            var app = builder.Build();

            var configManager = app.Services.GetRequiredService<ConfigManager>();
            var logger = app.Services.GetRequiredService<Logger>();
            var notificationService = app.Services.GetRequiredService<NotificationService>();
            var ipBlockingService = app.Services.GetRequiredService<IPBlockingService>();
            
            logger.SetDebugMode(isDebug);
            logger.SetNotificationService(notificationService);
            logger.SetConfigManager(configManager);
            I18nService.Instance.SetLogger(logger);

            var config = configManager.GetConfig();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            if (config.HttpsEnabled && config.HttpsRedirectEnabled && config.HttpEnabled)
            {
                app.UseHttpsRedirection();
                logger.LogInfo(I18nService.Instance.T("https.redirect_enabled", config.HttpsPort));
            }

            app.UseSecurityHeaders();
            app.UseIPBlocking();
            app.UseRateLimiting();
            app.UseRequestHeaderFiltering();
            app.UseSubdirectoryRouting();

            app.Use(async (context, next) =>
            {
                await next();
                if (context.Response.StatusCode == 405)
                {
                    context.Response.StatusCode = 404;
                }
            });

            app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            });

            app.UseRouting();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Repository}/{action=Index}/{id?}");

            logger.LogInfo(I18nService.Instance.T("server.starting"));
            logger.LogInfo(I18nService.Instance.T("config.loaded", config.IP, config.Port, config.RepositoryPath));

            var portsToCheck = new List<int>();
            if (config.HttpEnabled || !config.HttpsEnabled)
            {
                portsToCheck.Add(config.Port);
            }
            if (config.HttpsEnabled)
            {
                portsToCheck.Add(config.HttpsPort);
            }

            var occupiedPorts = new List<int>();
            foreach (var port in portsToCheck)
            {
                if (IsPortInUse(port, config.IP))
                {
                    occupiedPorts.Add(port);
                }
            }

            if (occupiedPorts.Count > 0)
            {
                var portStr = string.Join(", ", occupiedPorts);
                logger.LogError(new Exception(I18nService.Instance.T("server.port_in_use", portStr)), I18nService.Instance.T("server.port_check_failed"));
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(I18nService.Instance.T("error.port_occupied", portStr));
                Console.ResetColor();
                Console.WriteLine(I18nService.Instance.T("server.exit_in_seconds", 5));
                Thread.Sleep(5000);
                return;
            }

            if (config.PintoTop)
            {
                if (OperatingSystem.IsWindows())
                {
                    WindowHelper.SetConsoleTopMost();
                    logger.LogInfo(I18nService.Instance.T("console.topmost_set"));
                }
                else
                {
                    logger.LogWarning(I18nService.Instance.T("console.topmost_not_supported"));
                }
            }

            if (config.Background)
            {
                if (OperatingSystem.IsWindows())
                {
                    WindowHelper.HideConsole();
                    logger.LogInfo(I18nService.Instance.T("console.hidden"));
                }
                else
                {
                    logger.LogWarning(I18nService.Instance.T("console.background_not_supported"));
                }
            }

            configManager.EnsureRepositoryDirectoryExists();
            var repoPath = Path.GetFullPath(config.RepositoryPath);
            logger.LogInfo(I18nService.Instance.T("repository.dir_confirmed", repoPath));

            var fileWatcher = app.Services.GetRequiredService<FileWatcherService>();
            fileWatcher.StartWatching(repoPath);
            logger.LogInfo(I18nService.Instance.T("watcher.started"));

            configManager.OnConfigChanged += (sender, newConfig) =>
            {
                try
                {
                    logger.LogInfo(I18nService.Instance.T("config.changed"));
                    
                    var newRepositoryPath = Path.GetFullPath(newConfig.RepositoryPath);
                    fileWatcher.UpdateRepositoryPath(newRepositoryPath);
                    
                    if (!Directory.Exists(newRepositoryPath))
                    {
                        Directory.CreateDirectory(newRepositoryPath);
                        logger.LogInfo(I18nService.Instance.T("repository.dir_created", newRepositoryPath));
                    }
                    
                    string addressType = newConfig.IP == "::" ? I18nService.Instance.T("listen.ipv4_ipv6") : 
                                        newConfig.IP == "0.0.0.0" ? I18nService.Instance.T("listen.ipv4_all") : 
                                        newConfig.IP;
                    logger.LogInfo(I18nService.Instance.T("config.services_updated", newConfig.IP, addressType, newConfig.Port, newConfig.RepositoryPath));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, I18nService.Instance.T("config.update_failed"));
                }
            };

            app.UseDynamicStaticFiles();

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(AppContext.BaseDirectory),
                RequestPath = string.Empty,
                OnPrepareResponse = ctx =>
                {
                    if (ctx.Context.Request.Path == "/favicon.ico")
                    {
                        ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=604800");
                    }
                }
            });
            
            try
            {
                string urls;
                
                if (args.Length > 0)
                {
                    urls = args[0];
                    logger.LogInfo(I18nService.Instance.T("listen.info", urls));
                    ConsoleANSI.ConsoleAnsiArtist.PrintAnsiText("R E P O");
                    app.Run(urls);
                }
                else
                {
                    string listenAddress = config.IP;
                    string formattedAddress = listenAddress == "::" ? $"[{listenAddress}]" : listenAddress;
                    
                    string addressInfo = listenAddress;
                    if (listenAddress == "::")
                    {
                        addressInfo = I18nService.Instance.T("listen.ipv4_ipv6");
                    }
                    else if (listenAddress == "0.0.0.0")
                    {
                        addressInfo = I18nService.Instance.T("listen.ipv4_all");
                    }
                    
                    bool shouldEnableHttp = config.HttpEnabled;
                    if (!config.HttpsEnabled)
                    {
                        shouldEnableHttp = true;
                    }
                    
                    if (!config.ProxyProtocolEnabled)
                    {
                        if (shouldEnableHttp)
                        {
                            string httpUrl = $"http://{formattedAddress}:{config.Port}";
                            app.Urls.Add(httpUrl);
                            logger.LogInfo(I18nService.Instance.T("http.enabled", httpUrl));
                        }
                        else
                        {
                            logger.LogInfo(I18nService.Instance.T("http.disabled"));
                        }
                        
                        if (config.HttpsEnabled)
                        {
                            string httpsUrl = $"https://{formattedAddress}:{config.HttpsPort}";
                            app.Urls.Add(httpsUrl);
                        
                            bool hasValidCertificate = false;
                        
                            if (!string.IsNullOrEmpty(config.HttpsCertificatePath) && 
                                System.IO.File.Exists(config.HttpsCertificatePath))
                            {
                                hasValidCertificate = true;
                                logger.LogInfo(I18nService.Instance.T("https.using_cert", config.HttpsCertificatePath));
                            }
                            else
                            {
                                logger.LogInfo(I18nService.Instance.T("https.generating_cert"));
                                
                                var certGenerator = new CertificateGenerator(logger);
                                var (certPath, keyPath) = certGenerator.GenerateSelfSignedCertificate(
                                    config.Domain, 
                                    Directory.GetCurrentDirectory());
                                
                                if (!string.IsNullOrEmpty(certPath) && !string.IsNullOrEmpty(keyPath))
                                {
                                    hasValidCertificate = true;
                                    logger.LogInfo(I18nService.Instance.T("https.cert_generated"));
                                    
                                    config.HttpsCertificatePath = certPath;
                                    config.HttpsCertificatePassword = "";
                                    configManager.SaveConfig(config);
                                }
                            }
                            
                            if (!hasValidCertificate)
                            {
                                logger.LogWarning(I18nService.Instance.T("https.cert_invalid"));
                            }
                            
                            logger.LogInfo(I18nService.Instance.T("https.enabled", httpsUrl));
                            
                            if (config.HttpsRedirectEnabled && shouldEnableHttp)
                            {
                                logger.LogInfo(I18nService.Instance.T("https.redirect_active"));
                            }
                            else if (!config.HttpsRedirectEnabled)
                            {
                                logger.LogInfo(I18nService.Instance.T("https.redirect_disabled"));
                            }
                            else if (!shouldEnableHttp)
                            {
                                logger.LogInfo(I18nService.Instance.T("https.redirect_not_active"));
                            }
                        }
                    }
                    else
                    {
                        if (config.HttpsEnabled)
                        {
                            logger.LogInfo(I18nService.Instance.T("https.enabled", $"https://{formattedAddress}:{config.HttpsPort}"));
                        }
                        else
                        {
                            logger.LogInfo(I18nService.Instance.T("https.disabled"));
                        }
                    }
                    
                    if (config.ProxyProtocolEnabled)
                    {
                        logger.LogInfo("PROXY Protocol: Enabled");
                    }
                    
                    StringBuilder listenInfo = new StringBuilder(I18nService.Instance.T("listen.info", $"({addressInfo})"));
                    if (shouldEnableHttp)
                    {
                        listenInfo.Append($", HTTP {config.Port}");
                    }
                    if (config.HttpsEnabled)
                    {
                        listenInfo.Append($", HTTPS {config.HttpsPort}");
                    }
                    logger.LogInfo(listenInfo.ToString());
                    
                    var adminConnectionManager = app.Services.GetRequiredService<AdminConnectionManager>();
                    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
                    
                    Console.CancelKeyPress += async (sender, e) =>
                    {
                        e.Cancel = true;
                        logger.LogInfo(I18nService.Instance.T("server.shutdown"));
                        
                        if (adminConnectionManager.ConnectionCount > 0)
                        {
                            logger.LogInfo(I18nService.Instance.T("admin.notifying", adminConnectionManager.ConnectionCount));
                            await adminConnectionManager.NotifyAllAsync(I18nService.Instance.T("admin.server_shutdown"));
                            await Task.Delay(500);
                            await adminConnectionManager.CloseAllAsync(I18nService.Instance.T("admin.server_closing"));
                        }
                        
                        lifetime.StopApplication();
                    };
                    
                    ConsoleANSI.ConsoleAnsiArtist.PrintAnsiText("R E P O");
                    
                    app.Run();
                }
                
                logger.LogInfo(I18nService.Instance.T("server.stopped"));
            }
            catch (Exception ex)
            {
                if (OperatingSystem.IsWindows())
                {
                    WindowHelper.ShowConsole();
                }
                logger.LogError(ex, I18nService.Instance.T("error.start_failed"));
                
                if (ShouldAutoRestartOnError())
                {
                    logger.LogInfo(I18nService.Instance.T("server.auto_restart_enabled"));
                    PrepareAutoRestart(args);
                }
                else
                {
                    logger.LogInfo(I18nService.Instance.T("server.auto_restart_disabled"));
                    throw;
                }
            }
        }

        private static bool ShouldAutoRestart()
        {
            try
            {
                if (!File.Exists("Config.json"))
                    return false;
                
                var jsonContent = File.ReadAllText("Config.json");
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent);
                
                if (data != null && data.ContainsKey("autoRestart") && 
                    data["autoRestart"].ValueKind == JsonValueKind.True)
                {
                    return true;
                }
            }
            catch (Exception)
            {
            }
            
            return false;
        }

        private static bool ShouldAutoRestartOnError()
        {
            try
            {
                if (!File.Exists("Config.json"))
                    return false;
                
                var jsonContent = File.ReadAllText("Config.json");
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent);
                
                if (data != null && data.ContainsKey("autoRestart") && 
                    data["autoRestart"].ValueKind == JsonValueKind.True)
                {
                    if (data.ContainsKey("maxRestartAttempts") && 
                        data.ContainsKey("restartCount"))
                    {
                        var maxAttempts = data["maxRestartAttempts"].GetInt32();
                        var currentCount = data["restartCount"].GetInt32();
                        
                        return currentCount < maxAttempts;
                    }
                    
                    return true;
                }
            }
            catch (Exception)
            {
            }
            
            return false;
        }

        private static void PrepareAutoRestart(string[] args)
        {
            try
            {
                UpdateRestartCount();
                
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExePath))
                {
                    currentExePath = Environment.ProcessPath;
                }
                
                if (!string.IsNullOrEmpty(currentExePath))
                {
                    Thread.Sleep(2000);
                    
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = currentExePath,
                        Arguments = string.Join(" ", args),
                        UseShellExecute = true,
                        CreateNoWindow = false
                    };
                    
                    Process.Start(startInfo);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Auto-restart failed: {ex.Message}");
            }
            
            Environment.Exit(0);
        }

        private static void AutoRestart(string[] args)
        {
            try
            {
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExePath))
                {
                    currentExePath = Environment.ProcessPath;
                }
                
                if (!string.IsNullOrEmpty(currentExePath))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = currentExePath,
                        Arguments = string.Join(" ", args),
                        UseShellExecute = true,
                        CreateNoWindow = false
                    };
                    
                    Process.Start(startInfo);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Auto-restart failed: {ex.Message}");
            }
            
            Environment.Exit(0);
        }

        private static void UpdateRestartCount()
        {
            try
            {
                if (!File.Exists("Config.json"))
                    return;
                
                var jsonContent = File.ReadAllText("Config.json");
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent);
                
                if (data != null)
                {
                    var currentCount = 0;
                    if (data.ContainsKey("restartCount"))
                    {
                        currentCount = data["restartCount"].GetInt32();
                    }
                    
                    currentCount++;
                    
                    var newData = new Dictionary<string, object>
                    {
                        ["autoRestart"] = data.ContainsKey("autoRestart") ? data["autoRestart"].GetBoolean() : false,
                        ["maxRestartAttempts"] = data.ContainsKey("maxRestartAttempts") ? data["maxRestartAttempts"].GetInt32() : 3,
                        ["restartDelaySeconds"] = data.ContainsKey("restartDelaySeconds") ? data["restartDelaySeconds"].GetInt32() : 5,
                        ["restartCount"] = currentCount,
                        ["lastRestartTime"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    };
                    
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var newJsonContent = JsonSerializer.Serialize(newData, options);
                    File.WriteAllText("Config.json", newJsonContent);
                }
            }
            catch (Exception)
            {
            }
        }

        private static string ReadLanguageConfig()
        {
            try
            {
                if (!File.Exists("Config.json"))
                    return "en";
                
                var jsonContent = File.ReadAllText("Config.json");
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent);
                
                if (data != null)
                {
                    var key = data.Keys.FirstOrDefault(k => k.Equals("Language", StringComparison.OrdinalIgnoreCase));
                    if (key != null)
                    {
                        return data[key].GetString() ?? "en";
                    }
                }
            }
            catch (Exception)
            {
            }
            
            return "en";
        }

        private static bool IsPortInUse(int port, string listenIP)
        {
            try
            {
                System.Net.IPAddress ipAddress;
                if (listenIP == "::")
                {
                    ipAddress = System.Net.IPAddress.IPv6Any;
                }
                else if (listenIP == "0.0.0.0")
                {
                    ipAddress = System.Net.IPAddress.Any;
                }
                else
                {
                    ipAddress = System.Net.IPAddress.Parse(listenIP);
                }

                using var socket = new System.Net.Sockets.Socket(
                    ipAddress.AddressFamily,
                    System.Net.Sockets.SocketType.Stream,
                    System.Net.Sockets.ProtocolType.Tcp);
                
                socket.ExclusiveAddressUse = true;
                
                if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    socket.SetSocketOption(
                        System.Net.Sockets.SocketOptionLevel.IPv6,
                        System.Net.Sockets.SocketOptionName.IPv6Only,
                        0);
                }
                
                socket.Bind(new System.Net.IPEndPoint(ipAddress, port));
                socket.Close();
                
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
