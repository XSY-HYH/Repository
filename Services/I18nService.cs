using System.Reflection;
using System.Text;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Repository.Services
{
    public class I18nService
    {
        private static I18nService? _instance;
        private static readonly object _lock = new object();
        
        private Dictionary<string, Dictionary<string, string>> _translations = new();
        private string _currentLanguage = "en";
        private Logger? _logger;
        
        public static I18nService Instance
        {
            get
            {
                if (_instance == null)
                {
                    throw new InvalidOperationException("I18nService not initialized. Call Initialize() first.");
                }
                return _instance;
            }
        }
        
        private I18nService()
        {
        }
        
        public static void Initialize(string language = "en")
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new I18nService();
                    _instance.LoadTranslations(language);
                }
            }
        }
        
        public void SetLogger(Logger logger)
        {
            _logger = logger;
        }
        
        public void SetLanguage(string language)
        {
            if (_currentLanguage == language)
                return;
            
            LoadTranslations(language);
        }
        
        private void LoadTranslations(string language)
        {
            _translations.Clear();
            
            LoadEmbeddedTranslations();
            
            if (language != "en" && language != "zh")
            {
                if (!LoadExternalTranslations(language))
                {
                    LogWarning("lang.fallback", language);
                    language = "en";
                }
            }
            
            _currentLanguage = language;
            LogInfo("lang.set", language);
        }
        
        private void LoadEmbeddedTranslations()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "Repository.lang.yml";
                
                if (assembly.GetManifestResourceNames().Contains(resourceName))
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        var yamlContent = reader.ReadToEnd();
                        ParseYamlContent(yamlContent);
                        LogInfo("lang.embedded_loaded");
                    }
                }
                else
                {
                    LogWarning("lang.embedded_not_found");
                }
            }
            catch (Exception ex)
            {
                LogError("lang.parse_failed", ex.Message);
            }
        }
        
        private bool LoadExternalTranslations(string language)
        {
            try
            {
                var langDir = Path.Combine(AppContext.BaseDirectory, "lang");
                var langFile = Path.Combine(langDir, $"{language}.yml");
                
                if (!File.Exists(langFile))
                {
                    return false;
                }
                
                var yamlContent = File.ReadAllText(langFile, Encoding.UTF8);
                ParseYamlContent(yamlContent);
                LogInfo("lang.external_loaded", langFile);
                return true;
            }
            catch (Exception ex)
            {
                LogError("lang.external_failed", language, ex.Message);
                return false;
            }
        }
        
        private void ParseYamlContent(string yamlContent)
        {
            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build();
                
                var result = deserializer.Deserialize<Dictionary<string, object>>(yamlContent);
                
                if (result == null)
                    return;
                
                foreach (var lang in result)
                {
                    if (lang.Value is Dictionary<object, object> translations)
                    {
                        var langDict = new Dictionary<string, string>();
                        FlattenDictionary(translations, "", langDict);
                        _translations[lang.Key] = langDict;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("lang.parse_failed", ex.Message);
            }
        }
        
        private void FlattenDictionary(Dictionary<object, object> source, string prefix, Dictionary<string, string> target)
        {
            foreach (var kvp in source)
            {
                var key = prefix.Length > 0 ? $"{prefix}.{kvp.Key}" : kvp.Key.ToString() ?? "";
                
                if (kvp.Value is Dictionary<object, object> nested)
                {
                    FlattenDictionary(nested, key, target);
                }
                else
                {
                    target[key] = kvp.Value?.ToString() ?? "";
                }
            }
        }
        
        private void LogInfo(string key, params object[] args)
        {
            var message = args.Length > 0 ? string.Format(GetTranslation(key), args) : GetTranslation(key);
            if (_logger != null)
            {
                _logger.LogInfo(message);
            }
            else
            {
                Console.WriteLine($"[I18n] {message}");
            }
        }
        
        private void LogWarning(string key, params object[] args)
        {
            var message = args.Length > 0 ? string.Format(GetTranslation(key), args) : GetTranslation(key);
            if (_logger != null)
            {
                _logger.LogWarning(message);
            }
            else
            {
                Console.WriteLine($"[I18n] Warning: {message}");
            }
        }
        
        private void LogError(string key, params object[] args)
        {
            var message = args.Length > 0 ? string.Format(GetTranslation(key), args) : GetTranslation(key);
            if (_logger != null)
            {
                _logger.LogError(message);
            }
            else
            {
                Console.WriteLine($"[I18n] Error: {message}");
            }
        }
        
        public string T(string key, params object[] args)
        {
            return Translate(key, args);
        }
        
        public string Translate(string key, params object[] args)
        {
            var translation = GetTranslation(key);
            
            if (args.Length > 0)
            {
                try
                {
                    return string.Format(translation, args);
                }
                catch
                {
                    return translation;
                }
            }
            
            return translation;
        }
        
        private string GetTranslation(string key)
        {
            if (_translations.TryGetValue(_currentLanguage, out var langTranslations))
            {
                if (langTranslations.TryGetValue(key, out var translation))
                {
                    return translation;
                }
            }
            
            if (_currentLanguage != "en" && _translations.TryGetValue("en", out var enTranslations))
            {
                if (enTranslations.TryGetValue(key, out var translation))
                {
                    return translation;
                }
            }
            
            return key;
        }
        
        public string CurrentLanguage => _currentLanguage;
        
        public IEnumerable<string> AvailableLanguages => _translations.Keys;
        
        public string GetWebTranslationsJson()
        {
            var webTranslations = new Dictionary<string, string>();
            
            if (_translations.TryGetValue(_currentLanguage, out var langTranslations))
            {
                foreach (var kvp in langTranslations)
                {
                    if (kvp.Key.StartsWith("web."))
                    {
                        var webKey = kvp.Key.Substring(4);
                        webTranslations[webKey] = kvp.Value;
                    }
                }
            }
            
            if (_currentLanguage != "en" && _translations.TryGetValue("en", out var enTranslations))
            {
                foreach (var kvp in enTranslations)
                {
                    if (kvp.Key.StartsWith("web."))
                    {
                        var webKey = kvp.Key.Substring(4);
                        if (!webTranslations.ContainsKey(webKey))
                        {
                            webTranslations[webKey] = kvp.Value;
                        }
                    }
                }
            }
            
            return JsonSerializer.Serialize(webTranslations);
        }
    }
    
    public static class I18nExtensions
    {
        public static string T(this string key, params object[] args)
        {
            return I18nService.Instance.Translate(key, args);
        }
    }
}
