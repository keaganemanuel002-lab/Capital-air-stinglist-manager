using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;

namespace StingListManager.Services;

public sealed class FirebasePushNotificationService
{
    private static readonly HttpClient HttpClient = new();
    private readonly AppSettings _settings;

    public FirebasePushNotificationService(AppSettings settings)
    {
        _settings = settings;
    }

    public bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(_settings.FirebaseProjectId)
            && !string.IsNullOrWhiteSpace(_settings.FirebaseServiceAccountJsonPath)
            && File.Exists(_settings.FirebaseServiceAccountJsonPath);
    }

    public async Task<(bool ok, string message)> SendTopicNotificationAsync(
        string topic,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured())
            return (false, "Firebase push is not configured.");

        if (string.IsNullOrWhiteSpace(topic))
            return (false, "Notification topic is required.");

        try
        {
            var credential = GoogleCredential.FromFile(_settings.FirebaseServiceAccountJsonPath!)
                .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");

            var accessToken = await credential.UnderlyingCredential
                .GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);

            var payload = new
            {
                message = new
                {
                    topic = topic.Trim(),
                    notification = new
                    {
                        title = title.Trim(),
                        body = body.Trim()
                    },
                    data = data ?? new Dictionary<string, string>()
                }
            };

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://fcm.googleapis.com/v1/projects/{_settings.FirebaseProjectId}/messages:send");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var shortBody = string.IsNullOrWhiteSpace(responseBody)
                    ? "(empty)"
                    : responseBody.Trim();
                return (false, $"FCM send failed: HTTP {(int)response.StatusCode} {shortBody}");
            }

            return (true, "Notification sent.");
        }
        catch (Exception ex)
        {
            return (false, $"FCM send failed: {ex.Message}");
        }
    }
}

