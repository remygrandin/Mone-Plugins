using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace Mone.Plugins.Probes.Https;

[ProbePlugin(ProbeMode = ProbeMode.Active, InstantiationMode = InstantiationMode.PerTarget)]
public sealed class HttpsProbePlugin : IProbePlugin, IConfigurablePlugin
{
    public ConfigManifest GetConfigManifest() => new()
    {
        Fields =
        [
            new ConfigField { Key = "Timeout", DisplayName = "Timeout (ms)", Description = "HTTP request timeout in milliseconds", FieldType = ConfigFieldType.Integer, DefaultValue = "10000", Required = false, IsGlobal = false },
            new ConfigField { Key = "ExpectedStatusCode", DisplayName = "Expected Status Code", Description = "HTTP status code to consider healthy", FieldType = ConfigFieldType.Integer, DefaultValue = "200", Required = false, IsGlobal = false },
            new ConfigField { Key = "CertExpiryWarningDays", DisplayName = "Cert Expiry Warning (days)", Description = "Days before certificate expiry to trigger a warning", FieldType = ConfigFieldType.Integer, DefaultValue = "30", Required = false, IsGlobal = false },
            new ConfigField { Key = "FollowRedirects", DisplayName = "Follow Redirects", Description = "Whether to follow HTTP redirects", FieldType = ConfigFieldType.Boolean, DefaultValue = "true", Required = false, IsGlobal = false },
            new ConfigField { Key = "ValidateCertificate", DisplayName = "Validate Certificate", Description = "Whether to validate TLS certificate chain", FieldType = ConfigFieldType.Boolean, DefaultValue = "true", Required = false, IsGlobal = false },
            new ConfigField { Key = "Path", DisplayName = "Path", Description = "URL path to probe", FieldType = ConfigFieldType.String, DefaultValue = "/", Required = false, IsGlobal = false },
        ]
    };
    private int _timeoutMs = 10000;
    private int _expectedStatusCode = 200;
    private int _certExpiryWarningDays = 30;
    private bool _followRedirects = true;
    private bool _validateCertificate = true;
    private string _path = "/";
    private HttpClient _httpClient = null!;
    private X509Certificate2? _capturedCert;

    public string Name => "HttpsProbe";
    public Version Version => new(1, 0, 0);
    public string Description => "HTTPS endpoint probe — checks HTTP status and TLS certificate health";
    public ProbeMode ProbeMode => ProbeMode.Active;
    public InstantiationMode InstantiationMode => InstantiationMode.PerTarget;

    public Task InitializeAsync(IPluginContext context)
    {
        if (context.Configuration.TryGetValue("Timeout", out var timeout) && int.TryParse(timeout, out var t))
            _timeoutMs = t;
        if (context.Configuration.TryGetValue("ExpectedStatusCode", out var code) && int.TryParse(code, out var c))
            _expectedStatusCode = c;
        if (context.Configuration.TryGetValue("CertExpiryWarningDays", out var days) && int.TryParse(days, out var d))
            _certExpiryWarningDays = d;
        if (context.Configuration.TryGetValue("FollowRedirects", out var redir) && bool.TryParse(redir, out var r))
            _followRedirects = r;
        if (context.Configuration.TryGetValue("ValidateCertificate", out var valCert) && bool.TryParse(valCert, out var v))
            _validateCertificate = v;
        if (context.Configuration.TryGetValue("Path", out var path) && !string.IsNullOrWhiteSpace(path))
            _path = path;

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = _followRedirects,
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = CaptureAndValidateCert
            }
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(_timeoutMs)
        };

        return Task.CompletedTask;
    }

    private bool CaptureAndValidateCert(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (certificate is not null)
            _capturedCert = new X509Certificate2(certificate);

        return !_validateCertificate || sslPolicyErrors == SslPolicyErrors.None;
    }

    public Task<IReadOnlyList<MetricDeclaration>> GetMetricsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MetricDeclaration> metrics =
        [
            new MetricDeclaration("status_code", "HTTP status code"),
            new MetricDeclaration("response_time_ms", "Response time", "ms"),
            new MetricDeclaration("cert_days_remaining", "TLS cert days remaining", "d"),
            new MetricDeclaration("cert_valid", "TLS cert validity", null,
                new Dictionary<double, string> { [0] = "Invalid", [1] = "Valid" }),
        ];
        return Task.FromResult(metrics);
    }

    public async Task<ProbeResult> ExecuteAsync(string targetId, CancellationToken cancellationToken)
    {
        _capturedCert = null;
        var sw = Stopwatch.StartNew();

        try
        {
            var url = $"https://{targetId}{_path}";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            sw.Stop();

            var statusCode = (int)response.StatusCode;
            var metadata = new Dictionary<string, object>
            {
                ["status_code"] = statusCode,
                ["response_time_ms"] = sw.Elapsed.TotalMilliseconds
            };

            if (_capturedCert is not null)
            {
                var daysRemaining = (_capturedCert.NotAfter - DateTime.UtcNow).Days;
                metadata["cert_subject"] = _capturedCert.Subject;
                metadata["cert_issuer"] = _capturedCert.Issuer;
                metadata["cert_expiry"] = _capturedCert.NotAfter.ToString("O");
                metadata["cert_days_remaining"] = daysRemaining;
                metadata["cert_thumbprint"] = _capturedCert.Thumbprint;
                metadata["cert_valid"] = daysRemaining > 0 ? 1 : 0;
            }

            var certDaysRemaining = _capturedCert is not null
                ? (_capturedCert.NotAfter - DateTime.UtcNow).Days
                : int.MaxValue;

            var monitoringStatus = DetermineStatus(statusCode, certDaysRemaining);
            var summary = BuildSummary(targetId, statusCode, monitoringStatus, certDaysRemaining);

            return new ProbeResult(monitoringStatus, summary, DateTimeOffset.UtcNow, sw.Elapsed, metadata);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return new ProbeResult(
                MonitoringStatus.Unreachable,
                $"HTTPS request to {targetId} timed out after {_timeoutMs}ms",
                DateTimeOffset.UtcNow,
                sw.Elapsed,
                new Dictionary<string, object> { ["error"] = "timeout", ["timeout_ms"] = _timeoutMs });
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return new ProbeResult(
                MonitoringStatus.Unreachable,
                $"HTTPS connection to {targetId} failed: {ex.Message}",
                DateTimeOffset.UtcNow,
                sw.Elapsed,
                new Dictionary<string, object> { ["error"] = ex.Message });
        }
    }

    private MonitoringStatus DetermineStatus(int statusCode, int certDaysRemaining)
    {
        if (certDaysRemaining <= _certExpiryWarningDays)
            return MonitoringStatus.Degraded;

        return statusCode switch
        {
            >= 200 and < 300 => MonitoringStatus.Healthy,
            >= 300 and < 400 => MonitoringStatus.Healthy,
            >= 400 and < 500 => MonitoringStatus.Degraded,
            _ => MonitoringStatus.Unhealthy
        };
    }

    private static string BuildSummary(string targetId, int statusCode, MonitoringStatus status, int certDaysRemaining)
    {
        var certInfo = certDaysRemaining < int.MaxValue
            ? $", cert expires in {certDaysRemaining}d"
            : "";
        return $"HTTPS {targetId}: HTTP {statusCode} → {status}{certInfo}";
    }
}
