# HttpsProbe

HTTPS endpoint probe — checks the HTTP status code and the health of the target's
TLS certificate.

- **Plugin name:** `HttpsProbe`
- **Version:** 1.0.0
- **Kind:** Probe
- **Probe mode:** Active (polled on the assignment's schedule)
- **Instantiation:** Per target (one plugin instance per host)

## How it works

On each scheduled run the probe issues an HTTP `GET` to
`https://{target}{Path}` using a pooled `HttpClient`. During the TLS handshake a
certificate-validation callback captures the server certificate, so the probe can
report certificate details and days-until-expiry alongside the HTTP result.

The client is built once at initialization with the configured timeout, redirect
behaviour, and certificate-validation policy. When `ValidateCertificate` is off,
the handshake succeeds even with an untrusted/expired chain (the certificate is
still captured and reported).

The target host comes from the assignment; only the URL `Path` is configurable.

## Parameters

| Key | Display name | Type | Required | Default | Description |
|-----|--------------|------|----------|---------|-------------|
| `Timeout` | Timeout (ms) | Integer | No | `10000` | HTTP request timeout in milliseconds. |
| `ExpectedStatusCode` | Expected Status Code | Integer | No | `200` | HTTP status code considered healthy. |
| `CertExpiryWarningDays` | Cert Expiry Warning (days) | Integer | No | `30` | Days before certificate expiry at which the status drops to Degraded. |
| `FollowRedirects` | Follow Redirects | Boolean | No | `true` | Whether to follow HTTP redirects. |
| `ValidateCertificate` | Validate Certificate | Boolean | No | `true` | Whether to validate the TLS certificate chain. |
| `Path` | Path | String | No | `/` | URL path to probe. |

## Metrics

| Key | Display name | Unit | Value mapping |
|-----|--------------|------|---------------|
| `status_code` | HTTP status code | — | — |
| `response_time_ms` | Response time | ms | — |
| `cert_days_remaining` | TLS cert days remaining | d | — |
| `cert_valid` | TLS cert validity | — | `0` = Invalid, `1` = Valid |

When a certificate is captured, extra metadata is added: `cert_subject`,
`cert_issuer`, `cert_expiry`, `cert_thumbprint`.

## Status mapping

Certificate expiry is checked first, then the HTTP status code:

| Condition | Status |
|-----------|--------|
| Cert expires within `CertExpiryWarningDays` | `Degraded` |
| `2xx` or `3xx` | `Healthy` |
| `4xx` | `Degraded` |
| `5xx` (and other) | `Unhealthy` |
| Request timed out | `Unreachable` |
| Connection failed (`HttpRequestException`) | `Unreachable` |

> Note: the certificate-expiry check takes precedence — an endpoint returning
> `200` with a certificate inside the warning window reports `Degraded`.
