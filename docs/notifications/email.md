# EmailNotifier

Sends alert emails over SMTP when a host's status changes, using
[MailKit](https://github.com/jstedfast/MailKit).

- **Plugin name:** `EmailNotifier`
- **Version:** 1.0.0
- **Kind:** Notification

## How it works

When the Alert Engine receives a `StatusChange`, it calls this plugin's
`SendAsync`. The plugin builds a plain-text email summarising the change (target,
previous → current status, timestamp, result summary, and probe duration) and
sends it via the configured SMTP server. When `UseSsl` is on it connects with
StartTLS; if an `SmtpUser` is set, it authenticates before sending.

If the required configuration (`SmtpHost`, `FromAddress`, `ToAddresses`) is
missing, the plugin reports a failed delivery with `"Missing SMTP configuration"`.
SMTP/socket errors are returned as a failed `DeliveryResult` with the error
message.

The email subject is `[Mone Alert] Host {target}: {previous} → {current}`.

## Parameters

| Key | Display name | Type | Required | Global | Default | Description |
|-----|--------------|------|----------|--------|---------|-------------|
| `SmtpHost` | SMTP Host | String | **Yes** | Yes | — | SMTP server hostname. |
| `SmtpPort` | SMTP Port | Integer | No | Yes | `587` | SMTP server port. |
| `SmtpUser` | SMTP User | String | No | Yes | — | SMTP authentication username. Auth is skipped when blank. |
| `SmtpPassword` | SMTP Password | Secret | No | Yes | — | SMTP authentication password. |
| `FromAddress` | From Address | String | **Yes** | Yes | — | Sender email address. |
| `ToAddresses` | To Addresses | String | **Yes** | No | — | Comma-separated recipient addresses. |
| `UseSsl` | Use SSL/TLS | Boolean | No | Yes | `true` | Use StartTLS for the SMTP connection. |

> **Global vs per-assignment:** the SMTP server settings are global (configured
> once), but `ToAddresses` is per-assignment so different hosts can alert
> different recipients.

## Output

Returns `DeliveryResult(true)` on a successful send, or
`DeliveryResult(false, <error>)` on missing config or an SMTP failure.
