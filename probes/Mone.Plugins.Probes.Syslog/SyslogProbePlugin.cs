using System.Diagnostics;
using System.Net;
using System.Text;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace Mone.Plugins.Probes.Syslog;

[ProbePlugin(ProbeMode = ProbeMode.Passive, InstantiationMode = InstantiationMode.Batch)]
public sealed class SyslogProbePlugin : IPassiveUdpPlugin, IConfigurablePlugin
{
    public ConfigManifest GetConfigManifest() => new()
    {
        Fields = []
    };
    public string Name => "Syslog";
    public Version Version => new(1, 0, 0);
    public string Description => "Passive UDP syslog probe — receives and parses RFC 3164/5424 syslog messages";
    public ProbeMode ProbeMode => ProbeMode.Passive;
    public InstantiationMode InstantiationMode => InstantiationMode.Batch;
    public int UdpPort => 514;

    public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;

    public Task<IReadOnlyList<MetricDeclaration>> GetMetricsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MetricDeclaration> metrics =
        [
            new MetricDeclaration("message_received", "Message received", null,
                new Dictionary<double, string> { [0] = "Parse failed", [1] = "Received" }),
            new MetricDeclaration("priority", "Syslog priority"),
            new MetricDeclaration("severity_numeric", "Severity (numeric)", null,
                new Dictionary<double, string>
                {
                    [0] = "Emergency",
                    [1] = "Alert",
                    [2] = "Critical",
                    [3] = "Error",
                    [4] = "Warning",
                    [5] = "Notice",
                    [6] = "Informational",
                    [7] = "Debug",
                }),
        ];
        return Task.FromResult(metrics);
    }

    public Task<ProbeResult> ExecuteAsync(string targetId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Syslog is a passive UDP probe — use HandleDatagramAsync instead of ExecuteAsync.");
    }

    public Task<ProbeResult> HandleDatagramAsync(
        ReadOnlyMemory<byte> datagram,
        IPEndPoint remoteEndpoint,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        var rawMessage = Encoding.UTF8.GetString(datagram.Span);

        if (!SyslogMessageParser.TryParse(rawMessage, out var syslog))
        {
            sw.Stop();
            var errorMetadata = new Dictionary<string, object>
            {
                ["message_received"] = 0,
                ["raw_message"] = rawMessage,
                ["remote_endpoint"] = remoteEndpoint.ToString(),
                ["received_at"] = DateTimeOffset.UtcNow.ToString("O")
            };

            return Task.FromResult(new ProbeResult(
                MonitoringStatus.Unknown,
                $"Failed to parse syslog message from {remoteEndpoint}",
                DateTimeOffset.UtcNow,
                sw.Elapsed,
                errorMetadata));
        }

        sw.Stop();

        var status = MapSeverityToStatus(syslog.Severity);

        var metadata = new Dictionary<string, object>
        {
            ["message_received"] = 1,
            ["format"] = syslog.Format.ToString(),
            ["priority"] = syslog.Priority,
            ["facility"] = syslog.Facility.ToString(),
            ["severity"] = syslog.Severity.ToString(),
            ["severity_numeric"] = (int)syslog.Severity,
            ["remote_endpoint"] = remoteEndpoint.ToString(),
            ["received_at"] = DateTimeOffset.UtcNow.ToString("O")
        };

        if (syslog.Hostname is not null)
            metadata["hostname"] = syslog.Hostname;
        if (syslog.AppName is not null)
            metadata["app_name"] = syslog.AppName;
        if (syslog.ProcessId is not null)
            metadata["process_id"] = syslog.ProcessId;
        if (syslog.MsgId is not null)
            metadata["msg_id"] = syslog.MsgId;
        if (syslog.StructuredData is not null)
            metadata["structured_data"] = syslog.StructuredData;
        if (syslog.Message is not null)
            metadata["message"] = syslog.Message;

        var summary = syslog.Message is not null
            ? $"Syslog [{syslog.Severity}] from {syslog.Hostname ?? remoteEndpoint.Address.ToString()}: {Truncate(syslog.Message, 120)}"
            : $"Syslog [{syslog.Severity}] from {syslog.Hostname ?? remoteEndpoint.Address.ToString()}";

        return Task.FromResult(new ProbeResult(
            status,
            summary,
            syslog.Timestamp,
            sw.Elapsed,
            metadata));
    }

    internal static MonitoringStatus MapSeverityToStatus(SyslogSeverity severity) => severity switch
    {
        SyslogSeverity.Emergency => MonitoringStatus.Unhealthy,
        SyslogSeverity.Alert => MonitoringStatus.Unhealthy,
        SyslogSeverity.Critical => MonitoringStatus.Unhealthy,
        SyslogSeverity.Error => MonitoringStatus.Degraded,
        SyslogSeverity.Warning => MonitoringStatus.Degraded,
        SyslogSeverity.Notice => MonitoringStatus.Healthy,
        SyslogSeverity.Informational => MonitoringStatus.Healthy,
        SyslogSeverity.Debug => MonitoringStatus.Healthy,
        _ => MonitoringStatus.Unknown
    };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 3), "...");
}
