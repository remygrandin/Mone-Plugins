using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace Mone.Plugins.Probes.Webhook;

[ProbePlugin(ProbeMode = ProbeMode.Passive, InstantiationMode = InstantiationMode.PerTarget)]
public sealed class WebhookProbePlugin : IPassiveProbePlugin, IConfigurablePlugin
{
    public ConfigManifest GetConfigManifest() => new()
    {
        Fields =
        [
            new ConfigField { Key = "MaxPayloadSize", DisplayName = "Max Payload Size", Description = "Maximum accepted webhook payload size in bytes", FieldType = ConfigFieldType.Integer, DefaultValue = "1048576", Required = false, IsGlobal = false },
        ]
    };
    private long _maxPayloadSize = 1_048_576;

    public string Name => "Webhook";
    public Version Version => new(1, 0, 0);
    public string Description => "Passive webhook ingress probe — accepts HTTP POST payloads from external systems";
    public ProbeMode ProbeMode => ProbeMode.Passive;
    public InstantiationMode InstantiationMode => InstantiationMode.PerTarget;
    public string EndpointPath => "/api/webhooks";

    public Task InitializeAsync(IPluginContext context)
    {
        if (context.Configuration.TryGetValue("MaxPayloadSize", out var maxSize) && long.TryParse(maxSize, out var size))
            _maxPayloadSize = size;

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MetricDeclaration>> GetMetricsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MetricDeclaration> metrics =
        [
            new MetricDeclaration("accepted", "Payload accepted", null,
                new Dictionary<double, string> { [0] = "Rejected", [1] = "Accepted" }),
            new MetricDeclaration("payload_size_bytes", "Payload size", "B"),
        ];
        return Task.FromResult(metrics);
    }

    public Task<ProbeResult> ExecuteAsync(string targetId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Webhook is a passive probe — use HandleAsync via the HTTP endpoint instead of ExecuteAsync.");
    }

    public Task<ProbeResult> HandleAsync(string targetId, string payload, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var payloadSizeBytes = System.Text.Encoding.UTF8.GetByteCount(payload);

        if (payloadSizeBytes > _maxPayloadSize)
        {
            sw.Stop();
            var rejectMetadata = new Dictionary<string, object>
            {
                ["accepted"] = 0,
                ["payload_size_bytes"] = payloadSizeBytes,
                ["max_payload_size_bytes"] = _maxPayloadSize,
                ["received_at"] = DateTimeOffset.UtcNow.ToString("O")
            };

            return Task.FromResult(new ProbeResult(
                MonitoringStatus.Unhealthy,
                $"Webhook payload rejected for {targetId}: {payloadSizeBytes} bytes exceeds limit of {_maxPayloadSize}",
                DateTimeOffset.UtcNow,
                sw.Elapsed,
                rejectMetadata));
        }

        sw.Stop();

        var metadata = new Dictionary<string, object>
        {
            ["accepted"] = 1,
            ["payload"] = payload,
            ["payload_size_bytes"] = payloadSizeBytes,
            ["received_at"] = DateTimeOffset.UtcNow.ToString("O")
        };

        return Task.FromResult(new ProbeResult(
            MonitoringStatus.Healthy,
            $"Webhook received for {targetId}: {payloadSizeBytes} bytes ingested",
            DateTimeOffset.UtcNow,
            sw.Elapsed,
            metadata));
    }
}
