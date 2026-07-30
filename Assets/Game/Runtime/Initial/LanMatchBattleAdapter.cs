using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Demo;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using ArknoNights.Lobby;
using ArknoNights.Match;

public sealed class LanMatchBattleSet
{
    internal LanMatchBattleSet(
        IEnumerable<BattleMatchRequest> requests,
        IEnumerable<PlayerBattleObservation> observations,
        IReadOnlyDictionary<string, string> inputHashes,
        IReadOnlyDictionary<string, string> entityOwners)
    {
        Requests = new ReadOnlyCollection<BattleMatchRequest>(
            requests.OrderBy(item => item.MatchId, StringComparer.Ordinal).ToArray());
        Observations = new ReadOnlyCollection<PlayerBattleObservation>(
            observations.OrderBy(item => item.PlayerId, StringComparer.Ordinal).ToArray());
        InputHashes = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(inputHashes, StringComparer.Ordinal));
        EntityOwners = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(entityOwners, StringComparer.Ordinal));
    }

    public IReadOnlyList<BattleMatchRequest> Requests { get; }
    public IReadOnlyList<PlayerBattleObservation> Observations { get; }
    public IReadOnlyDictionary<string, string> InputHashes { get; }
    public IReadOnlyDictionary<string, string> EntityOwners { get; }
}

public static class LanMatchBattleAdapter
{
    public const int MaximumBattleTicks = 1800;

    public static bool TryCreate(
        ScopedSnapshotPayload snapshot,
        UnitCatalog unitCatalog,
        AbilityCatalog abilityCatalog,
        IReadOnlyDictionary<string, string> sealedInputHashes,
        out LanMatchBattleSet battleSet,
        out string diagnosticCode)
    {
        battleSet = null;
        if (snapshot == null
            || snapshot.PublicState == null
            || unitCatalog == null
            || abilityCatalog == null
            || sealedInputHashes == null
            || !string.Equals(
                snapshot.PublicState.Phase,
                MatchPhase.Battle.ToString(),
                StringComparison.Ordinal)
            || snapshot.PublicState.Pairings == null
            || snapshot.PublicState.Pairings.Length < 1
            || snapshot.PublicState.Pairings.Length > 2
            || snapshot.PublicState.Seats == null)
        {
            diagnosticCode = "match.runtime.battle.snapshot.invalid";
            return false;
        }

        var requests = new List<BattleMatchRequest>();
        var observations = new Dictionary<string, PlayerBattleObservation>(
            StringComparer.Ordinal);
        var inputHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var entityOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pairing in snapshot.PublicState.Pairings
                     .OrderBy(item => item.BattleIndex))
        {
            if (pairing == null
                || !sealedInputHashes.TryGetValue(
                    pairing.BattleId ?? string.Empty,
                    out var sealedInputHash)
                || !BattleInputSha256.IsCanonicalHash(sealedInputHash))
            {
                diagnosticCode = "match.runtime.battle.sealedHash.missing";
                return false;
            }
            var home = snapshot.PublicState.Seats.SingleOrDefault(item =>
                item != null
                && string.Equals(
                    item.PlayerId,
                    pairing.HomePlayerId,
                    StringComparison.Ordinal));
            var away = snapshot.PublicState.Seats.SingleOrDefault(item =>
                item != null
                && string.Equals(
                    item.PlayerId,
                    pairing.AwayPlayerId,
                    StringComparison.Ordinal));
            if (home == null || away == null || home.Eliminated || away.Eliminated)
            {
                diagnosticCode = "match.runtime.battle.participant.invalid";
                return false;
            }

            var players = new[]
            {
                CreatePlayer(home, BattleSide.Home, entityOwners),
                CreatePlayer(away, BattleSide.Away, entityOwners)
            };
            if (!BattleInputFactory.TryCreate(
                    new BattleInputSpecification(
                        BattleInput.LocalBattleSchemaVersion,
                        pairing.BattleId,
                        MaximumBattleTicks,
                        unitCatalog.Entries.Select(item => item.Definition),
                        abilityCatalog.Abilities,
                        players),
                    out var input,
                    out var errors))
            {
                diagnosticCode = errors.Count == 0
                    ? "match.runtime.battle.input.invalid"
                    : errors[0].Code;
                return false;
            }
            requests.Add(new BattleMatchRequest(
                pairing.BattleId,
                input,
                sealedInputHash));
            inputHashes.Add(pairing.BattleId, BattleInputSha256.Compute(input));

            if (string.Equals(pairing.Kind, MatchPairingKind.Official.ToString(), StringComparison.Ordinal))
            {
                observations[pairing.HomePlayerId] = new PlayerBattleObservation(
                    pairing.HomePlayerId,
                    pairing.BattleId,
                    BattleObserverView.Home);
                observations[pairing.AwayPlayerId] = new PlayerBattleObservation(
                    pairing.AwayPlayerId,
                    pairing.BattleId,
                    BattleObserverView.Away);
            }
            else
            {
                var recipient = string.Equals(
                    pairing.HomePlayerId,
                    pairing.ShadowOwnerPlayerId,
                    StringComparison.Ordinal)
                    ? pairing.AwayPlayerId
                    : pairing.HomePlayerId;
                observations[recipient] = new PlayerBattleObservation(
                    recipient,
                    pairing.BattleId,
                    string.Equals(recipient, pairing.HomePlayerId, StringComparison.Ordinal)
                        ? BattleObserverView.Home
                        : BattleObserverView.Away);
            }
        }

