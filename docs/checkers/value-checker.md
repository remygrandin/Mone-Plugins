# ValueChecker

Compares a probe metadata value against an expected string value, with optional
sustain conditions. Useful for non-numeric metrics such as a status string or an
SNMP trap value.

- **Plugin name:** `ValueChecker`
- **Version:** 1.2.0
- **Kind:** Checker
- **Invocation:** On probe result (runs each time a matching probe result arrives)

## How it works

When a probe result arrives, the checker reads the metadata value at `MetricKey`,
converts it to a string, and compares it to `ExpectedValue` (honouring
`CaseSensitive`). The `ComparisonMode` decides what "matching" means:

- **`Equal`** — `Healthy` when the value equals `ExpectedValue`, otherwise the
  configured `FailureStatus`.
- **`NotEqual`** — `Healthy` when the value differs from `ExpectedValue`,
  otherwise the configured `FailureStatus`.

If the metric key is missing, the checker returns no status change.

### Sustain conditions

By default a failure triggers immediately. The sustain parameters work the same
as in [ThresholdChecker](threshold-checker.md):

- **`SustainEntries`** — the check must fail on this many *consecutive* probe
  results before the failure is reported.
- **`SustainMinutes`** — the failure must persist for this many minutes before
  being reported.

When both are set, both must be satisfied. A single passing (`Healthy`) result
clears the pending failure.

## Parameters

| Key | Display name | Type | Required | Default | Description |
|-----|--------------|------|----------|---------|-------------|
| `MetricKey` | Metric Key | String | **Yes** | — | Metadata key whose value to check (e.g. `ip_status`). |
| `ExpectedValue` | Expected Value | String | **Yes** | — | The value to compare against. |
| `ComparisonMode` | Comparison Mode | Choice | No | `Equal` | `Equal` (healthy on match) or `NotEqual` (healthy on mismatch). |
| `CaseSensitive` | Case Sensitive | Boolean | No | `false` | Whether the comparison is case-sensitive. |
| `FailureStatus` | Failure Status | Choice | No | `Unhealthy` | Status returned when the condition is not met: `Degraded` or `Unhealthy`. |
| `SustainEntries` | Sustain Entries | Integer (min 0) | No | `0` | Consecutive failing results required before triggering (`0` = immediate). |
| `SustainMinutes` | Sustain Minutes | Integer (min 0) | No | `0` | Minutes the failure must persist before triggering (`0` = immediate). |

## Output

Emits a `StatusChange` carrying `Healthy` or the configured `FailureStatus`.
Returns nothing when the metric is absent.

## Example

To alert (Unhealthy) whenever a ping's `ip_status` is anything other than
`Success`:

- `MetricKey` = `ip_status`
- `ExpectedValue` = `Success`
- `ComparisonMode` = `Equal`
- `FailureStatus` = `Unhealthy`
