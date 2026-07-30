# ADR: Operation-result-driven LAN synchronization

- Status: Accepted and implemented
- Date: 2026-07-30

## Context

The implemented `lan-match-v2` session sends an owner-scoped full snapshot after
every authoritative mutation and after preparation clock advancement. The
formal HUD consumes these replacements as its regular data source. In practice
this repeatedly resets preparation presentation: unit animation twitches,
shop purchase confirmation is cancelled by unrelated synchronization, and the
existing local deployment drag, selection diamond and retreat UI no longer
retain their interaction lifecycle.

These are not separate UI defects. They result from treating recovery
snapshots as the steady-state presentation protocol.

## Decision

The next Match protocol version keeps host authority but replaces steady-state
snapshot broadcast with an ordered stream of authoritative operation results.

A client sends only a final operation request. The host validates and executes
it through the existing single authority actor, then emits one atomic result
that also serves as the command acknowledgement. Results carry a stable
command or system action identity, host ordering, accepted/rejected status,
stable diagnostics, the resulting revision, and final absolute values for all
affected stable identities. Clients apply results idempotently; they do not
reconstruct authority from relative arithmetic.

Host-generated phase transitions, AI actions, connection/control changes,
natural refresh and income, battle seal/start, settlement, elimination and
match end use the same system-result model. A purchase result may have
recipient-specific projections: the owner receives private shop/economy data,
while other recipients receive only public unit changes.

Full owner-scoped recovery state is legal only for initial Match entry,
reconnect recovery, and explicit recovery after invalid ordering. It is not
sent after normal commands, clock advancement, AI actions or phase changes.

## Local presentation boundary

Click, hover, selection, shop confirmation, drag preview, selection diamond,
retreat UI, pending visual state and animation playback are local-only. An
unrelated network result must never clear or rebuild them.

Formation drag displays its pending target locally after release and sends one
final command. Acceptance keeps the result; rejection restores the previous
position. Pending locks are per UnitId, shop slot or button rather than global.
Shop confirmation is bound to `SlotId + UnitId` and survives unrelated
results.

Authoritative gold, shop contents, unit acquisition/fusion, formation and
ready state change only after the host result is applied.

## Liveness, time and recovery

Each connection uses one client-originated Ping and host Pong per second. Pong
carries host monotonic time, phase and phase deadline. Preparation countdown
and playback position are derived locally from the absolute host timeline;
there is no per-second full snapshot or independent PlaybackClock broadcast.
Three missed heartbeats retain the existing disconnect threshold.

On disconnect, local pending interactions are cleared. Commands without a
received result are not automatically resent. Reconnect applies one recovery
state, which determines whether an earlier command committed.

## Compatibility

This is a breaking protocol change. The result-stream implementation must use
a new Match protocol version and reject snapshot-driven clients before seat
commitment. The project will not maintain two formal synchronization modes.

## Implementation

`lan-match-v3` implements OperationResult/SystemResult, RecoveryState,
client-originated Ping/host Pong, local absolute-time playback derivation and
stable-identity HUD reconciliation. The old `CommandAck`, `ScopedSnapshot`,
`ClockSync`, standalone BattleSeal/PlaybackStart/MatchEnded and
`PlaybackClock` wire kinds are no longer part of the formal protocol.
