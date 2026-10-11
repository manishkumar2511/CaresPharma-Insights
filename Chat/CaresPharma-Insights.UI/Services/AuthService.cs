using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using CaresPharma_Insights.UI.Models;

namespace CaresPharma_Insights.UI.Services
{
    public class AuthService
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly HttpClient _httpClient;
        private readonly string _caresPharmaBaseUrl;

        public UserProfileModel? CurrentUser { get; private set; }

        public AuthService(IJSRuntime jsRuntime, HttpClient httpClient, IConfiguration configuration)
        {
            _jsRuntime = jsRuntime;
            _httpClient = httpClient;
            _caresPharmaBaseUrl = configuration["CaresPharmaAppUrl"] ?? "http://localhost:4200";
        }

        public async Task<string?> GetTokenAsync()
        {
            return await _jsRuntime.InvokeAsync<string?>("carespharmaAuthBridge.getToken");
        }

        public async Task SetTokenAsync(string token, string? userInfoJson = null)
        {
            if (string.IsNullOrWhiteSpace(token)) return;

            await _jsRuntime.InvokeVoidAsync("carespharmaAuthBridge.setToken", token, userInfoJson);
            ApplyAuthorizationHeader(token);
            await LoadUserProfileAsync(token);
        }

        public async Task ClearTokenAsync()
        {
            await _jsRuntime.InvokeVoidAsync("carespharmaAuthBridge.clearToken");
            _httpClient.DefaultRequestHeaders.Authorization = null;
            CurrentUser = null;
        }

        public async Task<bool> IsAuthenticatedAsync()
        {
            string? token = await GetTokenAsync();
            return !string.IsNullOrWhiteSpace(token);
        }

        public void ApplyAuthorizationHeader(string token)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        public async Task InitializeAsync()
        {
            string? token = await GetTokenAsync();
            if (!string.IsNullOrWhiteSpace(token))
            {
                ApplyAuthorizationHeader(token);
                await LoadUserProfileAsync(token);
            }
        }

        public async Task LoadUserProfileAsync(string? token = null)
        {
            try
            {
                token ??= await GetTokenAsync();
                JsonElement? profileJson = await _jsRuntime.InvokeAsync<JsonElement?>("carespharmaAuthBridge.getUserProfile", token);
                if (profileJson.HasValue && profileJson.Value.ValueKind == JsonValueKind.Object)
                {
                    JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    CurrentUser = JsonSerializer.Deserialize<UserProfileModel>(profileJson.Value.GetRawText(), options);
                }
            }
            catch (Exception)
            {
                CurrentUser = null;
            }
        }

        /// <summary>
        /// Opens a Google-like popup window to CaresPharma Login and waits for successful token & user postMessage
        /// </summary>
        public async Task<bool> LoginWithPopupAsync()
        {
            try
            {
                JsonElement? result = await _jsRuntime.InvokeAsync<JsonElement?>("carespharmaAuthBridge.openLoginPopup", _caresPharmaBaseUrl);
                if (result.HasValue && result.Value.ValueKind == JsonValueKind.Object)
                {
                    if (result.Value.TryGetProperty("token", out JsonElement tokenProp))
                    {
                        string? token = tokenProp.GetString();
                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            string? userJson = null;
                            if (result.Value.TryGetProperty("user", out JsonElement userProp) && userProp.ValueKind == JsonValueKind.Object)
                            {
                                userJson = userProp.GetRawText();
                            }

                            await SetTokenAsync(token, userJson);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Popup auth error: {ex.Message}");
            }

            return false;
        }
    }
}
