# WebhookNotifier

Sends the raw status change to any configurable HTTP endpoint — a generic
integration point for systems without a dedicated notifier.

- **Plugin name:** `WebhookNotifier`
- **Version:** 1.0.0
- **Kind:** Notification

## How it works

When the Alert Engine receives a `StatusChange`, this plugin serialises the entire
`StatusChange` object as JSON and sends it to the configured `Url` using the
configured HTTP `Method`. Any custom `Headers` are added to the request. The JSON
body includes the target, previous and current status, the changed timestamp, and
the full latest probe result.

A non-2xx response is reported as a failed delivery
(`"Webhook returned HTTP <code>"`); network errors and timeouts are returned as
failures. If `Url` is unset, delivery fails with `"Missing Url configuration"`.

Unlike the Slack and Teams notifiers, the destination `Url` is **per-assignment**,
so different rules can target different endpoints.

## Parameters

| Key | Display name | Type | Required | Global | Default | Description |
|-----|--------------|------|----------|--------|---------|-------------|
| `Url` | URL | String | **Yes** | No | — | Webhook endpoint URL. |
| `Method` | HTTP Method | Choice | No | No | `POST` | One of `GET`, `POST`, `PUT`, `PATCH`. |
| `Headers` | Headers | String | No | No | — | Comma-separated `key:value` header pairs. |

The `Headers` value is parsed as a comma-separated list of `key:value` pairs
(value split on the first `:`); malformed entries are ignored.

## Body

The request body is the JSON-serialised `StatusChange`, for example:

```json
{
  "TargetId": "host-01",
  "PreviousStatus": "Healthy",
  "CurrentStatus": "Unhealthy",
  "LatestResult": {
    "Status": "Unhealthy",
    "Summary": "Ping host-01: TimedOut",
    "Timestamp": "2026-06-10T12:00:00Z",
    "Duration": "00:00:05",
    "Metadata": { "success": 0 }
  },
  "ChangedAt": "2026-06-10T12:00:00Z"
}
```

## Output

Returns `DeliveryResult(true)` when the endpoint responds with a success status,
or `DeliveryResult(false, <error>)` otherwise.
