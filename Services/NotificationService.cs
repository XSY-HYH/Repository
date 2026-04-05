using System;
using System.Runtime.InteropServices;

namespace Repository.Services
{
    public class NotificationService
    {
        private readonly bool _isEnabled;
        private readonly bool _isWindows;
        
        public NotificationService(ConfigManager configManager)
        {
            _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            _isEnabled = configManager.GetConfig().Notification && _isWindows;
            
            if (_isEnabled)
            {
                WindowsNotifier.Notification.Initialize();
                Console.WriteLine(I18nService.Instance.T("notification.enabled"));
            }
            else if (!_isWindows)
            {
                Console.WriteLine(I18nService.Instance.T("notification.not_windows"));
            }
        }
        
        public bool IsEnabled => _isEnabled;
        
        public void Send(string title, string content, NotificationLevel level = NotificationLevel.Info)
        {
            if (!_isEnabled)
            {
                return;
            }
            
            try
            {
                var icon = level switch
                {
                    NotificationLevel.Info => WindowsNotifier.NotificationIcon.Info,
                    NotificationLevel.Warning => WindowsNotifier.NotificationIcon.Warning,
                    NotificationLevel.Error => WindowsNotifier.NotificationIcon.Error,
                    _ => WindowsNotifier.NotificationIcon.Info
                };
                
                WindowsNotifier.Notification.Send(title, content, icon);
            }
            catch (Exception ex)
            {
                Console.WriteLine(I18nService.Instance.T("notification.send_failed", ex.Message));
            }
        }
        
        public void SendInfo(string title, string content)
        {
            Send(title, content, NotificationLevel.Info);
        }
        
        public void SendWarning(string title, string content)
        {
            Send(title, content, NotificationLevel.Warning);
        }
        
        public void SendError(string title, string content)
        {
            Send(title, content, NotificationLevel.Error);
        }
        
        public void Cleanup()
        {
            if (_isEnabled)
            {
                try
                {
                    WindowsNotifier.Notification.Cleanup();
                }
                catch
                {
                }
            }
        }
    }
    
    public enum NotificationLevel
    {
        Info,
        Warning,
        Error
    }
}
