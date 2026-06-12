using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;

namespace Mone.Plugins.Probes.SnmpTrap;

/// <summary>
/// Passive SNMP trap receiver. Owns its own UDP socket bound to <c>:162</c>, decodes each datagram as
/// a v1/v2c trap PDU via SharpSnmpLib, resolves the assignment whose host address matches the sender,
/// and publishes a result through the executor-provided host (which spools when NATS is down).
/// </summary>
public sealed class SnmpTrapProbePlugin : IPassiveProbePlugin, IConfigurablePlugin
{
    public const int ListenPort = 162;

    private UdpClient? _socket;
    private Task? _receiveLoop;
    private IPassiveProbeHost? _host;

    public string Name => "SnmpTrap";
    public Version Version => new(1, 0, 0);
    public string Description => "Passive UDP SNMP trap receiver — decodes v1/v2c trap PDUs via SharpSnmpLib";
    public PassiveProtocol Protocol => PassiveProtocol.Udp;
    public int Port => ListenPort;

    public ConfigManifest GetConfigManifest() => new() { Fields = [] };

    public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;

    public Task<IReadOnlyList<MetricDeclaration>> GetMetricsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MetricDeclaration> metrics =
        [
            new MetricDeclaration("trap_received", "Trap received", null,
                new Dictionary<double, string> { [0] = "Parse failed", [1] = "Received" }),
            new MetricDeclaration("variable_count", "Variable bindings", null),
            new MetricDeclaration("generic_trap", "Generic trap code"),
            new MetricDeclaration("specific_trap", "Specific trap code"),
            new MetricDeclaration("timestamp", "Agent timestamp", "ticks"),
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

    /// <summary>Decode an SNMP trap datagram into a result. Pure — no I/O, no socket state.</summary>
    internal static ProbeResult ParseDatagram(ReadOnlyMemory<byte> datagram, IPEndPoint remoteEndpoint)
    {
        var sw = Stopwatch.StartNew();

        IList<ISnmpMessage> messages;
        try
        {
            messages = MessageFactory.ParseMessages(datagram.ToArray(), new UserRegistry());
        }
        catch (Exception ex)
        {
            sw.Stop();
            var errorMetadata = new Dictionary<string, object>
            {
                ["trap_received"] = 0,
                ["error"] = ex.Message,
                ["datagram_length"] = datagram.Length,
                ["remote_endpoint"] = remoteEndpoint.ToString(),
                ["received_at"] = DateTimeOffset.UtcNow.ToString("O")
            };

            return new ProbeResult(
                MonitoringStatus.Unknown,
                $"Failed to parse SNMP trap from {remoteEndpoint}: {ex.Message}",
                DateTimeOffset.UtcNow,
                sw.Elapsed,
                errorMetadata);
        }

        if (messages.Count == 0)
        {
            sw.Stop();
            return new ProbeResult(
                MonitoringStatus.Unknown,
                $"Empty SNMP message from {remoteEndpoint}",
                DateTimeOffset.UtcNow,
                sw.Elapsed,
                new Dictionary<string, object>
                {
                    ["remote_endpoint"] = remoteEndpoint.ToString(),
                    ["received_at"] = DateTimeOffset.UtcNow.ToString("O")
                });
        }

        var msg = messages[0];
        var pdu = msg.Pdu();
        var version = msg.Version;

        var metadata = new Dictionary<string, object>
        {
            ["trap_received"] = 1,
            ["snmp_version"] = version.ToString(),
            ["community"] = msg.Parameters.UserName.ToString(),
            ["pdu_type"] = pdu.TypeCode.ToString(),
            ["remote_endpoint"] = remoteEndpoint.ToString(),
            ["received_at"] = DateTimeOffset.UtcNow.ToString("O")
        };

        if (pdu is TrapV1Pdu trapV1)
        {
            metadata["enterprise"] = trapV1.Enterprise.ToString();
            metadata["agent_address"] = trapV1.AgentAddress.ToString();
            metadata["generic_trap"] = trapV1.Generic.ToString();
            metadata["specific_trap"] = trapV1.Specific;
            metadata["timestamp"] = trapV1.TimeStamp;
        }

        var variables = pdu.Variables;
        if (variables.Count > 0)
        {
            var varbinds = new List<Dictionary<string, string>>(variables.Count);
            foreach (var v in variables)
            {
                varbinds.Add(new Dictionary<string, string>
                {
                    ["oid"] = v.Id.ToString(),
                    ["type"] = v.Data.TypeCode.ToString(),
                    ["value"] = v.Data.ToString()
                });
            }
            metadata["variable_bindings"] = varbinds;
            metadata["variable_count"] = variables.Count;
        }

        var trapOid = pdu is TrapV1Pdu v1
            ? v1.Enterprise.ToString()
            : variables.Count > 1 ? variables[1].Data.ToString() : "unknown";

        sw.Stop();

        var summary = $"SNMP {version} trap from {remoteEndpoint}: OID {trapOid}, {variables.Count} varbind(s)";

        return new ProbeResult(
            MonitoringStatus.Healthy,
            summary,
            DateTimeOffset.UtcNow,
            sw.Elapsed,
            metadata);
    }
}
