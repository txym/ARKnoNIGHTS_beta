using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ArknoNights.Match
{
    public enum MatchBotActionPart
    {
        Primary = 0,
        DeployFollowUp = 1
    }

    public sealed class MatchBotActionId
    {
        public MatchBotActionId(
            string sessionId,
            int seatIndex,
            long controllerGeneration,
            int roundNumber,
            long decisionOrdinal,
            MatchBotActionPart actionPart)
        {
            SessionId = sessionId ?? string.Empty;
            SeatIndex = seatIndex;
            ControllerGeneration = controllerGeneration;
            RoundNumber = roundNumber;
            DecisionOrdinal = decisionOrdinal;
            ActionPart = actionPart;
            var writer = new CanonicalSummaryWriter(nameof(MatchBotActionId));
            writer.String("sessionId", SessionId);
            writer.Integer("seatIndex", SeatIndex);
            writer.Integer("controllerGeneration", ControllerGeneration);
            writer.Integer("roundNumber", RoundNumber);
            writer.Integer("decisionOrdinal", DecisionOrdinal);
            writer.EnumValue("actionPart", ActionPart);
            CanonicalSummary = writer.ToString();
        }

        public string SessionId { get; }
        public int SeatIndex { get; }
        public long ControllerGeneration { get; }
        public int RoundNumber { get; }
        public long DecisionOrdinal { get; }
        public MatchBotActionPart ActionPart { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchBotOperationEnvelope
    {
        public MatchBotOperationEnvelope(
            MatchBotActionId actionId,
            string playerId,
            MatchCommandPayload payload)
        {
            ActionId = actionId;
            PlayerId = playerId ?? string.Empty;
            Payload = payload;
            var writer = new CanonicalSummaryWriter(nameof(MatchBotOperationEnvelope));
            writer.Summary("actionId", ActionId == null ? string.Empty : ActionId.CanonicalSummary);
            writer.String("playerId", PlayerId);
            writer.Summary("payload", Payload == null ? string.Empty : Payload.CanonicalSummary);
            CanonicalSummary = writer.ToString();
        }

        public MatchBotActionId ActionId { get; }
        public string PlayerId { get; }
        public MatchCommandPayload Payload { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchBotOperationResult
    {
        internal MatchBotOperationResult(
            MatchBotActionId actionId,
            MatchCommandCode code,
            long currentStateRevision,
            long? acceptedStateRevision,
            bool changedState,
            string diagnosticCode,
            MatchAcquisitionResult acquisition = null)
        {
            ActionId = actionId;
            Code = code;
            CurrentStateRevision = currentStateRevision;
            AcceptedStateRevision = acceptedStateRevision;
            ChangedState = changedState;
            DiagnosticCode = diagnosticCode ?? string.Empty;
            Acquisition = acquisition;
            var writer = new CanonicalSummaryWriter(nameof(MatchBotOperationResult));
            writer.Summary("actionId", ActionId == null ? string.Empty : ActionId.CanonicalSummary);
            writer.EnumValue("code", Code);
            writer.Integer("currentRevision", CurrentStateRevision);
            writer.String(
                "acceptedRevision",
                AcceptedStateRevision.HasValue
                    ? AcceptedStateRevision.Value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                    : "null");
            writer.Boolean("changedState", ChangedState);
            writer.String("diagnosticCode", DiagnosticCode);
            writer.Summary("acquisition", Acquisition == null ? string.Empty : Acquisition.CanonicalSummary);
            CanonicalSummary = writer.ToString();
        }

        public MatchBotActionId ActionId { get; }
        public MatchCommandCode Code { get; }
        public bool Accepted =>
            Code == MatchCommandCode.Accepted
            || Code == MatchCommandCode.AcceptedNoChange
            || Code == MatchCommandCode.PoolExhaustedDiagnostic;
        public long CurrentStateRevision { get; }
        public long? AcceptedStateRevision { get; }
        public bool ChangedState { get; }
        public string DiagnosticCode { get; }
        public MatchAcquisitionResult Acquisition { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchBotShopOfferSnapshot
    {
        internal MatchBotShopOfferSnapshot(
            MatchShopOfferState offer,
            int? baseElite0DeploymentCost)
        {
            SlotIndex = offer.SlotIndex;
            UnitId = offer.UnitId;
            TypeId = offer.TypeId;
            Price = offer.Price;
            BaseElite0DeploymentCost = baseElite0DeploymentCost;
            IsFrozen = offer.IsFrozen;
        }

        public int SlotIndex { get; }
        public string UnitId { get; }
        public string TypeId { get; }
        public int? Price { get; }
        public int? BaseElite0DeploymentCost { get; }
        public bool IsFrozen { get; }
        public bool IsEmpty => string.IsNullOrEmpty(UnitId);
    }

    public sealed class MatchBotHumanReadinessSnapshot
    {
        internal MatchBotHumanReadinessSnapshot(MatchSeatState seat)
        {
            PlayerId = seat.PlayerId;
            Ready = seat.Ready;
            Connected = seat.ConnectionState == MatchConnectionState.Connected;
            Eliminated = seat.Eliminated;
        }

        public string PlayerId { get; }
        public bool Ready { get; }
        public bool Connected { get; }
        public bool Eliminated { get; }
    }

    public sealed class MatchBotScopedSnapshot
    {
        internal MatchBotScopedSnapshot(MatchState state, MatchSeatState seat)
        {
            SessionId = state.SessionId;
            StateRevision = state.StateRevision;
            RoundNumber = state.RoundNumber;
            Phase = state.Phase;
            SeatIndex = seat.SeatIndex;
            PlayerId = seat.PlayerId;
            ControllerKind = seat.ControllerKind;
            ControllerGeneration = seat.ControllerGeneration;
            IsEliminated = seat.Eliminated;
            Gold = seat.Gold;
            Level = seat.Level;
            CurrentUpgradePrice = seat.Level == MatchEconomyRules.MaximumLevel
                ? (int?)null
                : seat.CurrentUpgradePrice;
            TotalDeploymentCost = seat.TotalDeploymentCost;
            AvailableDeploymentCost = seat.AvailableDeploymentCost;
            StagingSlotUsage = MatchStagingProjection.Project(
                seat,
                state.Pool.Catalog).Count;
            ShopOffers = new ReadOnlyCollection<MatchBotShopOfferSnapshot>(
                seat.ShopOffers
                    .OrderBy(offer => offer.SlotIndex)
                    .Select(offer =>
                    {
                        int? baseCost = null;
                        if (!offer.IsEmpty
                            && state.Pool.Catalog.TryGet(offer.TypeId, out var entry))
                        {
                            baseCost = entry.BaseDeploymentCost;
                        }
                        return new MatchBotShopOfferSnapshot(offer, baseCost);
                    })
                    .ToArray());
            OwnUnits = new ReadOnlyCollection<MatchUnitState>(
                seat.Units.OrderBy(unit => unit.UnitId, StringComparer.Ordinal).ToArray());
            PublicHumanReadiness = new ReadOnlyCollection<MatchBotHumanReadinessSnapshot>(
                state.Seats
                    .Where(candidate => candidate.ControllerKind == MatchControllerKind.Human)
                    .OrderBy(candidate => candidate.SeatIndex)
                    .Select(candidate => new MatchBotHumanReadinessSnapshot(candidate))
                    .ToArray());
            PreparationStartedAtHostMs = state.Flow.PreparationStartedAtHostMonotonicMs;
            PreparationDeadlineHostMs = state.Flow.PreparationDeadlineHostMonotonicMs;
            LastHostMonotonicMs = state.Flow.LastHostMonotonicMs;
        }

        public string SessionId { get; }
        public long StateRevision { get; }
        public int RoundNumber { get; }
        public MatchPhase Phase { get; }
        public int SeatIndex { get; }
        public string PlayerId { get; }
        public MatchControllerKind ControllerKind { get; }
        public long ControllerGeneration { get; }
        public bool IsEliminated { get; }
        public int Gold { get; }
        public int Level { get; }
        public int? CurrentUpgradePrice { get; }
        public int TotalDeploymentCost { get; }
        public int AvailableDeploymentCost { get; }
        public int StagingSlotUsage { get; }
        public IReadOnlyList<MatchBotShopOfferSnapshot> ShopOffers { get; }
        public IReadOnlyList<MatchUnitState> OwnUnits { get; }
        public IReadOnlyList<MatchBotHumanReadinessSnapshot> PublicHumanReadiness { get; }
        public long PreparationStartedAtHostMs { get; }
        public long PreparationDeadlineHostMs { get; }
        public long LastHostMonotonicMs { get; }
    }

    public sealed class MatchBotSeatControlSnapshot
    {
        internal MatchBotSeatControlSnapshot(MatchState state, MatchSeatState seat)
        {
            SessionId = state.SessionId;
            RoundNumber = state.RoundNumber;
            Phase = state.Phase;
            SeatIndex = seat.SeatIndex;
            PlayerId = seat.PlayerId;
            IsHost = string.Equals(
                state.HostPlayerId,
                seat.PlayerId,
                StringComparison.Ordinal);
            ControllerKind = seat.ControllerKind;
            ControllerGeneration = seat.ControllerGeneration;
            ConnectionState = seat.ConnectionState;
            Ready = seat.Ready;
            Eliminated = seat.Eliminated;
            PreparationStartedAtHostMs = state.Flow.PreparationStartedAtHostMonotonicMs;
            PreparationDeadlineHostMs = state.Flow.PreparationDeadlineHostMonotonicMs;
            LastHostMonotonicMs = state.Flow.LastHostMonotonicMs;
        }

        public string SessionId { get; }
        public int RoundNumber { get; }
        public MatchPhase Phase { get; }
        public int SeatIndex { get; }
        public string PlayerId { get; }
        public bool IsHost { get; }
        public MatchControllerKind ControllerKind { get; }
        public long ControllerGeneration { get; }
        public MatchConnectionState ConnectionState { get; }
        public bool Ready { get; }
        public bool Eliminated { get; }
        public long PreparationStartedAtHostMs { get; }
        public long PreparationDeadlineHostMs { get; }
        public long LastHostMonotonicMs { get; }
    }

    public interface IMatchBotHost
    {
        IReadOnlyList<MatchBotSeatControlSnapshot> ProjectBotControlSeats();
        bool TryProjectForBot(
            string playerId,
            out MatchBotScopedSnapshot snapshot,
            out string diagnosticCode);
        MatchBotOperationResult ExecuteBotOperation(MatchBotOperationEnvelope envelope);
        MatchTransactionResult SetHumanDisconnectedGrace(string playerId);
        MatchTransactionResult ActivateTakeoverBot(string playerId);
        MatchTransactionResult MarkHumanQuit(string playerId);
        MatchTransactionResult RestoreHumanControl(string playerId);
    }
}
