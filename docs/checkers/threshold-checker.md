# ThresholdChecker

Evaluates a numeric probe metric against configurable warning/critical thresholds,
with optional sustain conditions to suppress transient breaches.

- **Plugin name:** `ThresholdChecker`
- **Version:** 1.3.0
- **Kind:** Checker
- **Invocation:** On probe result (runs each time a matching probe result arrives)

## How it works

When a probe result arrives, the checker reads the metadata value at `MetricKey`,
parses it as a number, and compares it to the warning and critical thresholds
according to `ComparisonMode`. The comparison yields `Healthy`, `Degraded`
(warning breached), or `Unhealthy` (critical breached). If the metric key is
missing or not numeric, the checker returns no status change.

The thresholds are interpreted relative to the comparison mode:

| Mode | Degraded when | Unhealthy when |
|------|---------------|----------------|
| `GreaterThan` | value ≥ warning | value ≥ critical |
| `LessThan` | value ≤ warning | value ≤ critical |
| `Equal` | value = warning | value = critical |
| `NotEqual` | value ≠ warning | — (degraded only) |

### Sustain conditions

By default a breach triggers immediately. The two sustain parameters require a
breach to persist before it is reported, which dampens flapping:

- **`SustainEntries`** — the metric must breach on this many *consecutive* probe
  results (using probe history) before the status is reported. Until reached, the
  checker reports `Healthy`.
- **`SustainMinutes`** — the breach must persist for this many minutes (measured
  from when it was first seen) before being reported.

When both are set, both must be satisfied. A single `Healthy` result clears the
pending breach and resets the counters.

## Parameters

| Key | Display name | Type | Required | Default | Description |
|-----|--------------|------|----------|---------|-------------|
| `MetricKey` | Metric Key | String | **Yes** | — | Metadata key to evaluate against the thresholds. |
| `WarningThreshold` | Warning Threshold | Double | No | `0` | Value at which the status becomes Degraded. |
| `CriticalThreshold` | Critical Threshold | Double | No | `0` | Value at which the status becomes Unhealthy. |
| `ComparisonMode` | Comparison Mode | Choice | No | `GreaterThan` | One of `GreaterThan`, `LessThan`, `Equal`, `NotEqual`. |
| `SustainEntries` | Sustain Entries | Integer (min 0) | No | `0` | Consecutive breaching results required before triggering (`0` = immediate). |
| `SustainMinutes` | Sustain Minutes | Integer (min 0) | No | `0` | Minutes the breach must persist before triggering (`0` = immediate). |

## Output

Emits a `StatusChange` carrying the evaluated status (`Healthy`, `Degraded`, or
`Unhealthy`). Returns nothing when the metric is absent or non-numeric.

## Example

To alert when ping latency exceeds 200 ms (warning) / 500 ms (critical), sustained
for 3 consecutive results:

- `MetricKey` = `latency_ms`
- `ComparisonMode` = `GreaterThan`
- `WarningThreshold` = `200`
- `CriticalThreshold` = `500`
- `SustainEntries` = `3`
