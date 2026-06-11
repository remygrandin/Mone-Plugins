# SlackNotifier

Posts alert messages to a Slack channel via an
[incoming webhook](https://api.slack.com/messaging/webhooks).

- **Plugin name:** `SlackNotifier`
- **Version:** 1.0.0
- **Kind:** Notification

## How it works

When the Alert Engine receives a `StatusChange`, this plugin formats a short
multi-line message (target, previous → current status, changed timestamp, result
status, and summary) and `POST`s it as a `{ "text": ... }` JSON body to the
configured Slack incoming webhook URL.

A non-2xx response from Slack is reported as a failed delivery
(`"Slack webhook returned HTTP <code>"`); network errors and timeouts are
likewise returned as failures. If the webhook URL is unset, delivery fails with
`"Missing WebhookUrl configuration"`.

## Parameters

| Key | Display name | Type | Required | Global | Default | Description |
|-----|--------------|------|----------|--------|---------|-------------|
| `WebhookUrl` | Webhook URL | Secret | **Yes** | Yes | — | Slack incoming webhook URL. |

## Output

Returns `DeliveryResult(true)` when Slack responds with a success status, or
`DeliveryResult(false, <error>)` otherwise.
