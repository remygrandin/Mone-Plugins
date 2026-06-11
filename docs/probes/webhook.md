# Webhook (probe)

Passive webhook ingress probe — accepts HTTP `POST` payloads pushed by external
systems instead of polling a target.

- **Plugin name:** `Webhook`
- **Version:** 1.0.0
- **Kind:** Probe (passive)
- **Probe mode:** Passive (`IPassiveProbePlugin`)
- **Instantiation:** Per target
- **Endpoint path:** `/api/webhooks`

## How it works

This is a **passive** probe: it is never scheduled. Instead the Probe Executor
exposes the HTTP endpoint `/api/webhooks`, and when an external system POSTs a
payload there, the executor routes it to this plugin's `HandleAsync`. The plugin
measures the payload size, rejects anything over `MaxPayloadSize`, and otherwise
ingests the body (the raw payload is stored in result metadata under `payload`).

Because it is passive, calling the active `ExecuteAsync` path throws — there is
nothing to poll.

## Parameters

| Key | Display name | Type | Required | Default | Description |
|-----|--------------|------|----------|---------|-------------|
| `MaxPayloadSize` | Max Payload Size | Integer | No | `1048576` | Maximum accepted payload size in bytes (1 MiB default). |

## Metrics

| Key | Display name | Unit | Value mapping |
|-----|--------------|------|---------------|
| `accepted` | Payload accepted | — | `0` = Rejected, `1` = Accepted |
| `payload_size_bytes` | Payload size | B | — |

Additional metadata: `payload` (the raw body, when accepted),
`max_payload_size_bytes` (on rejection), `received_at`.

## Status mapping

| Condition | Status |
|-----------|--------|
| Payload within size limit | `Healthy` |
| Payload exceeds `MaxPayloadSize` | `Unhealthy` |
