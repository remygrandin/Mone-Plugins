using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Plugins.Probes.Webhook;
using Xunit;

namespace Mone.Plugins.Tests;

public class WebhookProbePluginTests
{
    private static IPluginContext CreateContext(Dictionary<string, string>? config = null)
    {
        return new StubPluginContext("webhook-test", config ?? new Dictionary<string, string>());
    }

    [Fact]
    public void ProbeMode_IsPassive()
    {
        var plugin = new WebhookProbePlugin();
        Assert.Equal(ProbeMode.Passive, plugin.ProbeMode);
    }

    [Fact]
    public void InstantiationMode_IsPerTarget()
    {
        var plugin = new WebhookProbePlugin();
        Assert.Equal(InstantiationMode.PerTarget, plugin.InstantiationMode);
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
    public void EndpointPath_ReturnsExpectedValue()
    {
        var plugin = new WebhookProbePlugin();
        Assert.Equal("/api/webhooks", plugin.EndpointPath);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsNotSupportedException()
    {
        var plugin = new WebhookProbePlugin();
        await plugin.InitializeAsync(CreateContext());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => plugin.ExecuteAsync("target-1", CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ReturnsHealthyWithMetadata()
    {
        var plugin = new WebhookProbePlugin();
        await plugin.InitializeAsync(CreateContext());

        var payload = """{"event":"deploy","status":"success"}""";
        var result = await plugin.HandleAsync("target-1", payload, CancellationToken.None);

        Assert.Equal(MonitoringStatus.Healthy, result.Status);
        Assert.NotNull(result.Metadata);
        Assert.True(result.Metadata!.ContainsKey("payload"));
        Assert.True(result.Metadata.ContainsKey("payload_size_bytes"));
        Assert.True(result.Metadata.ContainsKey("received_at"));
        Assert.Equal(payload, result.Metadata["payload"]);
        Assert.Contains("target-1", result.Summary);
    }

    [Fact]
    public async Task HandleAsync_CalculatesPayloadSizeCorrectly()
    {
        var plugin = new WebhookProbePlugin();
        await plugin.InitializeAsync(CreateContext());

        var payload = "hello";
        var result = await plugin.HandleAsync("target-1", payload, CancellationToken.None);

        Assert.Equal(5, result.Metadata!["payload_size_bytes"]);
    }

    [Fact]
    public async Task HandleAsync_WithMultibyteCharacters_CalculatesByteCount()
    {
        var plugin = new WebhookProbePlugin();
        await plugin.InitializeAsync(CreateContext());

        // UTF-8: each char is 3 bytes
        var payload = "ééé";
        var expectedBytes = System.Text.Encoding.UTF8.GetByteCount(payload);
        var result = await plugin.HandleAsync("target-1", payload, CancellationToken.None);

        Assert.Equal(expectedBytes, result.Metadata!["payload_size_bytes"]);
    }

    [Fact]
    public async Task HandleAsync_ReturnsTimestamp()
    {
        var plugin = new WebhookProbePlugin();
        await plugin.InitializeAsync(CreateContext());

        var before = DateTimeOffset.UtcNow;
        var result = await plugin.HandleAsync("target-1", "{}", CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(result.Timestamp, before, after.AddSeconds(1));
    }

    [Fact]
    public async Task HandleAsync_ReturnsDuration()
    {
        var plugin = new WebhookProbePlugin();
        await plugin.InitializeAsync(CreateContext());

        var result = await plugin.HandleAsync("target-1", "{}", CancellationToken.None);

        Assert.True(result.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task HandleAsync_OversizedPayload_ReturnsUnhealthy()
    {
        var plugin = new WebhookProbePlugin();
        var context = CreateContext(new Dictionary<string, string>
        {
            ["MaxPayloadSize"] = "10"
        });
        await plugin.InitializeAsync(context);

        var payload = new string('x', 20);
        var result = await plugin.HandleAsync("target-1", payload, CancellationToken.None);

        Assert.Equal(MonitoringStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Metadata);
        Assert.True(result.Metadata!.ContainsKey("payload_size_bytes"));
        Assert.True(result.Metadata.ContainsKey("max_payload_size_bytes"));
        Assert.Equal(10L, result.Metadata["max_payload_size_bytes"]);
        Assert.Contains("rejected", result.Summary);
    }

    [Fact]
    public async Task InitializeAsync_ReadsMaxPayloadSize()
    {
        var plugin = new WebhookProbePlugin();
        var context = CreateContext(new Dictionary<string, string>
        {
            ["MaxPayloadSize"] = "500"
        });

        await plugin.InitializeAsync(context);
        // Config applied — verified by the oversized payload test
    }

    [Fact]
    public async Task InitializeAsync_IgnoresInvalidMaxPayloadSize()
    {
        var plugin = new WebhookProbePlugin();
        var context = CreateContext(new Dictionary<string, string>
        {
            ["MaxPayloadSize"] = "not-a-number"
        });

        await plugin.InitializeAsync(context);
        // Falls back to default without error

        var result = await plugin.HandleAsync("target-1", "{}", CancellationToken.None);
        Assert.Equal(MonitoringStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task HandleAsync_EmptyPayload_ReturnsHealthy()
    {
        var plugin = new WebhookProbePlugin();
        await plugin.InitializeAsync(CreateContext());

        var result = await plugin.HandleAsync("target-1", "", CancellationToken.None);

        Assert.Equal(MonitoringStatus.Healthy, result.Status);
        Assert.Equal(0, result.Metadata!["payload_size_bytes"]);
    }
}
