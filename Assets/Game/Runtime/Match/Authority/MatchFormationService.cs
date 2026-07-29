using System;
using System.Collections.Generic;
using System.Linq;

namespace ArknoNights.Match
{
    internal enum MatchFormationOperationOrigin
    {
        HumanCommand = 0,
        BotAuthority = 1,
        SealSafety = 2
    }

    internal sealed class MatchFormationOperationResult
    {
        internal MatchFormationOperationResult(
            MatchCommandCode code,
            string diagnosticCode,
            bool changed)
        {
            Code = code;
            DiagnosticCode = diagnosticCode;
            Changed = changed;
        }

        internal MatchCommandCode Code { get; }
        internal string DiagnosticCode { get; }
        internal bool Changed { get; }
        internal bool Accepted =>
            Code == MatchCommandCode.Accepted || Code == MatchCommandCode.AcceptedNoChange;
    }

    internal static class MatchFormationService
    {
        internal static MatchFormationOperationResult TryApply(
            MatchState source,
            MatchEconomyTransactionDraft draft,
            string playerId,
            MatchCommandPayload command,
            MatchFormationOperationOrigin origin)
        {
            if (source == null || draft == null)
            {
                return Rejected(
                    MatchCommandCode.InternalInvariantViolation,
                    "match.formation.stateOrDraft.null");
            }
            MatchSeatState seat;
            try
            {
                seat = draft.GetSeat(playerId);
            }
            catch (KeyNotFoundException)
            {
                return Rejected(MatchCommandCode.UnknownPlayer, "match.formation.player.unknown");
            }
            var validation = ValidateCommon(source, seat, origin);
            if (validation != null)
            {
                return validation;
            }

            if (command is DeployUnitCommand deploy)
            {
                return TryDeploy(source, draft, seat, deploy);
            }
            if (command is ReplaceDeployedUnitCommand replace)
            {
                return TryReplace(source, draft, seat, replace);
            }
            if (command is RelocateOrSwapUnitCommand relocate)
            {
                return TryRelocateOrSwap(draft, seat, relocate);
            }
            if (command is RetreatUnitCommand retreat)
            {
                return TryRetreat(source, draft, seat, retreat);
            }
            return Rejected(MatchCommandCode.InvalidPayload, "match.formation.command.unsupported");
        }

        private static MatchFormationOperationResult ValidateCommon(
            MatchState source,
            MatchSeatState seat,
            MatchFormationOperationOrigin origin)
        {
            if (source.Phase == MatchPhase.Ended)
            {
                return Rejected(MatchCommandCode.InvalidTransition, "match.phase.ended");
            }
            if (source.Phase != MatchPhase.Preparation)
            {
                return Rejected(MatchCommandCode.PhaseRejected, "match.formation.phase.rejected");
            }
            if (seat.Eliminated)
            {
                return Rejected(MatchCommandCode.Eliminated, "match.formation.player.eliminated");
            }
            if (origin == MatchFormationOperationOrigin.HumanCommand)
            {
                if (seat.ControllerKind != MatchControllerKind.Human)
                {
                    return Rejected(
                        MatchCommandCode.ControllerRejected,
                        "match.formation.controller.rejected");
                }
                if (seat.ConnectionState != MatchConnectionState.Connected)
                {
                    return Rejected(
                        MatchCommandCode.ConnectionRejected,
                        "match.formation.connection.rejected");
                }
                if (seat.Ready)
                {
                    return Rejected(
                        MatchCommandCode.FormationLocked,
                        "match.formation.ready.locked");
                }
            }
            else if (origin == MatchFormationOperationOrigin.BotAuthority
                && seat.ControllerKind == MatchControllerKind.Human)
            {
                return Rejected(
                    MatchCommandCode.ControllerRejected,
                    "match.formation.botOrigin.human.rejected");
            }
            return null;
        }

