
using Microsoft.Extensions.Options;
using SOSCallParsial.Models;
using SOSCallParsial.Models.Configs;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SOSCallParsial.Services
{
    public class CallQueueService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CallQueueService> _logger;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _tokenUrl;
        private readonly string _extension;

        public CallQueueService(HttpClient httpClient, ILogger<CallQueueService> logger, IOptions<CallSettings> options)
        {
            _httpClient = httpClient;
            _logger = logger;
            _clientId = options.Value.ClientId;
            _clientSecret = options.Value.ClientSecret;
            _tokenUrl = options.Value.TokenUrl;
            _extension = options.Value.Extension;
        }

        public async Task EnqueueCallAsync(DomoMessage message)
        {
            if (string.IsNullOrEmpty(message.PhoneNumber)) return;

            try
            {
                var tokenRequest = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", _clientId),
                    new KeyValuePair<string, string>("client_secret", _clientSecret),
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                });

                var tokenResponse = await _httpClient.PostAsync(_tokenUrl, tokenRequest);
                var tokenJson = await tokenResponse.Content.ReadAsStringAsync();

                if (!tokenResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to get token: {0}", tokenJson);
                    return;
                }

                using var tokenDoc = JsonDocument.Parse(tokenJson);
                string accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var callPayload = new
                {
                    destination = "+" + message.PhoneNumber
                };

                string json = JsonSerializer.Serialize(callPayload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                string callUrl = $"https://sos-medecins.3cx.ch/callcontrol/{_extension}/makecall";

                var callResponse = await _httpClient.PostAsync(callUrl, content);
                var result = await callResponse.Content.ReadAsStringAsync();




                if (callResponse.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Call successfully initiated to {0}", message.PhoneNumber);
                }
                else
                {
                    _logger.LogWarning("Failed to initiate call. Status: {0}. Response: {1}", callResponse.StatusCode, result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while trying to enqueue call.");
            }
        }
    }
}
