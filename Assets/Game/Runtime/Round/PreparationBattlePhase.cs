using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Player;

namespace ArknoNights.Round
{
    public enum LocalBattlePhase { Loading, Preparation, Battle, Error }

    /// <summary>Pure, one-shot preparation clock. Scene code supplies unscaled time and performs side effects.</summary>
    public sealed class PreparationBattlePhaseMachine
    {
        public const float PreparationDurationSeconds = 30f;

        public LocalBattlePhase Phase { get; private set; } = LocalBattlePhase.Loading;
        public float RemainingPreparationSeconds { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        public void EnterPreparation()
        {
            Phase = LocalBattlePhase.Preparation;
            RemainingPreparationSeconds = PreparationDurationSeconds;
            LastError = string.Empty;
        }

        /// <returns>true once when the preparation clock reaches zero.</returns>
        public bool Advance(float unscaledSeconds)
        {
            if (Phase != LocalBattlePhase.Preparation) return false;
            RemainingPreparationSeconds = Math.Max(0f, RemainingPreparationSeconds - Math.Max(0f, unscaledSeconds));
            if (RemainingPreparationSeconds > 0f) return false;
            Phase = LocalBattlePhase.Battle;
            return true;
        }

        public bool CompleteBattle()
        {
            if (Phase != LocalBattlePhase.Battle) return false;
            EnterPreparation();
            return true;
        }

        public void Fail(string error)
        {
            Phase = LocalBattlePhase.Error;
            RemainingPreparationSeconds = 0f;
            LastError = error ?? string.Empty;
        }
    }

    public sealed class PreparationSealResult
    {
        internal PreparationSealResult(PlayerStateSnapshot before, PlayerStateSnapshot after, IEnumerable<string> overflowRemovedUnitIds, string autoDeployedUnitId, BattleInput input)
        {
            Before = before;
            After = after;
            OverflowRemovedUnitIds = new ReadOnlyCollection<string>((overflowRemovedUnitIds ?? Enumerable.Empty<string>()).ToArray());
            AutoDeployedUnitId = autoDeployedUnitId ?? string.Empty;
            Input = input;
        }

        public PlayerStateSnapshot Before { get; }
        public PlayerStateSnapshot After { get; }
        public IReadOnlyList<string> OverflowRemovedUnitIds { get; }
        public string AutoDeployedUnitId { get; }
        public BattleInput Input { get; }
    }

    /// <summary>One player's irreversible preparation-end changes before a pair is converted into a BattleInput.</summary>
    public sealed class PreparationPlayerSealResult
    {
        internal PreparationPlayerSealResult(PlayerStateSnapshot before, PlayerStateSnapshot after, IEnumerable<string> overflowRemovedUnitIds, string autoDeployedUnitId)
        {
            Before = before;
            After = after;
            OverflowRemovedUnitIds = new ReadOnlyCollection<string>((overflowRemovedUnitIds ?? Enumerable.Empty<string>()).ToArray());
            AutoDeployedUnitId = autoDeployedUnitId ?? string.Empty;
        }

        public PlayerStateSnapshot Before { get; }
        public PlayerStateSnapshot After { get; }
        public IReadOnlyList<string> OverflowRemovedUnitIds { get; }
        public string AutoDeployedUnitId { get; }
    }

    /// <summary>An immutable input for one confirmed UI-009 battle pairing.</summary>
    public sealed class FourPlayerBattleMatchSeal
    {
        internal FourPlayerBattleMatchSeal(string matchId, BattleInput input) { MatchId = matchId ?? string.Empty; Input = input; }
        public string MatchId { get; }
        public BattleInput Input { get; }
    }

    /// <summary>All preparation commits and pair inputs for one four-player local battle phase.</summary>
    public sealed class FourPlayerBattleRoundSealResult
    {
        internal FourPlayerBattleRoundSealResult(IEnumerable<PreparationPlayerSealResult> playerSeals, IEnumerable<FourPlayerBattleMatchSeal> matches)
        {
            PlayerSeals = new ReadOnlyCollection<PreparationPlayerSealResult>((playerSeals ?? Enumerable.Empty<PreparationPlayerSealResult>()).ToArray());
            Matches = new ReadOnlyCollection<FourPlayerBattleMatchSeal>((matches ?? Enumerable.Empty<FourPlayerBattleMatchSeal>()).ToArray());
        }

        public IReadOnlyList<PreparationPlayerSealResult> PlayerSeals { get; }
        public IReadOnlyList<FourPlayerBattleMatchSeal> Matches { get; }
    }

