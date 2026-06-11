# SnmpTrap

Passive UDP SNMP trap receiver — decodes SNMP v1/v2c trap PDUs using
[SharpSnmpLib](https://github.com/lextudio/sharpsnmplib).

- **Plugin name:** `SnmpTrap`
- **Version:** 1.0.0
- **Kind:** Probe (passive)
- **Probe mode:** Passive (`IPassiveUdpPlugin`)
- **Instantiation:** Batch (a single instance handles datagrams for all targets)
- **UDP port:** `162`

## How it works

The Probe Executor opens UDP port `162` and forwards each received datagram to
`HandleDatagramAsync`. The plugin parses the bytes with `MessageFactory` and
inspects the first SNMP message: it records the SNMP version, community/user
name, and PDU type, then enumerates the variable bindings (OID, type, value). For
v1 traps it additionally extracts the enterprise OID, agent address, generic and
specific trap codes, and the agent timestamp.

A successfully received trap is always reported as `Healthy` — the plugin's job is
to capture the trap, not to judge its contents. Use a downstream checker (e.g.
[ValueChecker](../checkers/value-checker.md)) to alert on specific trap values.

Being a batch passive probe, it has no schedule and `ExecuteAsync` throws.

## Parameters

This plugin has **no configurable parameters**.

## Metrics

| Key | Display name | Unit | Value mapping |
|-----|--------------|------|---------------|
| `trap_received` | Trap received | — | `0` = Parse failed, `1` = Received |
| `variable_count` | Variable bindings | — | — |
| `generic_trap` | Generic trap code | — | — |
| `specific_trap` | Specific trap code | — | — |
| `timestamp` | Agent timestamp | ticks | — |

Additional metadata: `snmp_version`, `community`, `pdu_type`, `remote_endpoint`,
`received_at`, `variable_bindings` (list of OID/type/value), and for v1 traps
`enterprise`, `agent_address`.

## Status mapping

| Condition | Status |
|-----------|--------|
| Trap parsed and received | `Healthy` |
| Datagram could not be parsed | `Unknown` |
| Empty SNMP message | `Unknown` |
