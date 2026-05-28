using System.Text.Json;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace Mone.Plugins.Notifications.Teams;

[NotificationPlugin]
public sealed class TeamsNotificationPlugin : INotificationPlugin, IConfigurablePlugin
{
    public ConfigManifest GetConfigManifest() => new()
    {
        Fields =
        [
            new ConfigField { Key = "WebhookUrl", DisplayName = "Webhook URL", Description = "Microsoft Teams incoming webhook URL", FieldType = ConfigFieldType.Secret, Required = true, IsGlobal = true },
        ]
    };
    public string Name => "TeamsNotifier";
    public Version Version => new(1, 0, 0);
    public string Description => "Sends notifications to Microsoft Teams via incoming webhook";

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
            var payload = BuildAdaptiveCardPayload(statusChange);
            var json = JsonSerializer.Serialize(payload);

            using var client = new HttpClient();
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync(_webhookUrl, content, cancellationToken);

            return response.IsSuccessStatusCode
                ? new DeliveryResult(true)
                : new DeliveryResult(false, $"Teams webhook returned HTTP {(int)response.StatusCode}");
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

    private static object BuildAdaptiveCardPayload(StatusChange statusChange)
    {
        return new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    contentUrl = (string?)null,
                    content = new
                    {
                        type = "AdaptiveCard",
                        version = "1.4",
                        body = new object[]
                        {
                            new
                            {
                                type = "TextBlock",
                                text = $"[Mone Alert] Host {statusChange.TargetId}",
                                weight = "Bolder",
                                size = "Medium"
                            },
                            new
                            {
                                type = "FactSet",
                                facts = new[]
                                {
                                    new { title = "Target", value = statusChange.TargetId },
                                    new { title = "Previous Status", value = statusChange.PreviousStatus.ToString() },
                                    new { title = "Current Status", value = statusChange.CurrentStatus.ToString() },
                                    new { title = "Changed At", value = statusChange.ChangedAt.ToString("O") },
                                    new { title = "Result Status", value = statusChange.LatestResult.Status.ToString() },
                                    new { title = "Summary", value = statusChange.LatestResult.Summary }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}
