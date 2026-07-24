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

    /// <summary>Converts the persistent local state into a validated, immutable local-battle-v1 input.</summary>
    public static class PlayerStateBattleInputAdapter
    {
        public static bool TryCreate(PlayerStateSnapshot home, PlayerStateSnapshot away, UnitCatalog catalog, string battleId, int maxTicks, out BattleInput input, out IReadOnlyList<ValidationError> errors)
        {
            input = null;
            if (home == null || away == null || catalog == null)
            {
                errors = new[] { new ValidationError("round.snapshot.missing", "Home snapshot, Away snapshot, and catalog are required.") };
                return false;
            }

            var specification = new BattleInputSpecification(
                BattleInput.LocalBattleSchemaVersion,
                battleId,
                maxTicks,
                catalog.Entries.Select(entry => entry.Definition),
                new[] { ToPlayerSnapshot(home, BattleSide.Home), ToPlayerSnapshot(away, BattleSide.Away) });
            return BattleInputFactory.TryCreate(specification, out input, out errors);
        }

        public static bool TryCreate(PlayerStateSnapshot player, UnitCatalog catalog, PlayerSnapshot fixedAway, string battleId, int maxTicks, out BattleInput input, out IReadOnlyList<ValidationError> errors)
        {
            input = null;
            if (player == null || catalog == null || fixedAway == null)
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

            var before = home.Snapshot;
            var awaySnapshot = away.Snapshot;
            if (!TryValidateUniqueUnitIds(before, awaySnapshot, out error)) return false;

            var removed = home.RemoveOverflowUnits();
            if (!removed.Success)
            {
                error = "round.seal.overflow.remove.failed:" + removed.Code;
                return false;
            }

            var afterOverflow = home.Snapshot;
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
                    if (candidates.Count(item => item.Entry.DeploymentCost == highestCost) > 1)
                    {
                        error = "round.seal.autoDeploy.ambiguousHighestCost:" + highestCost;
                        return false;
                    }

                    var deploy = home.TryDeploy(candidates[0].Unit.UnitId, 5, 2);
                    if (!deploy.Success)
                    {
                        error = "round.seal.autoDeploy.failed:" + deploy.Code;
                        return false;
                    }
                    autoDeployedUnitId = candidates[0].Unit.UnitId;
                }
            }

            var after = home.Snapshot;
            if (!PlayerStateBattleInputAdapter.TryCreate(after, awaySnapshot, catalog, battleId, maxTicks, out var input, out var validationErrors))
            {
                error = "round.seal.input.invalid:" + string.Join(" | ", validationErrors.Select(item => item.ToString()).ToArray());
                return false;
            }
            result = new PreparationSealResult(before, after, removed.RemovedUnitIds, autoDeployedUnitId, input);
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
                    if (candidates.Count(item => item.Entry.DeploymentCost == highestCost) > 1)
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

            var after = state.Snapshot;
            if (!PlayerStateBattleInputAdapter.TryCreate(after, catalog, fixedAway, battleId, maxTicks, out var input, out var validationErrors))
            {
                error = "round.seal.input.invalid:" + string.Join(" | ", validationErrors.Select(item => item.ToString()).ToArray());
                return false;
            }
            result = new PreparationSealResult(before, after, removed.RemovedUnitIds, autoDeployedUnitId, input);
            return true;
        }

        private static UnitCatalogEntry Find(UnitCatalog catalog, string typeId)
        {
            catalog.TryGet(typeId, out var entry);
            return entry;
        }

        private static bool TryValidateUniqueUnitIds(PlayerStateSnapshot home, PlayerStateSnapshot away, out string error)
        {
            foreach (var id in home.Units.Select(unit => unit.UnitId))
            {
                if (away.Units.Any(unit => string.Equals(unit.UnitId, id, StringComparison.Ordinal)))
                {
                    error = "player.unitId.conflict; unitId=" + id + "; homePlayerId=" + home.PlayerId + "; awayPlayerId=" + away.PlayerId;
                    return false;
                }
            }
            error = string.Empty;
            return true;
        }
    }
}
