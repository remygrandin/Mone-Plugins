using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace Mone.Plugins.Checkers.ValueChecker;

[CheckerPlugin(CheckerMode = CheckerMode.Stream)]
public sealed class ValueCheckerPlugin : ICheckerPlugin, IConfigurablePlugin
{
    public ConfigManifest GetConfigManifest() => new()
    {
        Fields =
        [
            new ConfigField { Key = "MetricKey", DisplayName = "Metric Key", Description = "Metadata key whose value to check (e.g. ip_status)", FieldType = ConfigFieldType.String, Required = true, IsGlobal = false },
            new ConfigField { Key = "ExpectedValue", DisplayName = "Expected Value", Description = "The value to compare against", FieldType = ConfigFieldType.String, Required = true, IsGlobal = false },
            new ConfigField { Key = "ComparisonMode", DisplayName = "Comparison Mode", Description = "Equal: healthy when value matches. NotEqual: healthy when value differs", FieldType = ConfigFieldType.Choice, DefaultValue = "Equal", Required = false, IsGlobal = false, Choices = ["Equal", "NotEqual"] },
            new ConfigField { Key = "CaseSensitive", DisplayName = "Case Sensitive", Description = "Whether the comparison is case-sensitive", FieldType = ConfigFieldType.Boolean, DefaultValue = "false", Required = false, IsGlobal = false },
            new ConfigField { Key = "FailureStatus", DisplayName = "Failure Status", Description = "Status to return when the condition is not met", FieldType = ConfigFieldType.Choice, DefaultValue = "Unhealthy", Required = false, IsGlobal = false, Choices = ["Degraded", "Unhealthy"] },
            new ConfigField { Key = "SustainEntries", DisplayName = "Sustain Entries", Description = "Number of consecutive results that must fail the check before triggering (0 = immediate)", FieldType = ConfigFieldType.Integer, DefaultValue = "0", Required = false, IsGlobal = false, ValidationRules = new ConfigValidationRules { Min = 0 } },
            new ConfigField { Key = "SustainMinutes", DisplayName = "Sustain Minutes", Description = "Duration in minutes the check must fail before triggering (0 = immediate)", FieldType = ConfigFieldType.Integer, DefaultValue = "0", Required = false, IsGlobal = false, ValidationRules = new ConfigValidationRules { Min = 0 } },
        ]
    };

    public string Name => "ValueChecker";
    public Version Version => new(1, 0, 0);
    public string Description => "Checks a probe metadata value for string equality against an expected value with optional sustain conditions";
    public CheckerMode CheckerMode => CheckerMode.Stream;

    private string? _metricKey;
    private string _expectedValue = "";
    private bool _equalMode = true;
    private bool _caseSensitive;
    private MonitoringStatus _failureStatus = MonitoringStatus.Unhealthy;

    public Task InitializeAsync(IPluginContext context)
    {
        var config = context.Configuration;

        _metricKey = config.TryGetValue("MetricKey", out var mk) ? mk : null;

        _expectedValue = config.TryGetValue("ExpectedValue", out var ev) ? ev : "";

        _equalMode = !config.TryGetValue("ComparisonMode", out var cm)
            || !string.Equals(cm, "NotEqual", StringComparison.OrdinalIgnoreCase);

        _caseSensitive = config.TryGetValue("CaseSensitive", out var cs)
            && string.Equals(cs, "true", StringComparison.OrdinalIgnoreCase);

        _failureStatus = config.TryGetValue("FailureStatus", out var fs)
            && string.Equals(fs, "Degraded", StringComparison.OrdinalIgnoreCase)
            ? MonitoringStatus.Degraded
            : MonitoringStatus.Unhealthy;

        return Task.CompletedTask;
    }

    public Task<StatusChange> EvaluateAsync(string targetId, ProbeResult result, CancellationToken cancellationToken)
    {
        var status = EvaluateStatus(result);

        return Task.FromResult(new StatusChange(
            targetId,
            MonitoringStatus.Unknown,
            status,
            result,
            DateTimeOffset.UtcNow));
    }

    private MonitoringStatus EvaluateStatus(ProbeResult result)
    {
        if (_metricKey is null
            || result.Metadata is null
            || !result.Metadata.TryGetValue(_metricKey, out var raw))
        {
            return _failureStatus;
        }

        var actual = raw?.ToString() ?? "";
        var comparison = _caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var matches = string.Equals(actual, _expectedValue, comparison);

        if (_equalMode)
            return matches ? MonitoringStatus.Healthy : _failureStatus;

        return matches ? _failureStatus : MonitoringStatus.Healthy;
    }
}
