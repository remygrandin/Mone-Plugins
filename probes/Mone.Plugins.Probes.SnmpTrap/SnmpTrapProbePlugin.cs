using System.Diagnostics;
using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace Mone.Plugins.Probes.SnmpTrap;

[ProbePlugin(ProbeMode = ProbeMode.Passive, InstantiationMode = InstantiationMode.Batch)]
public sealed class SnmpTrapProbePlugin : IPassiveUdpPlugin, IConfigurablePlugin
{
    public ConfigManifest GetConfigManifest() => new()
    {
        Fields = []
    };
    public string Name => "SnmpTrap";
    public Version Version => new(1, 0, 0);
    public string Description => "Passive UDP SNMP trap receiver — decodes v1/v2c trap PDUs via SharpSnmpLib";
    public ProbeMode ProbeMode => ProbeMode.Passive;
    public InstantiationMode InstantiationMode => InstantiationMode.Batch;
    public int UdpPort => 162;

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

    public Task<ProbeResult> ExecuteAsync(string targetId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("SnmpTrap is a passive UDP probe — use HandleDatagramAsync instead of ExecuteAsync.");
    }

    public Task<ProbeResult> HandleDatagramAsync(
        ReadOnlyMemory<byte> datagram,
        IPEndPoint remoteEndpoint,
        CancellationToken cancellationToken)
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

            return Task.FromResult(new ProbeResult(
                MonitoringStatus.Unknown,
                $"Failed to parse SNMP trap from {remoteEndpoint}: {ex.Message}",
                DateTimeOffset.UtcNow,
                sw.Elapsed,
                errorMetadata));
        }

        if (messages.Count == 0)
        {
            sw.Stop();
            return Task.FromResult(new ProbeResult(
                MonitoringStatus.Unknown,
                $"Empty SNMP message from {remoteEndpoint}",
                DateTimeOffset.UtcNow,
                sw.Elapsed,
                new Dictionary<string, object>
                {
                    ["remote_endpoint"] = remoteEndpoint.ToString(),
                    ["received_at"] = DateTimeOffset.UtcNow.ToString("O")
                }));
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

        return Task.FromResult(new ProbeResult(
            MonitoringStatus.Healthy,
            summary,
            DateTimeOffset.UtcNow,
            sw.Elapsed,
            metadata));
    }
}
