# Webhook (probe)

Passive webhook ingress probe — accepts HTTP `POST` payloads pushed by external
systems instead of polling a target.

- **Plugin name:** `Webhook`
- **Version:** 1.0.0
- **Kind:** Probe (passive)
- **Probe mode:** Passive (`IPassiveProbePlugin`)
- **Protocol / port:** TCP `9080`
- **Endpoint path:** `POST /api/webhooks/{hostId}`

## How it works

This is a **passive** probe: it is never scheduled. The plugin owns its own
`HttpListener` bound to TCP `9080` and accepts `POST /api/webhooks/{hostId}`. For
each request it resolves the assignment whose host id matches the path, optionally
verifies the HMAC-SHA256 signature in `X-Webhook-Signature` against that
assignment's `webhook_secret`, measures the payload size, rejects anything over
`MaxPayloadSize`, and otherwise ingests the body (the raw payload is stored in
result metadata under `payload`). Results are published through the executor's
spooling sink, so they survive a NATS outage.

The executor's only responsibility is to ensure no two passive plugins bind the
same `(protocol, port)`; the plugin does all hosting, decoding, and auth itself.

Because it is passive, calling the active `ExecuteAsync` path throws — there is
nothing to poll.

## Parameters

| Key | Display name | Type | Required | Default | Description |
|-----|--------------|------|----------|---------|-------------|
| `MaxPayloadSize` | Max Payload Size | Integer | No | `1048576` | Maximum accepted payload size in bytes (1 MiB default). |
| `webhook_secret` | Webhook Secret | Secret | No | — | Shared secret for HMAC-SHA256 signature validation (`X-Webhook-Signature`). Leave empty to accept unsigned payloads. |

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
