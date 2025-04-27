using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Documents;

namespace Gemeni.Services
{
    public class StreamingResponse
    {
        public Candidate[] candidates { get; set; }
    }

    public class Candidate
    {
        public ContentData content { get; set; }
    }

    public class ContentData
    {
        public Part[] parts { get; set; }
        public string role { get; set; }
    }

    public class Part
    {
        public string text { get; set; }
        public ImagePart image { get; set; }
    }

    public class ImagePart
    {
        public string mimeType { get; set; }
        public string data { get; set; }
    }

    public class GeminiApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ApiKeyManager _apiKeyManager;
        private readonly Settings _settings;
        private readonly TelemetryService _telemetryService;
        private CancellationTokenSource _cancellationTokenSource;
        private List<dynamic> _conversationHistory = new List<dynamic>();
        private bool _isNewConversation = true;
        private StringBuilder _fullResponseBuilder = new StringBuilder();

        public delegate void ApiResponseCallback(string responseText, bool isComplete);
        public delegate void ApiErrorCallback(string errorMessage);
        public delegate void ApiResponseStartCallback();

        public GeminiApiService(Settings settings, ApiKeyManager apiKeyManager)
        {
            _settings = settings;
            _apiKeyManager = apiKeyManager;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(2);
            _telemetryService = TelemetryService.Instance;
        }

        public void CancelRequest()
        {
            _cancellationTokenSource?.Cancel();
        }

        public void ClearConversation()
        {
            _conversationHistory.Clear();
            _isNewConversation = true;
            _fullResponseBuilder.Clear();
        }

        public void AddUserMessageToHistory(string message)
        {
            _conversationHistory.Add(new
            {
                role = "user",
                parts = new[] { new { text = message } }
            });
        }

        public async Task QueryGeminiWithImage(
            string base64Image, 
            string query, 
            ApiResponseCallback onResponseUpdated, 
            ApiErrorCallback onError, 
            ApiResponseStartCallback onResponseStart)
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();
                
                var cancellationToken = _cancellationTokenSource.Token;
                
                _fullResponseBuilder.Clear();

                string apiKey = _apiKeyManager.GetKey();
                if (string.IsNullOrEmpty(apiKey))
                {
                    onError("API key not found. Please check your API key.");
                    return;
                }

                string modelId = _settings.SelectedModelId;
                if (string.IsNullOrEmpty(modelId))
                {
                    onError("No model supporting image processing found.");
                    return;
                }

                await _telemetryService.LogApiUsage(modelId, query, true);

                string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:streamGenerateContent?alt=sse&key={apiKey}";

                var imagePart = new
                {
                    inlineData = new
                    {
                        mimeType = "image/png",
                        data = base64Image
                    }
                };

                var textPart = new
                {
                    text = query
                };
                
