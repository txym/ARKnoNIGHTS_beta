using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ArknoNights.Match
{
    public enum MatchPhase
    {
        Initializing = 0,
        Preparation = 1,
        Sealing = 2,
        Battle = 3,
        Settlement = 4,
        Ended = 5
    }

    public enum MatchControllerKind
    {
        Human = 0,
        NativeBot = 1,
        TakeoverBot = 2
    }

    public enum MatchConnectionState
    {
        Connected = 0,
        DisconnectedGrace = 1,
        Quit = 2,
        Eliminated = 3
    }

    public enum PublicConnectionState
    {
        Online = 0,
        LostConnection = 1,
        Eliminated = 2,
        Spectating = 3
    }

    public enum MatchUnitZone
    {
        Deployed = 0,
        Staging = 1,
        Overflow = 2
    }

    public enum MatchInitializationCode
    {
        Accepted = 0,
        InvalidRequest = 1,
        InvalidSessionId = 2,
        InvalidMatchSeed = 3,
        InvalidCompatibilityManifest = 4,
        InvalidSeatCount = 5,
        InvalidSeatIndex = 6,
        DuplicateSeatIndex = 7,
        InvalidPlayerId = 8,
        DuplicatePlayerId = 9,
        InvalidDisplayName = 10,
        InvalidAvatarId = 11,
        InvalidController = 12,
        HostMissing = 13,
        HostNotHuman = 14,
        InvalidInitialValues = 15,
        InternalInvariantViolation = 16,
        InvalidShopCatalog = 17,
        ShopCatalogCompatibilityMismatch = 18
    }

    public enum MatchCommandCode
    {
        Accepted = 0,
        AcceptedNoChange = 1,
        SessionMismatch = 2,
        UnknownPlayer = 3,
        CommandIdInvalid = 4,
        CommandIdConflict = 5,
        FutureRevision = 6,
        PhaseRejected = 7,
        ControllerRejected = 8,
        ConnectionRejected = 9,
        Eliminated = 10,
        InvalidPayload = 11,
        InvalidTransition = 12,
        InternalInvariantViolation = 13,
        ShopSlotInvalid = 14,
        ShopOfferMissing = 15,
        ShopOfferChanged = 16,
        InsufficientGold = 17,
        StagingFull = 18,
        LevelMax = 19,
        UpgradeLevelChanged = 20,
        UpgradePriceChanged = 21,
        PoolExhaustedDiagnostic = 22,
        NaturalRefreshAlreadyApplied = 23,
        FormationLocked = 24,
        FormationPositionInvalid = 25,
        FormationPositionOccupied = 26,
        UnitUnknown = 27,
        UnitZoneRejected = 28,
        DeploymentCostChanged = 29,
        InsufficientDeploymentCost = 30,
        ClockRegressed = 31,
        BattleIdUnexpected = 32,
        SealedInputHashMismatch = 33,
        BattleOutcomeMismatch = 34,
        BattleResolutionConflict = 35,
        BattleResultsIncomplete = 36,
        BattlePlaybackIncomplete = 37,
        SettlementAlreadyApplied = 38,
        FatalMatchError = 39,
        ControllerGenerationChanged = 40
    }

    public static class MatchInitialValues
    {
        public const int Life = 400;
        public const int Gold = 7;
        public const int Level = 1;
        public const int TotalDeploymentCost = 16;
        public const int AvailableDeploymentCost = 16;
    }

    public sealed class MatchInitialPlayerValues
    {
        public MatchInitialPlayerValues(
            int life,
            int gold,
            int level,
            int totalDeploymentCost,
            int availableDeploymentCost)
        {
            Life = life;
            Gold = gold;
            Level = level;
            TotalDeploymentCost = totalDeploymentCost;
            AvailableDeploymentCost = availableDeploymentCost;
        }

        public static MatchInitialPlayerValues Standard => new MatchInitialPlayerValues(
            MatchInitialValues.Life,
            MatchInitialValues.Gold,
            MatchInitialValues.Level,
            MatchInitialValues.TotalDeploymentCost,
            MatchInitialValues.AvailableDeploymentCost);

        public int Life { get; }
        public int Gold { get; }
        public int Level { get; }
        public int TotalDeploymentCost { get; }
        public int AvailableDeploymentCost { get; }

        internal bool IsConfirmedStandard =>
            Life == MatchInitialValues.Life
            && Gold == MatchInitialValues.Gold
            && Level == MatchInitialValues.Level
            && TotalDeploymentCost == MatchInitialValues.TotalDeploymentCost
            && AvailableDeploymentCost == MatchInitialValues.AvailableDeploymentCost;
    }

    public sealed class MatchSeatInitialization
    {
        public MatchSeatInitialization(
            int seatIndex,
            string playerId,
            string displayName,
            string avatarId,
            MatchControllerKind initialControllerKind)
        {
            SeatIndex = seatIndex;
            PlayerId = playerId;
            DisplayName = displayName;
            AvatarId = avatarId;
            InitialControllerKind = initialControllerKind;
        }

        public int SeatIndex { get; }
        public string PlayerId { get; }
        public string DisplayName { get; }
        public string AvatarId { get; }
        public MatchControllerKind InitialControllerKind { get; }
    }

    public sealed class MatchInitializationRequest
    {
        public MatchInitializationRequest(
            string sessionId,
            string matchSeed,
            string hostPlayerId,
            MatchCompatibilityManifest compatibilityManifest,
            MatchShopCatalog shopCatalog,
            IEnumerable<MatchSeatInitialization> seats)
            : this(
                sessionId,
                matchSeed,
                hostPlayerId,
                compatibilityManifest,
                shopCatalog,
                MatchInitialPlayerValues.Standard,
                seats)
        {
        }

        public MatchInitializationRequest(
            string sessionId,
            string matchSeed,
            string hostPlayerId,
            MatchCompatibilityManifest compatibilityManifest,
            IEnumerable<MatchSeatInitialization> seats)
            : this(
                sessionId,
                matchSeed,
                hostPlayerId,
                compatibilityManifest,
                null,
                MatchInitialPlayerValues.Standard,
                seats)
        {
        }

        public MatchInitializationRequest(
            string sessionId,
            string matchSeed,
            string hostPlayerId,
            MatchCompatibilityManifest compatibilityManifest,
            MatchShopCatalog shopCatalog,
            MatchInitialPlayerValues initialPlayerValues,
            IEnumerable<MatchSeatInitialization> seats)
        {
            SessionId = sessionId;
            MatchSeed = matchSeed;
            HostPlayerId = hostPlayerId;
            CompatibilityManifest = compatibilityManifest;
            ShopCatalog = shopCatalog;
            InitialPlayerValues = initialPlayerValues;
            Seats = new ReadOnlyCollection<MatchSeatInitialization>(
                (seats ?? Enumerable.Empty<MatchSeatInitialization>()).ToArray());
        }

        public MatchInitializationRequest(
            string sessionId,
            string matchSeed,
            string hostPlayerId,
            MatchCompatibilityManifest compatibilityManifest,
            MatchInitialPlayerValues initialPlayerValues,
            IEnumerable<MatchSeatInitialization> seats)
            : this(
                sessionId,
                matchSeed,
                hostPlayerId,
                compatibilityManifest,
                null,
                initialPlayerValues,
                seats)
        {
        }

        public string SessionId { get; }
        public string MatchSeed { get; }
        public string HostPlayerId { get; }
        public MatchCompatibilityManifest CompatibilityManifest { get; }
        public MatchShopCatalog ShopCatalog { get; }
        public MatchInitialPlayerValues InitialPlayerValues { get; }
        public IReadOnlyList<MatchSeatInitialization> Seats { get; }
    }

    public sealed class MatchInitializationResult
    {
        internal MatchInitializationResult(
            MatchInitializationCode code,
            string diagnosticCode,
            MatchAuthority authority,
            bool poolExhaustedDiagnostic = false)
        {
            Code = code;
            DiagnosticCode = diagnosticCode ?? string.Empty;
            Authority = authority;
            PoolExhaustedDiagnostic = poolExhaustedDiagnostic;
        }

        public bool Success => Code == MatchInitializationCode.Accepted && Authority != null;
        public MatchInitializationCode Code { get; }
        public string DiagnosticCode { get; }
        public MatchAuthority Authority { get; }
        public bool PoolExhaustedDiagnostic { get; }
    }

    public sealed class MatchTransactionResult
    {
        internal MatchTransactionResult(
            MatchCommandCode code,
            long currentStateRevision,
            long? acceptedStateRevision,
            bool changedState,
            string diagnosticCode,
            IEnumerable<string> retiredUnitIds = null)
        {
            Code = code;
            CurrentStateRevision = currentStateRevision;
            AcceptedStateRevision = acceptedStateRevision;
            ChangedState = changedState;
            DiagnosticCode = diagnosticCode ?? string.Empty;
            RetiredUnitIds = new ReadOnlyCollection<string>(
                (retiredUnitIds ?? Enumerable.Empty<string>()).ToArray());

            var writer = new CanonicalSummaryWriter(nameof(MatchTransactionResult));
            writer.EnumValue("code", Code);
            writer.Integer("currentRevision", CurrentStateRevision);
            writer.String("acceptedRevision", AcceptedStateRevision.HasValue
                ? AcceptedStateRevision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "null");
            writer.Boolean("changed", ChangedState);
            writer.String("diagnostic", DiagnosticCode);
            foreach (var retiredUnitId in RetiredUnitIds)
            {
                writer.String("retiredUnitId", retiredUnitId);
            }
            CanonicalSummary = writer.ToString();
        }

        public MatchCommandCode Code { get; }
        public bool Accepted =>
            Code == MatchCommandCode.Accepted
            || Code == MatchCommandCode.AcceptedNoChange
            || Code == MatchCommandCode.PoolExhaustedDiagnostic;
        public long CurrentStateRevision { get; }
        public long? AcceptedStateRevision { get; }
        public bool ChangedState { get; }
        public string DiagnosticCode { get; }
        public IReadOnlyList<string> RetiredUnitIds { get; }
        public string CanonicalSummary { get; }
    }
}
