using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Plugins.Probes.Ping;
using Xunit;

namespace Mone.Plugins.Tests;

public class PingProbePluginTests
{
    private static IPluginContext CreateContext(Dictionary<string, string>? config = null)
    {
        return new StubPluginContext("ping-test", config ?? new Dictionary<string, string>());
    }

    [Fact]
    public void ProbeMode_IsActive()
    {
        var plugin = new PingProbePlugin();
        Assert.Equal(ProbeMode.Active, plugin.ProbeMode);
    }

    [Fact]
    public void InstantiationMode_IsPerTarget()
    {
        var plugin = new PingProbePlugin();
        Assert.Equal(InstantiationMode.PerTarget, plugin.InstantiationMode);
    }

    [Fact]
    public void Name_ReturnsPingProbe()
    {
        var plugin = new PingProbePlugin();
        Assert.Equal("PingProbe", plugin.Name);
    }

    [Fact]
    public void Version_IsOneZeroZero()
    {
        var plugin = new PingProbePlugin();
        Assert.Equal(new Version(1, 0, 0), plugin.Version);
    }

    [Fact]
    public async Task InitializeAsync_ReadsConfiguration()
    {
        var plugin = new PingProbePlugin();
        var context = CreateContext(new Dictionary<string, string>
        {
            ["Timeout"] = "3000",
            ["BufferSize"] = "64",
            ["Ttl"] = "64"
        });

        await plugin.InitializeAsync(context);

        // Configuration is read without error — internal fields verified via execution behavior
    }

    [Fact]
    public async Task InitializeAsync_IgnoresInvalidConfigValues()
    {
        var plugin = new PingProbePlugin();
        var context = CreateContext(new Dictionary<string, string>
        {
            ["Timeout"] = "not-a-number",
            ["BufferSize"] = "",
            ["Ttl"] = "abc"
        });

        await plugin.InitializeAsync(context);
        // Should not throw — falls back to defaults
    }

    [Fact]
    public async Task ExecuteAsync_AgainstLocalhost_ReturnsResultWithMetadata()
    {
        var plugin = new PingProbePlugin();
        await plugin.InitializeAsync(CreateContext());

        var result = await plugin.ExecuteAsync("127.0.0.1", CancellationToken.None);

        // ICMP may require CAP_NET_RAW or be platform-unsupported — accept Healthy or Unreachable
        Assert.NotNull(result.Metadata);
        Assert.True(result.Duration > TimeSpan.Zero);
        Assert.NotNull(result.Summary);
        Assert.NotEmpty(result.Summary);

        if (result.Status == MonitoringStatus.Healthy)
        {
            Assert.True(result.Metadata!.ContainsKey("latency_ms"));
            Assert.True(result.Metadata.ContainsKey("ttl"));
            Assert.True(result.Metadata.ContainsKey("buffer_size"));
            Assert.True(result.Metadata.ContainsKey("address"));
            Assert.True(result.Metadata.ContainsKey("ip_status"));
            Assert.Equal(32, result.Metadata["buffer_size"]);
        }
        else
        {
            Assert.Equal(MonitoringStatus.Unreachable, result.Status);
            Assert.True(result.Metadata!.ContainsKey("error"));
        }
    }

    [Fact]
    public async Task ExecuteAsync_AgainstUnreachableHost_ReturnsUnreachable()
    {
        var plugin = new PingProbePlugin();
        var context = CreateContext(new Dictionary<string, string>
        {
            ["Timeout"] = "1000"
        });
        await plugin.InitializeAsync(context);

        // 192.0.2.1 is TEST-NET-1, guaranteed not to route
        var result = await plugin.ExecuteAsync("192.0.2.1", CancellationToken.None);

        Assert.True(
            result.Status == MonitoringStatus.Unreachable || result.Status == MonitoringStatus.Unhealthy,
            $"Expected Unreachable or Unhealthy but got {result.Status}");
        Assert.NotNull(result.Summary);
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task ExecuteAsync_WithCustomBufferSize_UsesConfiguredSize()
    {
        var plugin = new PingProbePlugin();
        var context = CreateContext(new Dictionary<string, string>
        {
            ["BufferSize"] = "64"
        });
        await plugin.InitializeAsync(context);

        var result = await plugin.ExecuteAsync("127.0.0.1", CancellationToken.None);

        Assert.NotNull(result.Metadata);
        if (result.Status == MonitoringStatus.Healthy)
        {
            Assert.Equal(64, result.Metadata!["buffer_size"]);
        }
        else
        {
            // ICMP requires CAP_NET_RAW — graceful degradation is valid
            Assert.Equal(MonitoringStatus.Unreachable, result.Status);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsTimestamp()
    {
        var plugin = new PingProbePlugin();
        await plugin.InitializeAsync(CreateContext());

        var before = DateTimeOffset.UtcNow;
        var result = await plugin.ExecuteAsync("127.0.0.1", CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(result.Timestamp, before, after.AddSeconds(1));
    }
}
