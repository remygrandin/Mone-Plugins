using System.Collections.Concurrent;
using System.Globalization;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace Mone.Plugins.Checkers.ThresholdChecker;

public enum ComparisonMode
{
    GreaterThan,
    LessThan,
    Equal,
    NotEqual
}

[CheckerPlugin(InvocationMode = CheckerInvocationMode.OnProbeResult)]
public sealed class ThresholdCheckerPlugin : ICheckerPlugin, IConfigurablePlugin
{
    public ConfigManifest GetConfigManifest() => new()
    {
        Fields =
        [
            new ConfigField { Key = "MetricKey", DisplayName = "Metric Key", Description = "Metadata key to evaluate against thresholds", FieldType = ConfigFieldType.String, Required = true, IsGlobal = false },
            new ConfigField { Key = "WarningThreshold", DisplayName = "Warning Threshold", Description = "Value at which status becomes Degraded", FieldType = ConfigFieldType.Double, DefaultValue = "0", Required = false, IsGlobal = false },
            new ConfigField { Key = "CriticalThreshold", DisplayName = "Critical Threshold", Description = "Value at which status becomes Unhealthy", FieldType = ConfigFieldType.Double, DefaultValue = "0", Required = false, IsGlobal = false },
            new ConfigField { Key = "ComparisonMode", DisplayName = "Comparison Mode", Description = "How to compare the metric value against thresholds", FieldType = ConfigFieldType.Choice, DefaultValue = "GreaterThan", Required = false, IsGlobal = false, Choices = ["GreaterThan", "LessThan", "Equal", "NotEqual"] },
            new ConfigField { Key = "SustainEntries", DisplayName = "Sustain Entries", Description = "Number of consecutive results that must breach the threshold before triggering (0 = immediate)", FieldType = ConfigFieldType.Integer, DefaultValue = "0", Required = false, IsGlobal = false, ValidationRules = new ConfigValidationRules { Min = 0 } },
            new ConfigField { Key = "SustainMinutes", DisplayName = "Sustain Minutes", Description = "Duration in minutes the threshold must be breached before triggering (0 = immediate)", FieldType = ConfigFieldType.Integer, DefaultValue = "0", Required = false, IsGlobal = false, ValidationRules = new ConfigValidationRules { Min = 0 } },
        ]
    };
    public string Name => "ThresholdChecker";
    public Version Version => new(1, 2, 0);
    public string Description => "Evaluates numeric probe metrics against configurable warning/critical thresholds with optional sustain conditions";
    public CheckerInvocationMode InvocationMode => CheckerInvocationMode.OnProbeResult;
    public TimeSpan? Interval => null;

    private string? _metricKey;
    private double _warningThreshold;
    private double _criticalThreshold;
    private ComparisonMode _comparisonMode;
    private int _sustainEntries;
    private int _sustainMinutes;

    private readonly ConcurrentDictionary<string, PendingBreach> _pending = new();

    private sealed record PendingBreach(MonitoringStatus Status, int ConsecutiveCount, DateTimeOffset FirstSeen);

    public Task InitializeAsync(IPluginContext context)
    {
        var config = context.Configuration;

        _metricKey = config.TryGetValue("MetricKey", out var mk) ? mk : null;

        _warningThreshold = config.TryGetValue("WarningThreshold", out var wt)
            ? double.Parse(wt, CultureInfo.InvariantCulture)
            : 0;

        _criticalThreshold = config.TryGetValue("CriticalThreshold", out var ct)
            ? double.Parse(ct, CultureInfo.InvariantCulture)
            : 0;

        _comparisonMode = config.TryGetValue("ComparisonMode", out var cm)
            && Enum.TryParse<ComparisonMode>(cm, ignoreCase: true, out var parsed)
            ? parsed
            : ComparisonMode.GreaterThan;

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

    public async Task<StatusChange> EvaluateAsync(CheckerEvaluationContext context)
    {
        var triggering = context.TriggeringResult
            ?? throw new InvalidOperationException(
                "ThresholdChecker requires a triggering probe result (OnProbeResult mode)");

        var evaluated = EvaluateStatus(triggering);
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

    private MonitoringStatus EvaluateStatus(ProbeResult result)
    {
        if (_metricKey is not null
            && result.Metadata is not null
            && result.Metadata.TryGetValue(_metricKey, out var raw)
            && TryParseDouble(raw, out var value))
        {
            return _comparisonMode switch
            {
                ComparisonMode.GreaterThan => EvaluateGreaterThan(value),
                ComparisonMode.LessThan => EvaluateLessThan(value),
                ComparisonMode.Equal => EvaluateEqual(value),
                ComparisonMode.NotEqual => EvaluateNotEqual(value),
                _ => EvaluateGreaterThan(value)
            };
        }

        return MapProbeStatus(result.Status);
    }

    private MonitoringStatus EvaluateGreaterThan(double value)
    {
        if (value >= _criticalThreshold) return MonitoringStatus.Unhealthy;
        if (value >= _warningThreshold) return MonitoringStatus.Degraded;
        return MonitoringStatus.Healthy;
    }

    private MonitoringStatus EvaluateLessThan(double value)
    {
        if (value <= _criticalThreshold) return MonitoringStatus.Unhealthy;
        if (value <= _warningThreshold) return MonitoringStatus.Degraded;
        return MonitoringStatus.Healthy;
    }

    private MonitoringStatus EvaluateEqual(double value)
    {
        if (value == _criticalThreshold) return MonitoringStatus.Unhealthy;
        if (value == _warningThreshold) return MonitoringStatus.Degraded;
        return MonitoringStatus.Healthy;
    }

    private MonitoringStatus EvaluateNotEqual(double value)
    {
        if (value != _warningThreshold) return MonitoringStatus.Degraded;
        return MonitoringStatus.Healthy;
    }

    private static MonitoringStatus MapProbeStatus(MonitoringStatus probeStatus) =>
        probeStatus switch
        {
            MonitoringStatus.Healthy => MonitoringStatus.Healthy,
            MonitoringStatus.Degraded => MonitoringStatus.Degraded,
            MonitoringStatus.Unhealthy => MonitoringStatus.Unhealthy,
            MonitoringStatus.Unreachable => MonitoringStatus.Unreachable,
            _ => MonitoringStatus.Unknown
        };

    private static bool TryParseDouble(object raw, out double value)
    {
        if (raw is double d)
        {
            value = d;
            return true;
        }

        if (raw is int i)
        {
            value = i;
            return true;
        }

        if (raw is long l)
        {
            value = l;
            return true;
        }

        if (raw is float f)
        {
            value = f;
            return true;
        }

        if (raw is string s)
        {
            return double.TryParse(s, CultureInfo.InvariantCulture, out value);
        }

        value = 0;
        return false;
    }
}
