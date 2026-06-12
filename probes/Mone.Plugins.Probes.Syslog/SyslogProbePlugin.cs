using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;

namespace Mone.Plugins.Probes.Syslog;

/// <summary>
/// Passive syslog ingress probe. Owns its own UDP socket bound to <c>:514</c>, decodes each datagram
/// as an RFC 3164/5424 message, resolves the assignment whose host address matches the sender, and
/// publishes a result through the executor-provided host (which spools when NATS is down).
/// </summary>
public sealed class SyslogProbePlugin : IPassiveProbePlugin, IConfigurablePlugin
{
    public const int ListenPort = 514;

    private UdpClient? _socket;
    private Task? _receiveLoop;
    private IPassiveProbeHost? _host;

    public string Name => "Syslog";
    public Version Version => new(1, 0, 0);
    public string Description => "Passive UDP syslog probe — receives and parses RFC 3164/5424 syslog messages";
    public PassiveProtocol Protocol => PassiveProtocol.Udp;
    public int Port => ListenPort;

    public ConfigManifest GetConfigManifest() => new() { Fields = [] };

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

    public Task StartAsync(IPassiveProbeHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _socket = new UdpClient(new IPEndPoint(IPAddress.Any, ListenPort));
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try { _socket?.Close(); } catch { /* already closing */ }
        if (_receiveLoop is not null)
        {
            try { await _receiveLoop; } catch (OperationCanceledException) { /* shutting down */ }
        }
        _socket?.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var socket = _socket!;
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await socket.ReceiveAsync(ct);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            var assignment = _host!.Assignments.FirstOrDefault(a =>
                string.Equals(a.HostAddress, received.RemoteEndPoint.Address.ToString(), StringComparison.OrdinalIgnoreCase));
            if (assignment is null)
                continue;

            var result = ParseDatagram(received.Buffer, received.RemoteEndPoint);
            await _host.PublishResultAsync(assignment.HostId.ToString(), result, ct);
        }
    }

    /// <summary>Decode a syslog datagram into a result. Pure — no I/O, no socket state.</summary>
    internal static ProbeResult ParseDatagram(ReadOnlyMemory<byte> datagram, IPEndPoint remoteEndpoint)
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

            return new ProbeResult(
                MonitoringStatus.Unknown,
                $"Failed to parse syslog message from {remoteEndpoint}",
                DateTimeOffset.UtcNow,
                sw.Elapsed,
                errorMetadata);
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

        return new ProbeResult(
            status,
            summary,
            syslog.Timestamp,
            sw.Elapsed,
            metadata);
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
