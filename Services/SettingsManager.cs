using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;

namespace Gemeni.Services
{
    public class HotkeySettings
    {
        public Key Key { get; set; } = Key.G;
        public ModifierKeys Modifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;
    }

    public class GenerationConfig
    {
        public string ResponseMimeType { get; set; } = "text/plain";
    }

    public class Settings
    {
        public bool StartWithWindows { get; set; } = false;
        public string SelectedModelId { get; set; } = "gemini-2.5-flash-preview-04-17";
        public string SelectedModelName { get; set; } = "Gemini 2.5 Flash";
        public HotkeySettings Hotkey { get; set; } = new HotkeySettings();
        public string UserId { get; set; } = string.Empty;

        private Dictionary<string, string> _availableModels = new Dictionary<string, string>
        {
            { "Gemini 2.5 Flash", "gemini-2.5-flash-preview-04-17" },
            { "Gemini 2.5 Flash Thinking", "gemini-2.5-flash-preview-04-17" },
            { "Gemini 2.0 Flash Lite", "gemini-2.0-flash-lite-001" },
            { "Gemini 2.0 Flash Thinking", "gemini-2.0-flash-thinking-exp-01-21" },
            { "Gemini 2.5 Pro", "gemini-2.5-pro-exp-03-25" },
        };

        public Dictionary<string, string> AvailableModels 
        { 
            get => _availableModels; 
            set => _availableModels = value;
        }
    }

    public class SettingsManager
    {
        private const string APP_REGISTRY_KEY = "GeminiOverlay";
        private const string SETTINGS_FILE = "settings.json";
        private readonly string _appDataPath;
        private Settings _settings;

        public Settings CurrentSettings => _settings;

        public SettingsManager()
        {
            _appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                "GeminiOverlay");
            
            _settings = new Settings();
            LoadSettings();
            EnsureUserIdExists();
        }

        public void LoadSettings()
        {
            if (!Directory.Exists(_appDataPath))
            {
                Directory.CreateDirectory(_appDataPath);
            }
            
            string settingsFilePath = Path.Combine(_appDataPath, SETTINGS_FILE);
            
            if (File.Exists(settingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(settingsFilePath);
                    _settings = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading settings: {ex.Message}");
                    _settings = new Settings();
                }
            }
            else
            {
                _settings = new Settings();
                SaveSettings();
            }
        }

        private void EnsureUserIdExists()
        {
            if (string.IsNullOrEmpty(_settings.UserId))
            {
                _settings.UserId = Guid.NewGuid().ToString();
                SaveSettings();
                Console.WriteLine($"New user ID created: {_settings.UserId}");
            }
        }

        public void SaveSettings()
        {
            try
            {
                if (!Directory.Exists(_appDataPath))
                {
                    Directory.CreateDirectory(_appDataPath);
                }
                
                string settingsFilePath = Path.Combine(_appDataPath, SETTINGS_FILE);
                
                string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsFilePath, json);
                Console.WriteLine($"Settings saved successfully to {settingsFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        public void SetStartWithWindows(bool startWithWindows)
        {
            bool registryUpdated = false;
            
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (startWithWindows)
                    {
                        string executablePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        key.SetValue(APP_REGISTRY_KEY, executablePath);
                        Console.WriteLine($"Registry startup entry added: {executablePath}");
                    }
                    else
                    {
                        if (key.GetValue(APP_REGISTRY_KEY) != null)
                        {
                            key.DeleteValue(APP_REGISTRY_KEY);
                            Console.WriteLine("Registry startup entry removed");
                        }
                    }
                    registryUpdated = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting startup registry: {ex.Message}");
                registryUpdated = false;
            }
            
            _settings.StartWithWindows = startWithWindows;
            
            try 
            {
                SaveSettings();
                Console.WriteLine($"StartWithWindows value set to: {startWithWindows}");
                
                LoadSettings();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save StartWithWindows setting: {ex.Message}");
            }
            
            if (!registryUpdated)
            {
                Console.WriteLine("Warning: Registry was not updated but settings file was modified");
            }
        }

        public void SetSelectedModel(string modelName, string modelId)
        {
            _settings.SelectedModelName = modelName;
            _settings.SelectedModelId = modelId;
            SaveSettings();
        }
    }
} 
