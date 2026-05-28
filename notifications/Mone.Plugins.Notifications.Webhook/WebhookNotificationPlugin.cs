using System.Net.Http.Headers;
using System.Text.Json;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace Mone.Plugins.Notifications.Webhook;

[NotificationPlugin]
public sealed class WebhookNotificationPlugin : INotificationPlugin, IConfigurablePlugin
{
    public ConfigManifest GetConfigManifest() => new()
    {
        Fields =
        [
            new ConfigField { Key = "Url", DisplayName = "URL", Description = "Webhook endpoint URL", FieldType = ConfigFieldType.String, Required = true, IsGlobal = false },
            new ConfigField { Key = "Method", DisplayName = "HTTP Method", Description = "HTTP method to use for the webhook request", FieldType = ConfigFieldType.Choice, DefaultValue = "POST", Required = false, IsGlobal = false, Choices = ["GET", "POST", "PUT", "PATCH"] },
            new ConfigField { Key = "Headers", DisplayName = "Headers", Description = "Comma-separated key:value header pairs", FieldType = ConfigFieldType.String, Required = false, IsGlobal = false },
        ]
    };
    public string Name => "WebhookNotifier";
    public Version Version => new(1, 0, 0);
    public string Description => "Sends notifications to a configurable HTTP webhook endpoint";

    private string _url = string.Empty;
    private HttpMethod _method = HttpMethod.Post;
    private Dictionary<string, string> _headers = [];
    private bool _configured;

    public Task InitializeAsync(IPluginContext context)
    {
        var config = context.Configuration;

        if (!config.TryGetValue("Url", out var url) || string.IsNullOrWhiteSpace(url))
            return Task.CompletedTask;

        _url = url;

        if (config.TryGetValue("Method", out var method) && !string.IsNullOrWhiteSpace(method))
            _method = new HttpMethod(method.ToUpperInvariant());

        if (config.TryGetValue("Headers", out var headersRaw) && !string.IsNullOrWhiteSpace(headersRaw))
        {
            _headers = headersRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(h => h.Split(':', 2, StringSplitOptions.TrimEntries))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1]);
        }

        _configured = true;
        return Task.CompletedTask;
    }

    public async Task<DeliveryResult> SendAsync(StatusChange statusChange, CancellationToken cancellationToken)
    {
        if (!_configured)
            return new DeliveryResult(false, "Missing Url configuration");

        try
        {
            var json = JsonSerializer.Serialize(statusChange);

            using var client = new HttpClient();
            using var request = new HttpRequestMessage(_method, _url);
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            foreach (var (key, value) in _headers)
                request.Headers.TryAddWithoutValidation(key, value);

            var response = await client.SendAsync(request, cancellationToken);

            return response.IsSuccessStatusCode
                ? new DeliveryResult(true)
                : new DeliveryResult(false, $"Webhook returned HTTP {(int)response.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            return new DeliveryResult(false, ex.Message);
        }
        catch (TaskCanceledException ex)
        {
            return new DeliveryResult(false, $"Request timed out: {ex.Message}");
        }
    }
}
