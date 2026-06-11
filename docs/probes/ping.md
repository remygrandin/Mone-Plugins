# PingProbe

ICMP ping probe — measures round-trip latency and reachability of a host.

- **Plugin name:** `PingProbe`
- **Version:** 1.0.0
- **Kind:** Probe
- **Probe mode:** Active (polled on the assignment's schedule)
- **Instantiation:** Per target (one plugin instance per host)

## How it works

On each scheduled run the probe sends a single ICMP echo request to the target
address using .NET's `System.Net.NetworkInformation.Ping`, with the configured
timeout, payload buffer size, and TTL (the `DontFragment` flag is always set).
It records the round-trip time and the reply status, then maps the reply to a
monitoring status.

The target address comes from the host the assignment is bound to — there is no
address parameter.

> **Privileges:** sending ICMP echo requires raw-socket capability. In Docker the
> Probe Executor needs `cap_add: NET_RAW`; on a bare host the binary needs
> `setcap cap_net_raw+ep` or must run as root. Without it the probe returns
> `Unreachable` with a hint in the result metadata.

## Parameters

| Key | Display name | Type | Required | Default | Description |
|-----|--------------|------|----------|---------|-------------|
| `Timeout` | Timeout (ms) | Integer | No | `5000` | ICMP ping timeout in milliseconds. |
| `BufferSize` | Buffer Size | Integer | No | `32` | Size of the ICMP payload buffer in bytes. |
| `Ttl` | TTL | Integer | No | `128` | Time-to-live for the ICMP packet. |

## Metrics

| Key | Display name | Unit | Value mapping |
|-----|--------------|------|---------------|
| `success` | Reachable | — | `0` = Failure, `1` = Success |
| `latency_ms` | Round-trip latency | ms | — |
| `ttl` | Reply TTL | — | — |

Additional metadata captured on each result: `address`, `ip_status`,
`buffer_size`, `timeout`.

## Status mapping

| Condition | Status |
|-----------|--------|
| Reply `Success` | `Healthy` |
| Reply `TimedOut` | `Unreachable` |
| Any other reply status | `Unhealthy` |
| Platform doesn't support ping | `Unreachable` |
| Access denied (missing `CAP_NET_RAW`) | `Unreachable` (with `hint` in metadata) |