        private static MatchFormationOperationResult TryDeploy(
            MatchState source,
            MatchEconomyTransactionDraft draft,
            MatchSeatState seat,
            DeployUnitCommand command)
        {
            if (!command.TargetFormation.IsValid)
            {
                return Rejected(
                    MatchCommandCode.FormationPositionInvalid,
                    "match.formation.deploy.target.invalid");
            }
            if (command.ExpectedAvailableCost != seat.AvailableDeploymentCost)
            {
                return Rejected(
                    MatchCommandCode.DeploymentCostChanged,
                    "match.formation.deploy.availableCost.changed");
            }
            var unit = FindUnit(seat, command.UnitId);
            if (unit == null)
            {
                return Rejected(MatchCommandCode.UnitUnknown, "match.formation.deploy.unit.unknown");
            }
            if (unit.Zone != MatchUnitZone.Staging)
            {
                return Rejected(
                    MatchCommandCode.UnitZoneRejected,
                    "match.formation.deploy.unit.notStaging");
            }
            if (FindDeployedAt(seat, command.TargetFormation) != null)
            {
                return Rejected(
                    MatchCommandCode.FormationPositionOccupied,
                    "match.formation.deploy.target.occupied");
            }
            if (!TryGetDeploymentCost(source, unit, out var cost))
            {
                return Rejected(
                    MatchCommandCode.InternalInvariantViolation,
                    "match.formation.deploy.catalog.invalid");
            }
            if (seat.AvailableDeploymentCost < cost)
            {
                return Rejected(
                    MatchCommandCode.InsufficientDeploymentCost,
                    "match.formation.deploy.cost.insufficient");
            }

            var moved = Move(unit, MatchUnitZone.Deployed, command.TargetFormation);
            draft.ReplaceSeat(seat.With(
                availableDeploymentCost: checked(seat.AvailableDeploymentCost - cost),
                units: ReplaceUnit(seat, moved)));
            return Accepted("match.formation.deploy.accepted");
        }

        private static MatchFormationOperationResult TryReplace(
            MatchState source,
            MatchEconomyTransactionDraft draft,
            MatchSeatState seat,
            ReplaceDeployedUnitCommand command)
        {
            if (!command.TargetFormation.IsValid)
            {
                return Rejected(
                    MatchCommandCode.FormationPositionInvalid,
                    "match.formation.replace.target.invalid");
            }
            var incoming = FindUnit(seat, command.StagingUnitId);
            if (incoming == null)
            {
                return Rejected(MatchCommandCode.UnitUnknown, "match.formation.replace.incoming.unknown");
            }
            if (incoming.Zone != MatchUnitZone.Staging)
            {
                return Rejected(
                    MatchCommandCode.UnitZoneRejected,
                    "match.formation.replace.incoming.notStaging");
            }
            var outgoing = FindDeployedAt(seat, command.TargetFormation);
            if (outgoing == null
                || !string.Equals(
                    outgoing.UnitId,
                    command.ExpectedDeployedUnitId,
                    StringComparison.Ordinal))
            {
                return Rejected(
                    MatchCommandCode.ShopOfferChanged,
                    "match.formation.replace.outgoing.changed");
            }
            if (!TryGetDeploymentCost(source, outgoing, out var outgoingCost)
                || !TryGetDeploymentCost(source, incoming, out var incomingCost))
            {
                return Rejected(
                    MatchCommandCode.InternalInvariantViolation,
                    "match.formation.replace.catalog.invalid");
            }
            int nextAvailable;
            try
            {
                nextAvailable = checked(
                    seat.AvailableDeploymentCost + outgoingCost - incomingCost);
            }
            catch (OverflowException)
            {
                return Rejected(
                    MatchCommandCode.InternalInvariantViolation,
                    "match.formation.replace.cost.overflow");
            }
            if (nextAvailable < 0)
            {
                return Rejected(
                    MatchCommandCode.InsufficientDeploymentCost,
                    "match.formation.replace.cost.insufficient");
            }

            var nextUnits = seat.Units.Select(unit =>
            {
                if (string.Equals(unit.UnitId, outgoing.UnitId, StringComparison.Ordinal))
                {
                    return Move(unit, MatchUnitZone.Staging, null);
                }
                if (string.Equals(unit.UnitId, incoming.UnitId, StringComparison.Ordinal))
                {
                    return Move(unit, MatchUnitZone.Deployed, command.TargetFormation);
                }
                return unit;
            }).ToArray();
            var nextSeat = seat.With(
                availableDeploymentCost: nextAvailable,
                units: nextUnits);
            if (MatchStagingProjection.CountOccupiedSlots(
                nextSeat,
                source.Pool.Catalog) > MatchEconomyRules.StagingSlotCapacity)
            {
                return Rejected(MatchCommandCode.StagingFull, "match.formation.replace.staging.full");
            }
            draft.ReplaceSeat(nextSeat);
            return Accepted("match.formation.replace.accepted");
        }