        var alivePlayers = snapshot.PublicState.Seats
            .Where(item => item != null && !item.Eliminated)
            .Select(item => item.PlayerId)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        if (observations.Count != alivePlayers.Length
            || alivePlayers.Any(item => !observations.ContainsKey(item)))
        {
            diagnosticCode = "match.runtime.battle.observation.invalid";
            return false;
        }

        battleSet = new LanMatchBattleSet(
            requests,
            observations.Values,
            inputHashes,
            entityOwners);
        diagnosticCode = string.Empty;
        return true;
    }

    private static PlayerSnapshot CreatePlayer(
        PublicMatchSeatWire seat,
        BattleSide side,
        IDictionary<string, string> entityOwners)
    {
        var globalBuffs = (seat.GlobalBuffs ?? Array.Empty<MatchGlobalBuffWire>())
            .Select(item => new BuffPlaceholder(item.BuffTypeId, item.CanonicalPayload));
        var sourceEffects = (seat.SourceEffects ?? Array.Empty<MatchSourceEffectWire>())
            .Select(item => new BuffPlaceholder(item.EffectTypeId, item.CanonicalPayload));
        var units = new List<UnitSnapshot>();
        foreach (var unit in (seat.Units ?? Array.Empty<MatchUnitWire>())
                     .Where(item =>
                         item != null
                         && string.Equals(
                             item.Zone,
                             MatchUnitZone.Deployed.ToString(),
                             StringComparison.Ordinal))
                     .OrderBy(item => item.UnitId, StringComparer.Ordinal))
        {
            var targeted = (seat.TargetedUnitBuffs ?? Array.Empty<MatchTargetedBuffWire>())
                .Where(item => string.Equals(
                    item.TargetUnitId,
                    unit.UnitId,
                    StringComparison.Ordinal))
                .Select(item => new BuffPlaceholder(item.BuffTypeId, item.CanonicalPayload));
            var buffs = (unit.Buffs ?? Array.Empty<MatchBuffWire>())
                .Select(item => new BuffPlaceholder(item.BuffId, item.CanonicalPayload))
                .Concat(targeted)
                .Concat(globalBuffs)
                .Concat(sourceEffects)
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .ThenBy(item => item.RawPayload, StringComparer.Ordinal)
                .ToArray();
            var count = MatchEliteRules.GetBattleEntityCount(unit.EliteLevel);
            for (var ordinal = 0; ordinal < count; ordinal++)
            {
                var entityId = unit.UnitId + "~entity-" + ordinal;
                entityOwners[entityId] = unit.UnitId;
                units.Add(new UnitSnapshot(
                    entityId,
                    unit.TypeId,
                    UnitZone.Deployed,
                    new FormationCoordinate(unit.FormationX, unit.FormationY),
                    buffs,
                    unit.EliteLevel));
            }
        }
        return new PlayerSnapshot(seat.PlayerId, side, units);
    }
}
