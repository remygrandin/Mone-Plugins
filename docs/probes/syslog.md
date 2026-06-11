# Syslog

Passive UDP syslog probe — receives syslog datagrams and parses RFC 3164 (BSD)
and RFC 5424 messages.

- **Plugin name:** `Syslog`
- **Version:** 1.0.0
- **Kind:** Probe (passive)
- **Probe mode:** Passive (`IPassiveUdpPlugin`)
- **Instantiation:** Batch (a single instance handles datagrams for all targets)
- **UDP port:** `514`

## How it works

The Probe Executor opens UDP port `514` and forwards each received datagram to
this plugin's `HandleDatagramAsync`. The plugin decodes the bytes as UTF-8 and
runs them through an RFC 3164/5424 parser. On success it extracts the priority,
facility, and severity (plus optional hostname, app name, process id, message id,
and structured data) and maps the severity to a monitoring status. Unparseable
datagrams produce an `Unknown` result with the raw message retained.

Being a batch passive probe, it has no schedule and `ExecuteAsync` throws.

## Parameters

This plugin has **no configurable parameters**.

## Metrics

| Key | Display name | Unit | Value mapping |
|-----|--------------|------|---------------|
| `message_received` | Message received | — | `0` = Parse failed, `1` = Received |
| `priority` | Syslog priority | — | — |
| `severity_numeric` | Severity (numeric) | — | `0` Emergency … `7` Debug (see below) |

Severity numbering: `0` Emergency, `1` Alert, `2` Critical, `3` Error,
`4` Warning, `5` Notice, `6` Informational, `7` Debug.

Additional metadata when parsed: `format`, `facility`, `severity`,
`remote_endpoint`, `received_at`, and (when present) `hostname`, `app_name`,
`process_id`, `msg_id`, `structured_data`, `message`.

## Status mapping

The status is derived from the syslog **severity**:

| Severity | Status |
|----------|--------|
| Emergency, Alert, Critical | `Unhealthy` |
| Error, Warning | `Degraded` |
| Notice, Informational, Debug | `Healthy` |
| (message could not be parsed) | `Unknown` |