    /// <summary>Converts the persistent local state into a validated, immutable local-battle-v1 input.</summary>
    public static class PlayerStateBattleInputAdapter
    {
        public static bool TryCreate(PlayerStateSnapshot home, PlayerStateSnapshot away, UnitCatalog catalog, string battleId, int maxTicks, out BattleInput input, out IReadOnlyList<ValidationError> errors)
        {
            if (!TryLoadDefaultAbilityCatalog(catalog, out var abilityCatalog, out errors)) { input = null; return false; }
            return TryCreate(home, away, catalog, abilityCatalog, battleId, maxTicks, out input, out errors);
        }

        public static bool TryCreate(PlayerStateSnapshot home, PlayerStateSnapshot away, UnitCatalog catalog, AbilityCatalog abilityCatalog, string battleId, int maxTicks, out BattleInput input, out IReadOnlyList<ValidationError> errors)
        {
            input = null;
            if (home == null || away == null || catalog == null || abilityCatalog == null)
            {
                errors = new[] { new ValidationError("round.snapshot.missing", "Home snapshot, Away snapshot, and catalog are required.") };
                return false;
            }

            var specification = new BattleInputSpecification(
                BattleInput.LocalBattleSchemaVersion,
                battleId,
                maxTicks,
                catalog.Entries.Select(entry => entry.Definition),
                abilityCatalog.Abilities,
                new[] { ToPlayerSnapshot(home, BattleSide.Home), ToPlayerSnapshot(away, BattleSide.Away) });
            return BattleInputFactory.TryCreate(specification, out input, out errors);
        }

        public static bool TryCreate(PlayerStateSnapshot player, UnitCatalog catalog, PlayerSnapshot fixedAway, string battleId, int maxTicks, out BattleInput input, out IReadOnlyList<ValidationError> errors)
        {
            if (!TryLoadDefaultAbilityCatalog(catalog, out var abilityCatalog, out errors)) { input = null; return false; }
            return TryCreate(player, catalog, abilityCatalog, fixedAway, battleId, maxTicks, out input, out errors);
        }

        public static bool TryCreate(PlayerStateSnapshot player, UnitCatalog catalog, AbilityCatalog abilityCatalog, PlayerSnapshot fixedAway, string battleId, int maxTicks, out BattleInput input, out IReadOnlyList<ValidationError> errors)
        {
            input = null;
            if (player == null || catalog == null || abilityCatalog == null || fixedAway == null)
            {
                errors = new[] { new ValidationError("round.snapshot.missing", "Player snapshot, catalog, and fixed Away snapshot are required.") };
                return false;
            }

            var homeUnits = player.Units.Select(unit => new UnitSnapshot(
                unit.UnitId,
                unit.TypeId,
                ToBattleZone(unit.Zone),
                unit.Formation.HasValue ? new FormationCoordinate?(new FormationCoordinate(unit.Formation.Value.X, unit.Formation.Value.Y)) : null,
                unit.Buffs.Select(buff => new BuffPlaceholder(buff.Id, buff.RawPayload)),
                unit.EliteLevel));
            var specification = new BattleInputSpecification(
                BattleInput.LocalBattleSchemaVersion,
                battleId,
                maxTicks,
                catalog.Entries.Select(entry => entry.Definition),
                abilityCatalog.Abilities,
                new[] { new PlayerSnapshot(player.PlayerId, BattleSide.Home, homeUnits), fixedAway });
            return BattleInputFactory.TryCreate(specification, out input, out errors);
        }

        private static PlayerSnapshot ToPlayerSnapshot(PlayerStateSnapshot player, BattleSide side)
        {
            var units = player.Units.Select(unit => new UnitSnapshot(
                unit.UnitId,
                unit.TypeId,
                ToBattleZone(unit.Zone),
                unit.Formation.HasValue ? new FormationCoordinate?(new FormationCoordinate(unit.Formation.Value.X, unit.Formation.Value.Y)) : null,
                unit.Buffs.Select(buff => new BuffPlaceholder(buff.Id, buff.RawPayload)),
                unit.EliteLevel));
            return new PlayerSnapshot(player.PlayerId, side, units);
        }

        private static UnitZone ToBattleZone(PlayerUnitZone zone)
        {
            switch (zone)
            {
                case PlayerUnitZone.Deployed: return UnitZone.Deployed;
                case PlayerUnitZone.Shop: return UnitZone.Shop;
                default: return UnitZone.Staging;
            }
        }

        private static bool TryLoadDefaultAbilityCatalog(UnitCatalog catalog, out AbilityCatalog abilityCatalog, out IReadOnlyList<ValidationError> errors)
        {
            var loaded = AbilityCatalogLoader.LoadFromResources("BattleData/ability-catalog-v1", catalog);
            abilityCatalog = loaded.Catalog;
            errors = loaded.Errors;
            return loaded.Success;
        }
    }

