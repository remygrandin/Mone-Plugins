using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Plugins.Probes.Webhook;
using Xunit;

namespace Mone.Plugins.Tests;

public class WebhookProbePluginTests
{
    private const long DefaultMax = WebhookProbePlugin.DefaultMaxPayloadSize;

    [Fact]
    public void Protocol_IsTcp()
    {
        var plugin = new WebhookProbePlugin();
        Assert.Equal(PassiveProtocol.Tcp, plugin.Protocol);
    }

    [Fact]
    public void Port_IsListenPort()
    {
        var plugin = new WebhookProbePlugin();
        Assert.Equal(WebhookProbePlugin.ListenPort, plugin.Port);
    }

    [Fact]
    public void Name_ReturnsWebhook()
    {
        var plugin = new WebhookProbePlugin();
        Assert.Equal("Webhook", plugin.Name);
    }

    [Fact]
    public void Version_IsOneZeroZero()
    {
        var plugin = new WebhookProbePlugin();
        Assert.Equal(new Version(1, 0, 0), plugin.Version);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsNotSupportedException()
    {
        IProbePlugin plugin = new WebhookProbePlugin();
        await Assert.ThrowsAsync<NotSupportedException>(
            () => plugin.ExecuteAsync("target-1", CancellationToken.None));
    }

    [Fact]
    public void BuildResult_ReturnsHealthyWithMetadata()
    {
        var payload = """{"event":"deploy","status":"success"}""";
        var result = WebhookProbePlugin.BuildResult("target-1", payload, DefaultMax);

        Assert.Equal(MonitoringStatus.Healthy, result.Status);
        Assert.NotNull(result.Metadata);
        Assert.True(result.Metadata!.ContainsKey("payload"));
        Assert.True(result.Metadata.ContainsKey("payload_size_bytes"));
        Assert.True(result.Metadata.ContainsKey("received_at"));
        Assert.Equal(payload, result.Metadata["payload"]);
        Assert.Contains("target-1", result.Summary);
    }

    [Fact]
    public void BuildResult_CalculatesPayloadSizeCorrectly()
    {
        var result = WebhookProbePlugin.BuildResult("target-1", "hello", DefaultMax);
        Assert.Equal(5, result.Metadata!["payload_size_bytes"]);
    }

    [Fact]
    public void BuildResult_WithMultibyteCharacters_CalculatesByteCount()
    {
        // UTF-8: each char is 2 bytes
        var payload = "ééé";
        var expectedBytes = System.Text.Encoding.UTF8.GetByteCount(payload);
        var result = WebhookProbePlugin.BuildResult("target-1", payload, DefaultMax);

        Assert.Equal(expectedBytes, result.Metadata!["payload_size_bytes"]);
    }

    [Fact]
    public void BuildResult_ReturnsTimestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var result = WebhookProbePlugin.BuildResult("target-1", "{}", DefaultMax);
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(result.Timestamp, before, after.AddSeconds(1));
    }

    [Fact]
    public void BuildResult_ReturnsDuration()
    {
        var result = WebhookProbePlugin.BuildResult("target-1", "{}", DefaultMax);
        Assert.True(result.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public void BuildResult_OversizedPayload_ReturnsUnhealthy()
    {
        var payload = new string('x', 20);
        var result = WebhookProbePlugin.BuildResult("target-1", payload, 10);

        Assert.Equal(MonitoringStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Metadata);
        Assert.True(result.Metadata!.ContainsKey("payload_size_bytes"));
        Assert.True(result.Metadata.ContainsKey("max_payload_size_bytes"));
        Assert.Equal(10L, result.Metadata["max_payload_size_bytes"]);
        Assert.Contains("rejected", result.Summary);
    }

    [Fact]
    public void BuildResult_EmptyPayload_ReturnsHealthy()
    {
        var result = WebhookProbePlugin.BuildResult("target-1", "", DefaultMax);

        Assert.Equal(MonitoringStatus.Healthy, result.Status);
        Assert.Equal(0, result.Metadata!["payload_size_bytes"]);
    }

    [Fact]
    public void ComputeHmacSha256_ProducesStablePrefixedHex()
    {
        var signature = WebhookProbePlugin.ComputeHmacSha256("secret", "payload");

        Assert.StartsWith("sha256=", signature);
        // Deterministic for the same secret/payload
        Assert.Equal(signature, WebhookProbePlugin.ComputeHmacSha256("secret", "payload"));
        Assert.NotEqual(signature, WebhookProbePlugin.ComputeHmacSha256("secret", "other"));
    }
}
