using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;

namespace Mone.Plugins.Probes.Webhook;

/// <summary>
/// Passive webhook ingress probe. Owns its own HTTP listener and accepts
/// <c>POST /api/webhooks/{hostId}</c>. For each request it resolves the assignment for the target,
/// verifies the optional HMAC-SHA256 signature against that assignment's <c>webhook_secret</c>, and
/// publishes a result through the executor-provided host (which spools when NATS is down).
/// </summary>
public sealed class WebhookProbePlugin : IPassiveProbePlugin, IConfigurablePlugin
{
    public const int ListenPort = 9080;
    public const long DefaultMaxPayloadSize = 1_048_576;

    private HttpListener? _listener;
    private Task? _acceptLoop;
    private IPassiveProbeHost? _host;

    public string Name => "Webhook";
    public Version Version => new(1, 0, 0);
    public string Description => "Passive webhook ingress probe — accepts HTTP POST payloads from external systems";
    public PassiveProtocol Protocol => PassiveProtocol.Tcp;
    public int Port => ListenPort;

    public ConfigManifest GetConfigManifest() => new()
    {
        Fields =
        [
            new ConfigField { Key = "MaxPayloadSize", DisplayName = "Max Payload Size", Description = "Maximum accepted webhook payload size in bytes", FieldType = ConfigFieldType.Integer, DefaultValue = "1048576", Required = false, IsGlobal = false },
            new ConfigField { Key = "webhook_secret", DisplayName = "Webhook Secret", Description = "Shared secret for HMAC-SHA256 signature validation (X-Webhook-Signature). Leave empty to accept unsigned payloads.", FieldType = ConfigFieldType.Secret, Required = false, IsGlobal = false },
        ]
    };

    public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;

    public Task<IReadOnlyList<MetricDeclaration>> GetMetricsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MetricDeclaration> metrics =
        [
            new MetricDeclaration("accepted", "Payload accepted", null,
                new Dictionary<double, string> { [0] = "Rejected", [1] = "Accepted" }),
            new MetricDeclaration("payload_size_bytes", "Payload size", "B"),
        ];
        return Task.FromResult(metrics);
    }

    public Task StartAsync(IPassiveProbeHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{ListenPort}/");
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try { _listener?.Stop(); } catch { /* already stopping */ }
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch (OperationCanceledException) { /* shutting down */ }
        }
        _listener?.Close();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        var listener = _listener!;
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (ct.IsCancellationRequested || !listener.IsListening)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }

            await HandleRequestAsync(context, ct);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            if (!string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(response, 405, new { error = "Method not allowed" });
                return;
            }

            var targetId = ExtractTargetId(request.Url);
            if (targetId is null)
            {
                await WriteJsonAsync(response, 404, new { error = "Missing target id in path" });
                return;
            }

            var assignment = _host!.Assignments.FirstOrDefault(a =>
                string.Equals(a.HostId.ToString(), targetId, StringComparison.OrdinalIgnoreCase));

            if (assignment is null)
            {
                await WriteJsonAsync(response, 404, new { error = "No webhook assignment for target" });
                return;
            }

            string payload;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
                payload = await reader.ReadToEndAsync(ct);

            if (assignment.Config.TryGetValue("webhook_secret", out var secret) && !string.IsNullOrEmpty(secret))
            {
                var signature = request.Headers["X-Webhook-Signature"];
                if (string.IsNullOrEmpty(signature))
                {
                    await WriteJsonAsync(response, 401, new { error = "Missing X-Webhook-Signature header" });
                    return;
                }

                var expected = ComputeHmacSha256(secret, payload);
                if (!CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(signature), Encoding.UTF8.GetBytes(expected)))
                {
                    await WriteJsonAsync(response, 401, new { error = "Invalid signature" });
                    return;
                }
            }

            var maxPayloadSize = DefaultMaxPayloadSize;
            if (assignment.Config.TryGetValue("MaxPayloadSize", out var maxRaw) && long.TryParse(maxRaw, out var parsed))
                maxPayloadSize = parsed;

            var result = BuildResult(targetId, payload, maxPayloadSize);
            await _host.PublishResultAsync(targetId, result, ct);

            await WriteJsonAsync(response, 200, new { status = result.Status.ToString(), summary = result.Summary });
        }
        catch (Exception)
        {
            try { await WriteJsonAsync(response, 500, new { error = "Internal error" }); }
            catch { /* client gone */ }
        }
    }

    /// <summary>Turn an accepted payload into a result. Pure — no I/O, no listener state.</summary>
    internal static ProbeResult BuildResult(string targetId, string payload, long maxPayloadSize)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var payloadSizeBytes = Encoding.UTF8.GetByteCount(payload);

        if (payloadSizeBytes > maxPayloadSize)
        {
            sw.Stop();
            var rejectMetadata = new Dictionary<string, object>
            {
                ["accepted"] = 0,
                ["payload_size_bytes"] = payloadSizeBytes,
                ["max_payload_size_bytes"] = maxPayloadSize,
                ["received_at"] = DateTimeOffset.UtcNow.ToString("O")
            };

            return new ProbeResult(
                MonitoringStatus.Unhealthy,
                $"Webhook payload rejected for {targetId}: {payloadSizeBytes} bytes exceeds limit of {maxPayloadSize}",
                DateTimeOffset.UtcNow,
                sw.Elapsed,
                rejectMetadata);
        }

        sw.Stop();

        var metadata = new Dictionary<string, object>
        {
            ["accepted"] = 1,
            ["payload"] = payload,
            ["payload_size_bytes"] = payloadSizeBytes,
            ["received_at"] = DateTimeOffset.UtcNow.ToString("O")
        };

        return new ProbeResult(
            MonitoringStatus.Healthy,
            $"Webhook received for {targetId}: {payloadSizeBytes} bytes ingested",
            DateTimeOffset.UtcNow,
            sw.Elapsed,
            metadata);
    }

    internal static string ComputeHmacSha256(string secret, string payload)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        return $"sha256={Convert.ToHexStringLower(hash)}";
    }

    private static string? ExtractTargetId(Uri? url)
    {
        if (url is null) return null;
        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? null : segments[^1];
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object body)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body));
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.OutputStream.Close();
    }
}
