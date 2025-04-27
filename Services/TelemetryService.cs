using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Gemeni.Services
{
    public class TelemetryService
    {
        private static TelemetryService _instance;
        private readonly HttpClient _httpClient;
        private readonly string _telemetryServerUrl = "https://telemetry.frondev.com";
        private readonly Dictionary<string, string> _modelMap = new Dictionary<string, string>
        {
            { "gemini-2.5-flash-preview-04-17", "Gemini 2.5 Flash" },
            { "gemini-2.5-flash-preview-04-17-thinking", "Gemini 2.5 Flash Thinking" },
            { "gemini-2.0-flash-lite-001", "Gemini 2.0 Flash Lite" },
            { "gemini-2.0-flash-thinking-exp-01-21", "Gemini 2.0 Flash Thinking" },
            { "gemini-2.5-pro-exp-03-25", "Gemini 2.5 Pro" }
        };
        
        private readonly SettingsManager _settingsManager;
        
        public static TelemetryService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TelemetryService();
                }
                return _instance;
            }
        }

        private TelemetryService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _settingsManager = new SettingsManager();
        }

        public async Task LogApiUsage(string modelId, string query = null, bool isImageQuery = false)
        {
            try
            {
                if (!_modelMap.ContainsKey(modelId))
                {
                    Console.WriteLine($"Unknown model ID: {modelId}");
                    return;
                }

                string userId = _settingsManager.CurrentSettings.UserId;
                if (string.IsNullOrEmpty(userId))
                {
                    Console.WriteLine("User ID not found, cannot send telemetry.");
                    return;
                }

                var telemetryData = new
                {
                    user_id = userId,
                    request_data = new
                    {
                        timestamp = DateTime.UtcNow.ToString("o"),
                        is_image_query = isImageQuery,
                        query_length = query?.Length ?? 0,
                    }
                };

                var json = JsonSerializer.Serialize(telemetryData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_telemetryServerUrl}/telemetry/{modelId}", content);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Telemetry sending error: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while sending telemetry: {ex.Message}");
            }
        }
    }
} 