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

[CheckerPlugin(CheckerMode = CheckerMode.Stream)]
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
    public Version Version => new(1, 1, 0);
    public string Description => "Evaluates numeric probe metrics against configurable warning/critical thresholds with optional sustain conditions";
    public CheckerMode CheckerMode => CheckerMode.Stream;

    private string? _metricKey;
    private double _warningThreshold;
    private double _criticalThreshold;
    private ComparisonMode _comparisonMode;

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
