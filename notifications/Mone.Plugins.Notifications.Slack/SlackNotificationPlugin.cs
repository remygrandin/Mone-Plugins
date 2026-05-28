using System.Net.Http.Json;
using System.Text.Json;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace Mone.Plugins.Notifications.Slack;

[NotificationPlugin]
public sealed class SlackNotificationPlugin : INotificationPlugin, IConfigurablePlugin
{
    public ConfigManifest GetConfigManifest() => new()
    {
        Fields =
        [
            new ConfigField { Key = "WebhookUrl", DisplayName = "Webhook URL", Description = "Slack incoming webhook URL", FieldType = ConfigFieldType.Secret, Required = true, IsGlobal = true },
        ]
    };
    public string Name => "SlackNotifier";
    public Version Version => new(1, 0, 0);
    public string Description => "Sends notifications to Slack via incoming webhook";

    private string _webhookUrl = string.Empty;
    private bool _configured;

    public Task InitializeAsync(IPluginContext context)
    {
        var config = context.Configuration;

        if (!config.TryGetValue("WebhookUrl", out var webhookUrl) || string.IsNullOrWhiteSpace(webhookUrl))
            return Task.CompletedTask;

        _webhookUrl = webhookUrl;
        _configured = true;
        return Task.CompletedTask;
    }

    public async Task<DeliveryResult> SendAsync(StatusChange statusChange, CancellationToken cancellationToken)
    {
        if (!_configured)
            return new DeliveryResult(false, "Missing WebhookUrl configuration");

        try
        {
            var text = $"[Mone Alert] Host {statusChange.TargetId}: {statusChange.PreviousStatus} → {statusChange.CurrentStatus}\n" +
                       $"Changed: {statusChange.ChangedAt:O}\n" +
                       $"Status: {statusChange.LatestResult.Status}\n" +
                       $"Summary: {statusChange.LatestResult.Summary}";

            var payload = new { text };
            var json = JsonSerializer.Serialize(payload);

            using var client = new HttpClient();
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync(_webhookUrl, content, cancellationToken);

            return response.IsSuccessStatusCode
                ? new DeliveryResult(true)
                : new DeliveryResult(false, $"Slack webhook returned HTTP {(int)response.StatusCode}");
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
