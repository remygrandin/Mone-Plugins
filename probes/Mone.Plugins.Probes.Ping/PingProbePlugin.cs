using System.Net.NetworkInformation;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace Mone.Plugins.Probes.Ping;

[ProbePlugin(ProbeMode = ProbeMode.Active, InstantiationMode = InstantiationMode.PerTarget)]
public sealed class PingProbePlugin : IProbePlugin, IConfigurablePlugin
{
    public ConfigManifest GetConfigManifest() => new()
    {
        Fields =
        [
            new ConfigField { Key = "Timeout", DisplayName = "Timeout (ms)", Description = "ICMP ping timeout in milliseconds", FieldType = ConfigFieldType.Integer, DefaultValue = "5000", Required = false, IsGlobal = false },
            new ConfigField { Key = "BufferSize", DisplayName = "Buffer Size", Description = "Size of the ICMP payload buffer in bytes", FieldType = ConfigFieldType.Integer, DefaultValue = "32", Required = false, IsGlobal = false },
            new ConfigField { Key = "Ttl", DisplayName = "TTL", Description = "Time-to-live for ICMP packets", FieldType = ConfigFieldType.Integer, DefaultValue = "128", Required = false, IsGlobal = false },
        ]
    };
    private int _timeoutMs = 5000;
    private int _bufferSize = 32;
    private int _ttl = 128;

    public string Name => "PingProbe";
    public Version Version => new(1, 0, 0);
    public string Description => "ICMP ping probe — measures latency and reachability";
    public ProbeMode ProbeMode => ProbeMode.Active;
    public InstantiationMode InstantiationMode => InstantiationMode.PerTarget;

    public Task InitializeAsync(IPluginContext context)
    {
        if (context.Configuration.TryGetValue("Timeout", out var timeout) && int.TryParse(timeout, out var t))
            _timeoutMs = t;
        if (context.Configuration.TryGetValue("BufferSize", out var bufSize) && int.TryParse(bufSize, out var b))
            _bufferSize = b;
        if (context.Configuration.TryGetValue("Ttl", out var ttlStr) && int.TryParse(ttlStr, out var ttlVal))
            _ttl = ttlVal;

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MetricDeclaration>> GetMetricsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MetricDeclaration> metrics =
        [
            new MetricDeclaration("success", "Reachable", null,
                new Dictionary<double, string> { [0] = "Failure", [1] = "Success" }),
            new MetricDeclaration("latency_ms", "Round-trip latency", "ms"),
            new MetricDeclaration("ttl", "Reply TTL"),
            new MetricDeclaration("buffer_size", "ICMP payload size", "B"),
        ];
        return Task.FromResult(metrics);
    }

    public async Task<ProbeResult> ExecuteAsync(string targetId, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var pinger = new System.Net.NetworkInformation.Ping();
            var buffer = new byte[_bufferSize];
            var options = new PingOptions { Ttl = _ttl, DontFragment = true };

            var reply = await pinger.SendPingAsync(targetId, _timeoutMs, buffer, options);
            sw.Stop();

            var metadata = new Dictionary<string, object>
            {
                ["success"] = reply.Status == IPStatus.Success ? 1 : 0,
                ["latency_ms"] = reply.RoundtripTime,
                ["ttl"] = reply.Options?.Ttl ?? -1,
                ["buffer_size"] = _bufferSize,
                ["address"] = reply.Address?.ToString() ?? targetId,
                ["ip_status"] = reply.Status.ToString()
            };

            var status = reply.Status switch
            {
                IPStatus.Success => MonitoringStatus.Healthy,
                IPStatus.TimedOut => MonitoringStatus.Unreachable,
                _ => MonitoringStatus.Unhealthy
            };

            var summary = status == MonitoringStatus.Healthy
                ? $"Ping {targetId}: {reply.RoundtripTime}ms, TTL={reply.Options?.Ttl}"
                : $"Ping {targetId}: {reply.Status}";

            return new ProbeResult(status, summary, DateTimeOffset.UtcNow, sw.Elapsed, metadata);
        }
        catch (PlatformNotSupportedException ex)
        {
            sw.Stop();
            return new ProbeResult(
                MonitoringStatus.Unreachable,
                $"Ping unavailable on this platform: {ex.Message}",
                DateTimeOffset.UtcNow,
                sw.Elapsed,
                new Dictionary<string, object> { ["success"] = 0, ["error"] = ex.Message });
        }
        catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.AccessDenied)
        {
            sw.Stop();
            return new ProbeResult(
                MonitoringStatus.Unreachable,
                $"Ping requires elevated privileges (CAP_NET_RAW on Linux): {ex.Message}",
                DateTimeOffset.UtcNow,
                sw.Elapsed,
                new Dictionary<string, object> { ["success"] = 0, ["error"] = ex.Message, ["hint"] = "setcap cap_net_raw+ep on the binary or run as root" });
        }
        catch (InvalidOperationException ex)
        {
            sw.Stop();
            return new ProbeResult(
                MonitoringStatus.Unreachable,
                $"Ping failed for {targetId}: {ex.Message}",
                DateTimeOffset.UtcNow,
                sw.Elapsed,
                new Dictionary<string, object> { ["success"] = 0, ["error"] = ex.Message });
        }
    }
}