    /// <summary>
    /// The irreversible preparation-end commit. Overflow deletion and auto-deployment happen before, and are
    /// represented by, the resulting immutable input; Core never receives an Overflow concept.
    /// </summary>
    public static class PreparationBattleSealer
    {
        public static bool TrySeal(PlayerState home, PlayerState away, UnitCatalog catalog, string battleId, int maxTicks, out PreparationSealResult result, out string error)
        {
            result = null;
            error = string.Empty;
            if (home == null || away == null || catalog == null)
            {
                error = "round.seal.dependencies.missing";
                return false;
            }

            if (!TryValidateUniqueUnitIds(new[] { home.Snapshot, away.Snapshot }, out error)) return false;
            if (!TryPreparePlayer(home, catalog, out var preparedHome, out error)) return false;
            if (!PlayerStateBattleInputAdapter.TryCreate(preparedHome.After, away.Snapshot, catalog, battleId, maxTicks, out var input, out var validationErrors))
            {
                error = "round.seal.input.invalid:" + string.Join(" | ", validationErrors.Select(item => item.ToString()).ToArray());
                return false;
            }
            result = new PreparationSealResult(preparedHome.Before, preparedHome.After, preparedHome.OverflowRemovedUnitIds, preparedHome.AutoDeployedUnitId, input);
            return true;
        }

        public static bool TrySeal(PlayerState state, UnitCatalog catalog, PlayerSnapshot fixedAway, string battleId, int maxTicks, out PreparationSealResult result, out string error)
        {
            result = null;
            error = string.Empty;
            if (state == null || catalog == null || fixedAway == null)
            {
                error = "round.seal.dependencies.missing";
                return false;
            }

            if (!TryPreparePlayer(state, catalog, out var preparedPlayer, out error)) return false;
            if (!PlayerStateBattleInputAdapter.TryCreate(preparedPlayer.After, catalog, fixedAway, battleId, maxTicks, out var input, out var validationErrors))
            {
                error = "round.seal.input.invalid:" + string.Join(" | ", validationErrors.Select(item => item.ToString()).ToArray());
                return false;
            }
            result = new PreparationSealResult(preparedPlayer.Before, preparedPlayer.After, preparedPlayer.OverflowRemovedUnitIds, preparedPlayer.AutoDeployedUnitId, input);
            return true;
        }

        /// <summary>Commits one player's overflow cleanup and automatic deployment without choosing an opposing side.</summary>
        public static bool TryPreparePlayer(PlayerState state, UnitCatalog catalog, out PreparationPlayerSealResult result, out string error)
        {
            result = null;
            error = string.Empty;
            if (state == null || catalog == null)
            {
                error = "round.seal.dependencies.missing";
                return false;
            }

            var before = state.Snapshot;
            var removed = state.RemoveOverflowUnits();
            if (!removed.Success)
            {
                error = "round.seal.overflow.remove.failed:" + removed.Code;
                return false;
            }

            var afterOverflow = state.Snapshot;
            string autoDeployedUnitId = null;
            if (!afterOverflow.Units.Any(unit => unit.Zone == PlayerUnitZone.Deployed))
            {
                var candidates = afterOverflow.Units
                    .Where(unit => unit.Zone == PlayerUnitZone.Staging)
                    .Select(unit => new { Unit = unit, Entry = Find(catalog, unit.TypeId) })
                    .Where(item => item.Entry != null && item.Entry.DeploymentCost <= afterOverflow.DeploymentCost)
                    .OrderByDescending(item => item.Entry.DeploymentCost)
                    .ThenBy(item => item.Unit.UnitId, StringComparer.Ordinal)
                    .ToArray();
                if (candidates.Length > 0)
                {
                    var highestCost = candidates[0].Entry.DeploymentCost;
                    var highestCandidates = candidates.Where(item => item.Entry.DeploymentCost == highestCost).ToArray();
                    if (highestCandidates.Length > 1 && highestCandidates.Skip(1).Any(item => !AreStrictStackEquivalent(highestCandidates[0].Unit, item.Unit)))
                    {
                        error = "round.seal.autoDeploy.ambiguousHighestCost:" + highestCost;
                        return false;
                    }

                    var deploy = state.TryDeploy(candidates[0].Unit.UnitId, 5, 2);
                    if (!deploy.Success)
                    {
                        error = "round.seal.autoDeploy.failed:" + deploy.Code;
                        return false;
                    }
                    autoDeployedUnitId = candidates[0].Unit.UnitId;
                }
            }

            result = new PreparationPlayerSealResult(before, state.Snapshot, removed.RemovedUnitIds, autoDeployedUnitId);
            return true;
        }

        private static UnitCatalogEntry Find(UnitCatalog catalog, string typeId)
        {
            catalog.TryGet(typeId, out var entry);
            return entry;
        }

