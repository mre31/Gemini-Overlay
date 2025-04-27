using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Gemeni.Services
{
    public class ApiKeyManager
    {
        private readonly Dictionary<string, ApiKeyInfo> _apiKeys;
        private int _currentKeyIndex = 0;
        private static ApiKeyManager _instance;

        public static ApiKeyManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ApiKeyManager();
                }
                return _instance;
            }
        }
        private ApiKeyManager()
        {
            _apiKeys = new Dictionary<string, ApiKeyInfo>();
            LoadApiKeysFromEnvFile();
        }

        private void LoadApiKeysFromEnvFile()
        {
            try
            {
                string executablePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string executableDir = Path.GetDirectoryName(executablePath);
                string envFilePath = Path.Combine(executableDir, ".env");
                
                if (!File.Exists(envFilePath))
                {
                    throw new FileNotFoundException(".env file not found. Please create a .env file in the application directory.");
                }
                
                string[] lines = File.ReadAllLines(envFilePath);
                int keyCount = 0;
                
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;
                        
                    string[] parts = line.Split('=');
                    if (parts.Length < 2)
                        continue;
                        
                    string key = parts[0].Trim();
                    string value = string.Join("=", parts.Skip(1)).Trim();
                    
                    if (key.StartsWith("API_KEY_") && !string.IsNullOrEmpty(value))
                    {
                        _apiKeys.Add(key, new ApiKeyInfo { Key = value, UsageCount = 0 });
                        keyCount++;
                    }
                }
                
                if (keyCount == 0)
                {
                    throw new InvalidOperationException("No API key found in the .env file. Please add at least one key in the format API_KEY_1=yourkey.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading API keys: {ex.Message}");
                throw new InvalidOperationException($"Failed to load API keys: {ex.Message}");
            }
        }

        public string GetKey(int timeWindow = 60, int maxUses = 100)
        {
            if (_apiKeys.Count == 0)
            {
                throw new InvalidOperationException("No API keys found. Please check the .env file.");
            }
            
            if (_apiKeys.Values.All(k => k.IsRateLimited(timeWindow, maxUses)))
            {
                throw new RateLimitExceededException("All API keys have exceeded the rate limit.");
            }
            
            ApiKeyInfo currentKey = _apiKeys.Values.ElementAt(_currentKeyIndex);
            if (currentKey.IsRateLimited(timeWindow, maxUses))
            {
                RotateToNextAvailableKey(timeWindow, maxUses);
                currentKey = _apiKeys.Values.ElementAt(_currentKeyIndex);
            }
            return currentKey.Key;
        }
        
        public void ReportUsage(string usedKey)
        {
            var keyInfo = _apiKeys.Values.FirstOrDefault(k => k.Key == usedKey);
            if (keyInfo != null)
            {
                keyInfo.RecordUsage();
            }
            _currentKeyIndex = (_currentKeyIndex + 1) % _apiKeys.Count;
        }
        
        private void RotateToNextAvailableKey(int timeWindow, int maxUses)
        {
            int startIndex = _currentKeyIndex;
            do
            {
                _currentKeyIndex = (_currentKeyIndex + 1) % _apiKeys.Count;
                if (_currentKeyIndex == startIndex)
                {
                    throw new RateLimitExceededException("All API keys have exceeded the rate limit.");
                }
            } while (_apiKeys.Values.ElementAt(_currentKeyIndex).IsRateLimited(timeWindow, maxUses));
        }
        
        public object GetUsageStatistics(object keyNameOrIndex = null)
        {
            if (keyNameOrIndex == null || keyNameOrIndex.ToString().ToLower() == "all")
            {
                var stats = new Dictionary<string, int>();
                foreach (var key in _apiKeys)
                {
                    stats.Add(key.Key, key.Value.UsageCount);
                }
                return stats;
            }
            if (keyNameOrIndex is int index)
            {
                if (index >= 0 && index < _apiKeys.Count)
                {
                    return _apiKeys.Values.ElementAt(index).UsageCount;
                }
                throw new ArgumentOutOfRangeException("Invalid key index.");
            }
            string keyName = keyNameOrIndex.ToString();
            if (_apiKeys.ContainsKey(keyName))
            {
                return _apiKeys[keyName].UsageCount;
            }
            throw new KeyNotFoundException("Specified key not found.");
        }
        
        public List<string> GetAllKeys()
        {
            return _apiKeys.Values.Select(k => k.Key).ToList();
        }
        
        public Dictionary<string, string> GetKeyNames()
        {
            var keyNames = new Dictionary<string, string>();
            foreach (var key in _apiKeys)
            {
                keyNames.Add(key.Value.Key, key.Key);
            }
            return keyNames;
        }
        
        public string GetCurrentKeyName()
        {
            return _apiKeys.Keys.ElementAt(_currentKeyIndex);
        }
    }
    
    public class ApiKeyInfo
    {
        public string Key { get; set; }
        public int UsageCount { get; set; }
        private readonly List<DateTime> _usageTimestamps = new List<DateTime>();
        public void RecordUsage()
        {
            UsageCount++;
            _usageTimestamps.Add(DateTime.Now);
        }
        public bool IsRateLimited(int timeWindow, int maxUses)
        {
            DateTime cutoff = DateTime.Now.AddSeconds(-timeWindow);
            int recentUses = _usageTimestamps.Count(t => t >= cutoff);
            return recentUses >= maxUses;
        }
    }
    
    public class RateLimitExceededException : Exception
    {
        public RateLimitExceededException(string message) : base(message) { }
    }
}