        private static MatchFormationOperationResult TryRelocateOrSwap(
            MatchEconomyTransactionDraft draft,
            MatchSeatState seat,
            RelocateOrSwapUnitCommand command)
        {
            if (!command.TargetFormation.IsValid)
            {
                return Rejected(
                    MatchCommandCode.FormationPositionInvalid,
                    "match.formation.relocate.target.invalid");
            }
            var sourceUnit = FindUnit(seat, command.UnitId);
            if (sourceUnit == null)
            {
                return Rejected(MatchCommandCode.UnitUnknown, "match.formation.relocate.unit.unknown");
            }
            if (sourceUnit.Zone != MatchUnitZone.Deployed || !sourceUnit.Formation.HasValue)
            {
                return Rejected(
                    MatchCommandCode.UnitZoneRejected,
                    "match.formation.relocate.unit.notDeployed");
            }
            if (sourceUnit.Formation.Value.Equals(command.TargetFormation))
            {
                return new MatchFormationOperationResult(
                    MatchCommandCode.AcceptedNoChange,
                    "match.formation.relocate.noChange",
                    false);
            }
            var targetUnit = FindDeployedAt(seat, command.TargetFormation);
            var nextUnits = seat.Units.Select(unit =>
            {
                if (string.Equals(unit.UnitId, sourceUnit.UnitId, StringComparison.Ordinal))
                {
                    return Move(unit, MatchUnitZone.Deployed, command.TargetFormation);
                }
                if (targetUnit != null
                    && string.Equals(unit.UnitId, targetUnit.UnitId, StringComparison.Ordinal))
                {
                    return Move(unit, MatchUnitZone.Deployed, sourceUnit.Formation);
                }
                return unit;
            });
            draft.ReplaceSeat(seat.With(units: nextUnits));
            return Accepted(targetUnit == null
                ? "match.formation.relocate.accepted"
                : "match.formation.swap.accepted");
        }

        private static MatchFormationOperationResult TryRetreat(
            MatchState source,
            MatchEconomyTransactionDraft draft,
            MatchSeatState seat,
            RetreatUnitCommand command)
        {
            var unit = FindUnit(seat, command.UnitId);
            if (unit == null)
            {
                return Rejected(MatchCommandCode.UnitUnknown, "match.formation.retreat.unit.unknown");
            }
            if (unit.Zone != MatchUnitZone.Deployed)
            {
                return Rejected(
                    MatchCommandCode.UnitZoneRejected,
                    "match.formation.retreat.unit.notDeployed");
            }
            if (!TryGetDeploymentCost(source, unit, out var cost))
            {
                return Rejected(
                    MatchCommandCode.InternalInvariantViolation,
                    "match.formation.retreat.catalog.invalid");
            }
            var moved = Move(unit, MatchUnitZone.Staging, null);
            var nextSeat = seat.With(
                availableDeploymentCost: checked(seat.AvailableDeploymentCost + cost),
                units: ReplaceUnit(seat, moved));
            if (MatchStagingProjection.CountOccupiedSlots(
                nextSeat,
                source.Pool.Catalog) > MatchEconomyRules.StagingSlotCapacity)
            {
                return Rejected(MatchCommandCode.StagingFull, "match.formation.retreat.staging.full");
            }
            draft.ReplaceSeat(nextSeat);
            return Accepted("match.formation.retreat.accepted");
        }

        private static MatchUnitState FindUnit(MatchSeatState seat, string unitId)
        {
            return seat.Units.FirstOrDefault(unit =>
                string.Equals(unit.UnitId, unitId, StringComparison.Ordinal));
        }

        private static MatchUnitState FindDeployedAt(
            MatchSeatState seat,
            MatchFormationPosition position)
        {
            return seat.Units.FirstOrDefault(unit =>
                unit.Zone == MatchUnitZone.Deployed
                && unit.Formation.HasValue
                && unit.Formation.Value.Equals(position));
        }

        private static bool TryGetDeploymentCost(
            MatchState source,
            MatchUnitState unit,
            out int cost)
        {
            if (!source.Pool.Catalog.TryGet(unit.TypeId, out var entry)
                || unit.EliteLevel < 0
                || unit.EliteLevel > entry.MaxEliteLevel)
            {
                cost = 0;
                return false;
            }
            cost = MatchEliteRules.GetDeploymentCost(entry, unit.EliteLevel);
            return true;
        }

        private static MatchUnitState Move(
            MatchUnitState unit,
            MatchUnitZone zone,
            MatchFormationPosition? formation)
        {
            return new MatchUnitState(
                unit.UnitId,
                unit.TypeId,
                zone,
                unit.EliteLevel,
                formation,
                unit.AcquisitionOrdinal,
                unit.Buffs);
        }

        private static IEnumerable<MatchUnitState> ReplaceUnit(
            MatchSeatState seat,
            MatchUnitState replacement)
        {
            return seat.Units.Select(unit =>
                string.Equals(unit.UnitId, replacement.UnitId, StringComparison.Ordinal)
                    ? replacement
                    : unit);
        }

        private static MatchFormationOperationResult Accepted(string diagnostic)
        {
            return new MatchFormationOperationResult(
                MatchCommandCode.Accepted,
                diagnostic,
                true);
        }

        private static MatchFormationOperationResult Rejected(
            MatchCommandCode code,
            string diagnostic)
        {
            return new MatchFormationOperationResult(code, diagnostic, false);
        }
    }
}
