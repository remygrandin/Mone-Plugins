# TeamsNotifier

Posts alert messages to a Microsoft Teams channel via an incoming webhook, using
an Adaptive Card.

- **Plugin name:** `TeamsNotifier`
- **Version:** 1.0.0
- **Kind:** Notification

## How it works

When the Alert Engine receives a `StatusChange`, this plugin builds an
Adaptive Card (schema version 1.4) with a bold title and a `FactSet` listing the
target, previous and current status, changed timestamp, result status, and
summary. The card is wrapped in the Teams message/attachment envelope and `POST`ed
as JSON to the configured incoming webhook URL.

A non-2xx response is reported as a failed delivery
(`"Teams webhook returned HTTP <code>"`); network errors and timeouts are
returned as failures. If the webhook URL is unset, delivery fails with
`"Missing WebhookUrl configuration"`.

## Parameters

| Key | Display name | Type | Required | Global | Default | Description |
|-----|--------------|------|----------|--------|---------|-------------|
| `WebhookUrl` | Webhook URL | Secret | **Yes** | Yes | — | Microsoft Teams incoming webhook URL. |

## Output

Returns `DeliveryResult(true)` when Teams responds with a success status, or
`DeliveryResult(false, <error>)` otherwise.