                _conversationHistory.Add(new
                {
                    role = "user",
                    parts = new object[] { 
                        new { 
                            inlineData = new {
                                mimeType = "image/png",
                                data = base64Image
                            }
                        }, 
                        new { 
                            text = query
                        } 
                    }
                });

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new object[] { imagePart, textPart }
                        }
                    }
                };

                string jsonRequest = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                using (var request = new HttpRequestMessage(HttpMethod.Post, apiUrl))
                {
                    request.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                    request.Headers.Add("Accept", "text/event-stream");
                    
                    using (var response = await _httpClient.SendAsync(
                        request, 
                        HttpCompletionOption.ResponseHeadersRead, 
                        cancellationToken).ConfigureAwait(false))
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        if (!response.IsSuccessStatusCode)
                        {
                            string errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            onError($"API Error: {response.StatusCode} - {errorContent}");
                            return;
                        }

                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var reader = new StreamReader(stream))
                        {
                            StringBuilder responseText = new StringBuilder();
                            bool firstResponseReceived = false;
                            
                            _fullResponseBuilder.Clear();
                            
                            var buffer = new char[1024];
                            int bytesRead;
                            StringBuilder lineBuilder = new StringBuilder();

                            while ((bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                    return;

                                string chunk = new string(buffer, 0, bytesRead);
                                lineBuilder.Append(chunk);

                                string accumulated = lineBuilder.ToString();
                                int newlineIndex;
                                while ((newlineIndex = accumulated.IndexOf('\n')) >= 0)
                                {
                                    string line = accumulated.Substring(0, newlineIndex).Trim();
                                    accumulated = accumulated.Substring(newlineIndex + 1);

                                    if (line.StartsWith("data: "))
                                    {
                                        string jsonData = line.Substring(6);
                                        if (jsonData == "[DONE]")
                                        {
                                            break;
                                        }

                                        try
                                        {
                                            var responseObj = JsonSerializer.Deserialize<StreamingResponse>(jsonData);
                                            if (responseObj?.candidates != null && responseObj.candidates.Length > 0)
                                            {
                                                var candidate = responseObj.candidates[0];
                                                if (candidate.content?.parts != null && candidate.content.parts.Length > 0)
                                                {
                                                    if (!firstResponseReceived)
                                                    {
                                                        firstResponseReceived = true;
                                                        onResponseStart();
                                                    }
                                                    
                                                    foreach (var part in candidate.content.parts)
                                                    {
                                                        if (!string.IsNullOrEmpty(part.text))
                                                        {
                                                            responseText.Append(part.text);
                                                            string currentText = responseText.ToString();
                                                            _fullResponseBuilder.Clear();
                                                            _fullResponseBuilder.Append(currentText);
                                                            onResponseUpdated(currentText, false);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        catch (JsonException ex)
                                        {
                                            onError($"JSON parsing error: {ex.Message}");
                                        }
                                    }
                                }

                                lineBuilder.Clear();
                                lineBuilder.Append(accumulated);
                            }

                            if (responseText.Length > 0 && !firstResponseReceived)
                            {
                                onResponseStart();
                            }

                            if (responseText.Length > 0)
                            {
                                string finalText = responseText.ToString();
                                
                                _conversationHistory.Add(new
                                {
                                    role = "model",
                                    parts = new[] { new { text = finalText } }
                                });
                                
                                onResponseUpdated(finalText, true);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                onError($"API request error: {ex.Message}");
            }
        }

        public async Task QueryGeminiAPI(string query, ApiResponseCallback onResponseUpdated, 
            ApiErrorCallback onError, ApiResponseStartCallback onResponseStart, bool scrollToEnd)
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();
                
                var cancellationToken = _cancellationTokenSource.Token;
                
                if (_isNewConversation)
                {
                    _fullResponseBuilder.Clear();
                    _isNewConversation = false;
                }
                else
                {
                    _fullResponseBuilder.Clear();
                }

                string apiKey = _apiKeyManager.GetKey();
                if (string.IsNullOrEmpty(apiKey))
                {
                    onError("API key not found. Please check your API key.");
                    return;
                }

                string modelId = _settings.SelectedModelId;
                if (string.IsNullOrEmpty(modelId))
                {
                    onError("Model ID not found. Please check your model selection.");
                    return;
                }

                await _telemetryService.LogApiUsage(modelId, query, false);

                string modelName = _settings.SelectedModelName;

                string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:streamGenerateContent?alt=sse&key={apiKey}";

                object requestBody;

                if (modelId == "gemini-2.5-flash-preview-04-17" && _settings.SelectedModelName == "Gemini 2.5 Flash")
                {
                    requestBody = new
                    {
                        contents = _conversationHistory.ToArray(),
                        generationConfig = new
                        {
                            thinkingConfig = new
                            {
                                thinkingBudget = 0
                            }
                        }
                    };
                }
                else
                {
                    requestBody = new
                    {
                        contents = _conversationHistory.ToArray()
                    };
                }

                string jsonRequest = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                using (var request = new HttpRequestMessage(HttpMethod.Post, apiUrl))
                {
                    request.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                    request.Headers.Add("Accept", "text/event-stream");
                    
                    using (var response = await _httpClient.SendAsync(
                        request, 
                        HttpCompletionOption.ResponseHeadersRead, 
                        cancellationToken).ConfigureAwait(false))
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        if (!response.IsSuccessStatusCode)
                        {
                            string errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            onError($"API Error: {response.StatusCode} - {errorContent}");
                            return;
                        }

                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var reader = new StreamReader(stream))
                        {
                            StringBuilder responseText = new StringBuilder();
                            bool firstResponseReceived = false;
                            
                            _fullResponseBuilder.Clear();
                            
                            var buffer = new char[1024];
                            int bytesRead;
                            StringBuilder lineBuilder = new StringBuilder();

                            while ((bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                    return;

                                string chunk = new string(buffer, 0, bytesRead);
                                lineBuilder.Append(chunk);

                                string accumulated = lineBuilder.ToString();
                                int newlineIndex;
                                while ((newlineIndex = accumulated.IndexOf('\n')) >= 0)
                                {
                                    string line = accumulated.Substring(0, newlineIndex).Trim();
                                    accumulated = accumulated.Substring(newlineIndex + 1);

                                    if (line.StartsWith("data: "))
                                    {
                                        string jsonData = line.Substring(6);
                                        if (jsonData == "[DONE]")
                                        {
                                            break;
                                        }

                                        try
                                        {
                                            var responseObj = JsonSerializer.Deserialize<StreamingResponse>(jsonData);
                                            if (responseObj?.candidates != null && responseObj.candidates.Length > 0)
                                            {
                                                var candidate = responseObj.candidates[0];
                                                if (candidate.content?.parts != null && candidate.content.parts.Length > 0)
                                                {
                                                    if (!firstResponseReceived)
                                                    {
                                                        firstResponseReceived = true;
                                                        onResponseStart();
                                                    }
                                                    
                                                    foreach (var part in candidate.content.parts)
                                                    {
                                                        if (!string.IsNullOrEmpty(part.text))
                                                        {
                                                            responseText.Append(part.text);
                                                            string currentText = responseText.ToString();
                                                            _fullResponseBuilder.Clear();
                                                            _fullResponseBuilder.Append(currentText);
                                                            onResponseUpdated(currentText, false);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        catch (JsonException ex)
                                        {
                                            onError($"JSON parsing error: {ex.Message}");
                                        }
                                    }
                                }

                                lineBuilder.Clear();
                                lineBuilder.Append(accumulated);
                            }

                            if (responseText.Length > 0 && !firstResponseReceived)
                            {
                                onResponseStart();
                            }

                            if (responseText.Length > 0)
                            {
                                string finalText = responseText.ToString();
                                
                                _conversationHistory.Add(new
                                {
                                    role = "model",
                                    parts = new[] { new { text = finalText } }
                                });
                                
                                onResponseUpdated(finalText, true);
                            }
                            else
                            {
                                onError("API did not respond or returned an empty response.");
                            }
                        }
                    }
                }

                _apiKeyManager.ReportUsage(apiKey);
            }
            catch (OperationCanceledException)
            {
                onError("[Request cancelled]");
            }
            catch (Exception ex)
            {
                string errorMessage = $"An error occurred: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMessage += $"\nDetails: {ex.InnerException.Message}";
                }
                onError(errorMessage);
            }
        }
    }
}
