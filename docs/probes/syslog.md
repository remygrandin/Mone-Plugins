# Syslog

Passive UDP syslog probe — receives syslog datagrams and parses RFC 3164 (BSD)
and RFC 5424 messages.

- **Plugin name:** `Syslog`
- **Version:** 1.0.0
- **Kind:** Probe (passive)
- **Probe mode:** Passive (`IPassiveProbePlugin`)
- **Protocol / port:** UDP `514`

## How it works

The plugin owns its own `UdpClient` bound to UDP `514`. For each datagram it
matches the sender's address against its assignments; if a host matches, it
decodes the bytes as UTF-8 and runs them through an RFC 3164/5424 parser. On
success it extracts the priority, facility, and severity (plus optional hostname,
app name, process id, message id, and structured data) and maps the severity to a
monitoring status. Unparseable datagrams produce an `Unknown` result with the raw
message retained. Results are published through the executor's spooling sink, so
they survive a NATS outage.

The executor's only responsibility is to ensure no two passive plugins bind the
same `(protocol, port)`; the plugin does all hosting and decoding itself.

Being a passive probe, it has no schedule and `ExecuteAsync` throws.

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
