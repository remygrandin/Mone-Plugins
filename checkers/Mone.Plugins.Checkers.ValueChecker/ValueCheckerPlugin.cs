using System.Collections.Concurrent;
using System.Globalization;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace Mone.Plugins.Checkers.ValueChecker;

[CheckerPlugin(InvocationMode = CheckerInvocationMode.OnProbeResult)]
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
    public Version Version => new(1, 2, 0);
    public string Description => "Checks a probe metadata value for string equality against an expected value with optional sustain conditions";
    public CheckerInvocationMode InvocationMode => CheckerInvocationMode.OnProbeResult;
    public TimeSpan? Interval => null;

    private string? _metricKey;
    private string _expectedValue = "";
    private bool _equalMode = true;
    private bool _caseSensitive;
    private MonitoringStatus _failureStatus = MonitoringStatus.Unhealthy;
    private int _sustainEntries;
    private int _sustainMinutes;

    private readonly ConcurrentDictionary<string, PendingBreach> _pending = new();

    private sealed record PendingBreach(MonitoringStatus Status, int ConsecutiveCount, DateTimeOffset FirstSeen);

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

        _sustainEntries = config.TryGetValue("SustainEntries", out var se)
            && int.TryParse(se, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seVal)
            ? Math.Max(0, seVal)
            : 0;

        _sustainMinutes = config.TryGetValue("SustainMinutes", out var sm)
            && int.TryParse(sm, NumberStyles.Integer, CultureInfo.InvariantCulture, out var smVal)
            ? Math.Max(0, smVal)
            : 0;

        return Task.CompletedTask;
    }

    public async Task<StatusChange?> EvaluateAsync(CheckerEvaluationContext context)
    {
        var triggering = context.TriggeringResult
            ?? throw new InvalidOperationException(
                "ValueChecker requires a triggering probe result (OnProbeResult mode)");

        if (EvaluateStatus(triggering) is not { } evaluated)
            return null;

        var effective = await ApplySustainAsync(context, triggering, evaluated);

        return new StatusChange(
            context.TargetId,
            MonitoringStatus.Unknown,
            effective,
            triggering,
            DateTimeOffset.UtcNow);
    }

    private async Task<MonitoringStatus> ApplySustainAsync(
        CheckerEvaluationContext context,
        ProbeResult triggering,
        MonitoringStatus evaluated)
    {
        if (_sustainEntries <= 0 && _sustainMinutes <= 0)
            return evaluated;

        if (evaluated == MonitoringStatus.Healthy)
        {
            _pending.TryRemove(context.TargetId, out _);
            return evaluated;
        }

        if (_sustainEntries > 1)
        {
            var history = await context.History.GetRecentAsync(
                context.TargetId,
                _sustainEntries - 1,
                context.TriggeringProbeId,
                context.CancellationToken);

            var consecutive = 1;
            foreach (var record in history)
            {
                if (EvaluateStatus(record.Result) == evaluated)
                    consecutive++;
                else
                    break;
            }

            if (consecutive < _sustainEntries)
            {
                Track(context.TargetId, evaluated, triggering.Timestamp);
                return MonitoringStatus.Healthy;
            }
        }

        if (_sustainMinutes > 0)
        {
            var pending = Track(context.TargetId, evaluated, triggering.Timestamp);
            var elapsed = (triggering.Timestamp - pending.FirstSeen).TotalMinutes;
            if (elapsed < _sustainMinutes)
                return MonitoringStatus.Healthy;
        }

        _pending.TryRemove(context.TargetId, out _);
        return evaluated;
    }

    private PendingBreach Track(string targetId, MonitoringStatus status, DateTimeOffset timestamp)
    {
        return _pending.AddOrUpdate(
            targetId,
            _ => new PendingBreach(status, 1, timestamp),
            (_, existing) => existing.Status == status
                ? existing with { ConsecutiveCount = existing.ConsecutiveCount + 1 }
                : new PendingBreach(status, 1, timestamp));
    }

    private MonitoringStatus? EvaluateStatus(ProbeResult result)
    {
        if (_metricKey is null
            || result.Metadata is null
            || !result.Metadata.TryGetValue(_metricKey, out var raw))
        {
            return null;
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
