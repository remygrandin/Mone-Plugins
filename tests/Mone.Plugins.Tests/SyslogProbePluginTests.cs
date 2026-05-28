using System.Net;
using System.Text;
using Mone.Contracts.Models;
using Mone.Plugins.Probes.Syslog;
using Xunit;

namespace Mone.Plugins.Tests;

public class SyslogProbePluginTests
{
    private static readonly IPEndPoint TestEndpoint = new(IPAddress.Loopback, 514);

    [Fact]
    public void Properties_AreCorrect()
    {
        var plugin = new SyslogProbePlugin();

        Assert.Equal("Syslog", plugin.Name);
        Assert.Equal(new Version(1, 0, 0), plugin.Version);
        Assert.Equal(ProbeMode.Passive, plugin.ProbeMode);
        Assert.Equal(InstantiationMode.Batch, plugin.InstantiationMode);
        Assert.Equal(514, plugin.UdpPort);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsNotSupported()
    {
        var plugin = new SyslogProbePlugin();
        await Assert.ThrowsAsync<NotSupportedException>(
            () => plugin.ExecuteAsync("target", CancellationToken.None));
    }

    [Fact]
    public async Task HandleDatagramAsync_ValidRfc5424_ReturnsCorrectResult()
    {
        var plugin = new SyslogProbePlugin();
        var raw = "<134>1 2025-05-24T12:00:00.000Z myhost myapp 1234 ID47 - Application started";
        var datagram = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(raw));

        var result = await plugin.HandleDatagramAsync(datagram, TestEndpoint, CancellationToken.None);

        Assert.Equal(MonitoringStatus.Healthy, result.Status);
        Assert.Contains("Syslog [Informational]", result.Summary);
        Assert.Contains("myhost", result.Summary);
        Assert.Contains("Application started", result.Summary);
        Assert.True(result.Duration > TimeSpan.Zero);
        Assert.NotNull(result.Metadata);
        Assert.Equal("Rfc5424", result.Metadata!["format"]);
        Assert.Equal("Local0", result.Metadata["facility"]);
        Assert.Equal("Informational", result.Metadata["severity"]);
        Assert.Equal("myhost", result.Metadata["hostname"]);
        Assert.Equal("myapp", result.Metadata["app_name"]);
    }

    [Fact]
    public async Task HandleDatagramAsync_ValidRfc3164_ReturnsCorrectResult()
    {
        var plugin = new SyslogProbePlugin();
        var raw = "<11>May 24 12:00:00 server1 kernel: Disk check completed";
        var datagram = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(raw));

        var result = await plugin.HandleDatagramAsync(datagram, TestEndpoint, CancellationToken.None);

        // PRI 11 = facility 1 (User), severity 3 (Error) => Degraded
        Assert.Equal(MonitoringStatus.Degraded, result.Status);
        Assert.Contains("Syslog [Error]", result.Summary);
        Assert.NotNull(result.Metadata);
        Assert.Equal("Rfc3164", result.Metadata!["format"]);
    }

    [Fact]
    public async Task HandleDatagramAsync_MalformedInput_ReturnsUnknown()
    {
        var plugin = new SyslogProbePlugin();
        var raw = "this is not a syslog message";
        var datagram = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(raw));

        var result = await plugin.HandleDatagramAsync(datagram, TestEndpoint, CancellationToken.None);

        Assert.Equal(MonitoringStatus.Unknown, result.Status);
        Assert.Contains("Failed to parse", result.Summary);
        Assert.Contains(TestEndpoint.ToString(), result.Summary);
        Assert.NotNull(result.Metadata);
        Assert.Equal(raw, result.Metadata!["raw_message"]);
    }

    [Fact]
    public async Task HandleDatagramAsync_EmptyDatagram_ReturnsUnknown()
    {
        var plugin = new SyslogProbePlugin();
        var datagram = new ReadOnlyMemory<byte>(Array.Empty<byte>());

        var result = await plugin.HandleDatagramAsync(datagram, TestEndpoint, CancellationToken.None);

        Assert.Equal(MonitoringStatus.Unknown, result.Status);
    }

    [Theory]
    [InlineData("<0>1 2025-05-24T12:00:00Z h a - - - m", MonitoringStatus.Unhealthy)]   // Emergency (0)
    [InlineData("<1>1 2025-05-24T12:00:00Z h a - - - m", MonitoringStatus.Unhealthy)]   // Alert (1)
    [InlineData("<2>1 2025-05-24T12:00:00Z h a - - - m", MonitoringStatus.Unhealthy)]   // Critical (2)
    [InlineData("<3>1 2025-05-24T12:00:00Z h a - - - m", MonitoringStatus.Degraded)]    // Error (3)
    [InlineData("<4>1 2025-05-24T12:00:00Z h a - - - m", MonitoringStatus.Degraded)]    // Warning (4)
    [InlineData("<5>1 2025-05-24T12:00:00Z h a - - - m", MonitoringStatus.Healthy)]     // Notice (5)
    [InlineData("<6>1 2025-05-24T12:00:00Z h a - - - m", MonitoringStatus.Healthy)]     // Informational (6)
    [InlineData("<7>1 2025-05-24T12:00:00Z h a - - - m", MonitoringStatus.Healthy)]     // Debug (7)
    public async Task HandleDatagramAsync_SeverityMapping(string raw, MonitoringStatus expected)
    {
        var plugin = new SyslogProbePlugin();
        var datagram = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(raw));

        var result = await plugin.HandleDatagramAsync(datagram, TestEndpoint, CancellationToken.None);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task HandleDatagramAsync_EmergencySeverity_ReturnsUnhealthy()
    {
        var plugin = new SyslogProbePlugin();
        // PRI 0 = facility 0 (Kernel), severity 0 (Emergency)
        var raw = "<0>1 2025-05-24T12:00:00Z host app - - - System panic";
        var datagram = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(raw));

        var result = await plugin.HandleDatagramAsync(datagram, TestEndpoint, CancellationToken.None);

        Assert.Equal(MonitoringStatus.Unhealthy, result.Status);
    }
}
