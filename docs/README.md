# Mone Plugins

This repository holds the built-in plugins for [Mone](https://github.com/remygrandin/Mone),
the plugin-first infrastructure monitoring platform. Each plugin is a standalone
`net10.0` class library that references `Mone.Contracts` and is loaded by a Mone
service at startup from its configured plugin directory.

This page lists every plugin in the repo. Each plugin has its own page covering
how it works, its parameters, the metrics it emits, and how its results map to a
monitoring status.

## Plugin kinds

Mone has three plugin kinds, each consumed by a different service:

| Kind | Interface | Service | Role |
|------|-----------|---------|------|
| **Probe** | `IProbePlugin` / `IPassiveProbePlugin` / `IPassiveUdpPlugin` | Probe Executor | Collect data from a target — actively polling, or passively receiving pushes (webhook / UDP). Publishes a `ProbeResult` to NATS `probes.results.*`. |
| **Checker** | `ICheckerPlugin` | Checker Engine | Evaluate probe results against rules and emit a `StatusChange` on NATS `status.changes.*`. |
| **Notification** | `INotificationPlugin` | Alert Engine | Deliver an alert to an external system when a status changes. |

Data flows left to right: a **probe** produces results, a **checker** turns
results into status changes, and a **notification** plugin dispatches the alert.

## Plugins in this repo

### Probes

| Plugin | Page | Mode | Description |
|--------|------|------|-------------|
| `PingProbe` | [probes/ping.md](probes/ping.md) | Active | ICMP ping — latency and reachability. |
| `HttpsProbe` | [probes/https.md](probes/https.md) | Active | HTTPS endpoint check with TLS certificate health. |
| `Webhook` | [probes/webhook.md](probes/webhook.md) | Passive (HTTP) | Accepts inbound webhook payloads. |
| `Syslog` | [probes/syslog.md](probes/syslog.md) | Passive (UDP/514) | Receives and parses RFC 3164/5424 syslog. |
| `SnmpTrap` | [probes/snmp-trap.md](probes/snmp-trap.md) | Passive (UDP/162) | Decodes SNMP v1/v2c trap PDUs. |

### Checkers

| Plugin | Page | Invocation | Description |
|--------|------|------------|-------------|
| `ThresholdChecker` | [checkers/threshold-checker.md](checkers/threshold-checker.md) | On probe result | Compares a numeric metric against warning/critical thresholds. |
| `ValueChecker` | [checkers/value-checker.md](checkers/value-checker.md) | On probe result | Compares a metric's string value against an expected value. |

### Notifications

| Plugin | Page | Description |
|--------|------|-------------|
| `EmailNotifier` | [notifications/email.md](notifications/email.md) | Sends alert emails over SMTP (MailKit). |
| `SlackNotifier` | [notifications/slack.md](notifications/slack.md) | Posts to a Slack incoming webhook. |
| `TeamsNotifier` | [notifications/teams.md](notifications/teams.md) | Posts an Adaptive Card to a Microsoft Teams webhook. |
| `WebhookNotifier` | [notifications/webhook.md](notifications/webhook.md) | POSTs the raw status change to any HTTP endpoint. |

## How parameters work

Every configurable plugin declares its parameters through a `ConfigManifest`
returned by `GetConfigManifest()`. The dashboard renders this manifest as a form
on the probe/checker/notification assignment, so the parameter tables on each
page mirror exactly what you see when configuring an assignment.

Each parameter has these properties:

- **Key** — the configuration key (what the plugin reads at runtime).
- **Type** — `String`, `Integer`, `Double`, `Boolean`, `Choice`, or `Secret`.
  `Secret` values are masked in the UI and stored encrypted.
- **Required** — whether the assignment is invalid without it.
- **Default** — value used when the field is left blank.
- **Global** — a global field is configured once and shared across all
  assignments of the plugin; a non-global field is set per assignment (e.g. per
  host). For example, an SMTP server is global, but the recipient list is
  per-assignment.

## Statuses

Probes and checkers resolve to a `MonitoringStatus`:

| Status | Meaning |
|--------|---------|
| `Healthy` | Target is operating normally. |
| `Degraded` | Working but impaired (warning-level). |
| `Unhealthy` | Failing (critical-level). |
| `Unreachable` | Could not be contacted at all. |
| `Unknown` | Result could not be interpreted (e.g. unparseable input). |

## Writing your own plugin

These pages document the *built-in* plugins. To build a custom one, see the
[repository README](../README.md) for the project layout and build/release
pipeline, and the [Mone README](https://github.com/remygrandin/Mone#plugin-system)
for the plugin interfaces and the `Mone.Contracts` SDK.