        private static bool AreStrictStackEquivalent(PlayerUnitSnapshot first, PlayerUnitSnapshot second)
        {
            if (first == null || second == null) return false;
            if (!string.Equals(first.TypeId, second.TypeId, StringComparison.Ordinal) || first.EliteLevel != second.EliteLevel) return false;
            var firstBuffs = first.Buffs.OrderBy(buff => buff.Id, StringComparer.Ordinal).ThenBy(buff => buff.RawPayload, StringComparer.Ordinal);
            var secondBuffs = second.Buffs.OrderBy(buff => buff.Id, StringComparer.Ordinal).ThenBy(buff => buff.RawPayload, StringComparer.Ordinal);
            return firstBuffs.SequenceEqual(secondBuffs);
        }

        internal static bool TryValidateUniqueUnitIds(IEnumerable<PlayerStateSnapshot> players, out string error)
        {
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var player in players ?? Enumerable.Empty<PlayerStateSnapshot>())
            {
                if (player == null) continue;
                foreach (var id in player.Units.Select(unit => unit.UnitId))
                {
                    if (owners.TryGetValue(id, out var existingOwner))
                    {
                        error = "player.unitId.conflict; unitId=" + id + "; homePlayerId=" + existingOwner + "; awayPlayerId=" + player.PlayerId;
                        return false;
                    }
                    owners.Add(id, player.PlayerId);
                }
            }
            error = string.Empty;
            return true;
        }
    }

    /// <summary>Pure UI-009 pairing: fixture Player1/Home vs Player2/Away and Player3/Home vs Player4/Away.</summary>
    public static class FourPlayerBattleRoundSealer
    {
        public static bool TrySealRound(LocalMatchState match, UnitCatalog catalog, int maxTicks, string roundId, out FourPlayerBattleRoundSealResult result, out string error)
        {
            var abilityCatalogLoad = AbilityCatalogLoader.LoadFromResources("BattleData/ability-catalog-v1", catalog);
            if (!abilityCatalogLoad.Success)
            {
                result = null;
                error = "round.abilityCatalog.load.failed:" + string.Join(" | ", abilityCatalogLoad.Errors.Select(item => item.ToString()).ToArray());
                return false;
            }
            return TrySealRound(match, catalog, abilityCatalogLoad.Catalog, maxTicks, roundId, out result, out error);
        }

        public static bool TrySealRound(LocalMatchState match, UnitCatalog catalog, AbilityCatalog abilityCatalog, int maxTicks, string roundId, out FourPlayerBattleRoundSealResult result, out string error)
        {
            result = null;
            error = string.Empty;
            if (match == null || catalog == null || abilityCatalog == null || maxTicks <= 0 || string.IsNullOrWhiteSpace(roundId))
            {
                error = "round.fourPlayer.dependencies.invalid";
                return false;
            }

            var playerIds = match.OrderedPlayerIds.ToArray();
            if (playerIds.Length != LocalMatchState.PlayerCount)
            {
                error = "round.fourPlayer.players.count.invalid:" + playerIds.Length;
                return false;
            }

            var states = new List<PlayerState>(playerIds.Length);
            foreach (var playerId in playerIds)
            {
                if (!match.TryGetPlayerState(playerId, out var state))
                {
                    error = "round.fourPlayer.player.missing:" + playerId;
                    return false;
                }
                states.Add(state);
            }
            if (!PreparationBattleSealer.TryValidateUniqueUnitIds(states.Select(state => state.Snapshot), out error)) return false;

            var playerSeals = new List<PreparationPlayerSealResult>(states.Count);
            foreach (var state in states)
            {
                if (!PreparationBattleSealer.TryPreparePlayer(state, catalog, out var playerSeal, out error)) return false;
                playerSeals.Add(playerSeal);
            }

            if (!PlayerStateBattleInputAdapter.TryCreate(playerSeals[0].After, playerSeals[1].After, catalog, abilityCatalog, roundId + "-ab", maxTicks, out var matchAb, out var abErrors))
            {
                error = "round.fourPlayer.match-ab.input.invalid:" + string.Join(" | ", abErrors.Select(item => item.ToString()).ToArray());
                return false;
            }
            if (!PlayerStateBattleInputAdapter.TryCreate(playerSeals[2].After, playerSeals[3].After, catalog, abilityCatalog, roundId + "-cd", maxTicks, out var matchCd, out var cdErrors))
            {
                error = "round.fourPlayer.match-cd.input.invalid:" + string.Join(" | ", cdErrors.Select(item => item.ToString()).ToArray());
                return false;
            }

            result = new FourPlayerBattleRoundSealResult(playerSeals, new[]
            {
                new FourPlayerBattleMatchSeal("match-ab", matchAb),
                new FourPlayerBattleMatchSeal("match-cd", matchCd)
            });
            return true;
        }
    }
}
