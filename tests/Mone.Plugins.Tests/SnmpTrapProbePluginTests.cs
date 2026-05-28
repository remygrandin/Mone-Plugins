using System.Net;
using Mone.Contracts.Models;
using Mone.Plugins.Probes.SnmpTrap;
using Xunit;

namespace Mone.Plugins.Tests;

public class SnmpTrapProbePluginTests
{
    private static readonly IPEndPoint TestEndpoint = new(IPAddress.Loopback, 162);

    // SNMPv2c Trap PDU bytes (community "public", enterprise OID 1.3.6.1.4.1.99):
    // Manually constructed BER-encoded SNMPv2c trap message
    private static readonly byte[] V2cTrapBytes =
    [
        0x30, 0x57, // SEQUENCE, length 87
        0x02, 0x01, 0x01, // INTEGER version = 1 (SNMPv2c)
        0x04, 0x06, 0x70, 0x75, 0x62, 0x6C, 0x69, 0x63, // OCTET STRING "public"
        0xA7, 0x4A, // SNMPv2-Trap-PDU, length 74
        0x02, 0x04, 0x00, 0x00, 0x00, 0x01, // INTEGER request-id = 1
        0x02, 0x01, 0x00, // INTEGER error-status = 0
        0x02, 0x01, 0x00, // INTEGER error-index = 0
        0x30, 0x3C, // SEQUENCE of varbinds, length 60
        // varbind 1: sysUpTime.0
        0x30, 0x10,
        0x06, 0x08, 0x2B, 0x06, 0x01, 0x02, 0x01, 0x01, 0x03, 0x00, // OID 1.3.6.1.2.1.1.3.0
        0x43, 0x04, 0x01, 0x7D, 0x78, 0x40, // TimeTicks
        // varbind 2: snmpTrapOID.0
        0x30, 0x14,
        0x06, 0x0A, 0x2B, 0x06, 0x01, 0x06, 0x03, 0x01, 0x01, 0x04, 0x01, 0x00, // OID 1.3.6.1.6.3.1.1.4.1.0
        0x06, 0x06, 0x2B, 0x06, 0x01, 0x04, 0x01, 0x63, // OID value 1.3.6.1.4.1.99
        // varbind 3: custom OID with string value
        0x30, 0x12,
        0x06, 0x08, 0x2B, 0x06, 0x01, 0x04, 0x01, 0x63, 0x01, 0x01, // OID 1.3.6.1.4.1.99.1.1
        0x04, 0x06, 0x73, 0x65, 0x72, 0x76, 0x65, 0x72  // OCTET STRING "server"
    ];

    [Fact]
    public void Properties_AreCorrect()
    {
        var plugin = new SnmpTrapProbePlugin();

        Assert.Equal("SnmpTrap", plugin.Name);
        Assert.Equal(new Version(1, 0, 0), plugin.Version);
        Assert.Equal(ProbeMode.Passive, plugin.ProbeMode);
        Assert.Equal(InstantiationMode.Batch, plugin.InstantiationMode);
        Assert.Equal(162, plugin.UdpPort);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsNotSupported()
    {
        var plugin = new SnmpTrapProbePlugin();
        await Assert.ThrowsAsync<NotSupportedException>(
            () => plugin.ExecuteAsync("target", CancellationToken.None));
    }

    [Fact]
    public async Task HandleDatagramAsync_V2cTrap_ReturnsHealthyWithMetadata()
    {
        var plugin = new SnmpTrapProbePlugin();
        var datagram = new ReadOnlyMemory<byte>(V2cTrapBytes);

        var result = await plugin.HandleDatagramAsync(datagram, TestEndpoint, CancellationToken.None);

        Assert.Equal(MonitoringStatus.Healthy, result.Status);
        Assert.Contains("SNMP", result.Summary);
        Assert.Contains("trap", result.Summary);
        Assert.Contains("varbind", result.Summary);
        Assert.True(result.Duration > TimeSpan.Zero);
        Assert.NotNull(result.Metadata);
        Assert.True(result.Metadata!.ContainsKey("snmp_version"));
        Assert.True(result.Metadata.ContainsKey("community"));
        Assert.True(result.Metadata.ContainsKey("pdu_type"));
        Assert.True(result.Metadata.ContainsKey("variable_bindings"));
        Assert.True(result.Metadata.ContainsKey("variable_count"));
    }

    [Fact]
    public async Task HandleDatagramAsync_V2cTrap_ExtractsVarbinds()
    {
        var plugin = new SnmpTrapProbePlugin();
        var datagram = new ReadOnlyMemory<byte>(V2cTrapBytes);

        var result = await plugin.HandleDatagramAsync(datagram, TestEndpoint, CancellationToken.None);

        Assert.NotNull(result.Metadata);

        // The V2c trap bytes may not parse correctly with SharpSnmpLib if the BER
        // encoding isn't valid — check if we got a successful parse first
        Assert.Equal(MonitoringStatus.Healthy, result.Status);
        Assert.True(result.Metadata!.ContainsKey("variable_bindings"));
        var varbinds = Assert.IsType<List<Dictionary<string, string>>>(result.Metadata["variable_bindings"]);
        Assert.NotEmpty(varbinds);

        foreach (var vb in varbinds)
        {
            Assert.True(vb.ContainsKey("oid"));
            Assert.True(vb.ContainsKey("type"));
            Assert.True(vb.ContainsKey("value"));
        }
    }

    [Fact]
    public async Task HandleDatagramAsync_MalformedData_ReturnsUnknown()
    {
        var plugin = new SnmpTrapProbePlugin();
        var datagram = new ReadOnlyMemory<byte>([0x01, 0x02, 0x03, 0xFF, 0xFE]);

        var result = await plugin.HandleDatagramAsync(datagram, TestEndpoint, CancellationToken.None);

        Assert.Equal(MonitoringStatus.Unknown, result.Status);
        Assert.Contains("Failed to parse", result.Summary);
        Assert.NotNull(result.Metadata);
        Assert.True(result.Metadata!.ContainsKey("error"));
        Assert.Equal(5, result.Metadata["datagram_length"]);
    }

    [Fact]
    public async Task HandleDatagramAsync_EmptyData_ReturnsUnknown()
    {
        var plugin = new SnmpTrapProbePlugin();
        var datagram = new ReadOnlyMemory<byte>(Array.Empty<byte>());

        var result = await plugin.HandleDatagramAsync(datagram, TestEndpoint, CancellationToken.None);

        // Empty data should either fail to parse or return unknown
        Assert.Equal(MonitoringStatus.Unknown, result.Status);
    }

    [Fact]
    public async Task HandleDatagramAsync_CommunityString_Extracted()
    {
        var plugin = new SnmpTrapProbePlugin();
        var datagram = new ReadOnlyMemory<byte>(V2cTrapBytes);

        var result = await plugin.HandleDatagramAsync(datagram, TestEndpoint, CancellationToken.None);

        Assert.NotNull(result.Metadata);
        Assert.Equal("public", result.Metadata!["community"]);
    }

    [Fact]
    public async Task HandleDatagramAsync_IncludesRemoteEndpoint()
    {
        var plugin = new SnmpTrapProbePlugin();
        var datagram = new ReadOnlyMemory<byte>(V2cTrapBytes);

        var result = await plugin.HandleDatagramAsync(datagram, TestEndpoint, CancellationToken.None);

        Assert.NotNull(result.Metadata);
        Assert.Equal(TestEndpoint.ToString(), result.Metadata!["remote_endpoint"]);
    }
}
