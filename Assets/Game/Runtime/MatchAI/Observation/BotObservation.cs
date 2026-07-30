using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using ArknoNights.Match;

namespace ArknoNights.MatchAI
{
    public sealed class BotShopOfferObservation
    {
        public BotShopOfferObservation(
            int slotIndex,
            string unitId,
            string typeId,
            int? price,
            int? baseElite0DeploymentCost,
            bool isFrozen)
        {
            SlotIndex = slotIndex;
            UnitId = unitId ?? string.Empty;
            TypeId = typeId ?? string.Empty;
            Price = price;
            BaseElite0DeploymentCost = baseElite0DeploymentCost;
            IsFrozen = isFrozen;
            CanonicalSummary = BotCanonical.Build(
                "BotShopOffer",
                SlotIndex,
                UnitId,
                TypeId,
                Price,
                BaseElite0DeploymentCost,
                IsFrozen);
        }

        public int SlotIndex { get; }
        public string UnitId { get; }
        public string TypeId { get; }
        public int? Price { get; }
        public int? BaseElite0DeploymentCost { get; }
        public bool IsFrozen { get; }
        public bool IsEmpty => string.IsNullOrEmpty(UnitId);
        public string CanonicalSummary { get; }

        public static BotShopOfferObservation Empty(int slotIndex)
        {
            return new BotShopOfferObservation(
                slotIndex,
                string.Empty,
                string.Empty,
                null,
                null,
                false);
        }
    }

    public sealed class BotUnitObservation
    {
        public BotUnitObservation(
            string unitId,
            string typeId,
            MatchUnitZone zone,
            int eliteLevel)
        {
            UnitId = unitId ?? string.Empty;
            TypeId = typeId ?? string.Empty;
            Zone = zone;
            EliteLevel = eliteLevel;
            CanonicalSummary = BotCanonical.Build(
                "BotUnit",
                UnitId,
                TypeId,
                Zone,
                EliteLevel);
        }

        public string UnitId { get; }
        public string TypeId { get; }
        public MatchUnitZone Zone { get; }
        public int EliteLevel { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class BotFormationObservation
    {
        public BotFormationObservation(string unitId, int x, int y)
        {
            UnitId = unitId ?? string.Empty;
            X = x;
            Y = y;
            CanonicalSummary = BotCanonical.Build("BotFormation", UnitId, X, Y);
        }

        public string UnitId { get; }
        public int X { get; }
        public int Y { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class BotHumanReadinessObservation
    {
        public BotHumanReadinessObservation(
            string playerId,
            bool ready,
            bool connected,
            bool eliminated)
        {
            PlayerId = playerId ?? string.Empty;
            Ready = ready;
            Connected = connected;
            Eliminated = eliminated;
            CanonicalSummary = BotCanonical.Build(
                "BotHumanReadiness",
                PlayerId,
                Ready,
                Connected,
                Eliminated);
        }

        public string PlayerId { get; }
        public bool Ready { get; }
        public bool Connected { get; }
        public bool Eliminated { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class BotObservation
    {
        public BotObservation(
            string sessionId,
            long stateRevision,
            int roundNumber,
            MatchPhase phase,
            int seatIndex,
            string playerId,
            MatchControllerKind controllerKind,
            bool isEliminated,
            int gold,
            int level,
            int? currentUpgradePrice,
            int totalDeploymentCost,
            int availableDeploymentCost,
            int stagingSlotUsage,
            IEnumerable<BotShopOfferObservation> shopOffers,
            IEnumerable<BotUnitObservation> ownUnits,
            IEnumerable<BotFormationObservation> ownFormation,
            IEnumerable<BotHumanReadinessObservation> publicHumanReadiness,
            long hostMonotonicNowMs,
            long preparationDeadlineHostMs,
            long decisionOrdinal)
        {
            SessionId = sessionId ?? string.Empty;
            StateRevision = stateRevision;
            RoundNumber = roundNumber;
            Phase = phase;
            SeatIndex = seatIndex;
            PlayerId = playerId ?? string.Empty;
            ControllerKind = controllerKind;
            IsEliminated = isEliminated;
            Gold = gold;
            Level = level;
            CurrentUpgradePrice = currentUpgradePrice;
            TotalDeploymentCost = totalDeploymentCost;
            AvailableDeploymentCost = availableDeploymentCost;
            StagingSlotUsage = stagingSlotUsage;
            ShopOffers = new ReadOnlyCollection<BotShopOfferObservation>(
                (shopOffers ?? Enumerable.Empty<BotShopOfferObservation>())
                    .OrderBy(offer => offer == null ? int.MinValue : offer.SlotIndex)
                    .ToArray());
            OwnUnits = new ReadOnlyCollection<BotUnitObservation>(
                (ownUnits ?? Enumerable.Empty<BotUnitObservation>())
                    .OrderBy(
                        unit => unit == null ? string.Empty : unit.UnitId,
                        StringComparer.Ordinal)
                    .ToArray());
            OwnFormation = new ReadOnlyCollection<BotFormationObservation>(
                (ownFormation ?? Enumerable.Empty<BotFormationObservation>())
                    .OrderBy(item => item == null ? int.MinValue : item.Y)
                    .ThenBy(item => item == null ? int.MinValue : item.X)
                    .ThenBy(item => item == null ? string.Empty : item.UnitId, StringComparer.Ordinal)
                    .ToArray());
            PublicHumanReadiness =
                new ReadOnlyCollection<BotHumanReadinessObservation>(
                    (publicHumanReadiness
                        ?? Enumerable.Empty<BotHumanReadinessObservation>())
                        .OrderBy(
                            human => human == null
                                ? string.Empty
                                : human.PlayerId,
                            StringComparer.Ordinal)
                        .ToArray());
            HostMonotonicNowMs = hostMonotonicNowMs;
            PreparationDeadlineHostMs = preparationDeadlineHostMs;
            DecisionOrdinal = decisionOrdinal;
            CanonicalSummary = BuildCanonicalSummary();
        }

        public string SessionId { get; }
        public long StateRevision { get; }
        public int RoundNumber { get; }
        public MatchPhase Phase { get; }
        public int SeatIndex { get; }
        public string PlayerId { get; }
        public MatchControllerKind ControllerKind { get; }
        public bool IsEliminated { get; }
        public int Gold { get; }
        public int Level { get; }
        public int? CurrentUpgradePrice { get; }
        public int TotalDeploymentCost { get; }
        public int AvailableDeploymentCost { get; }
        public int StagingSlotUsage { get; }
        public IReadOnlyList<BotShopOfferObservation> ShopOffers { get; }
        public IReadOnlyList<BotUnitObservation> OwnUnits { get; }
        public IReadOnlyList<BotFormationObservation> OwnFormation { get; }
        public IReadOnlyList<BotHumanReadinessObservation> PublicHumanReadiness { get; }
        public long HostMonotonicNowMs { get; }
        public long PreparationDeadlineHostMs { get; }
        public long DecisionOrdinal { get; }
        public string CanonicalSummary { get; }

        public bool AllRequiredHumansReady
        {
            get
            {
                var required = PublicHumanReadiness
                    .Where(human => human != null && !human.Eliminated)
                    .ToArray();
                return required.Length > 0
                    && required.All(human => human.Connected && human.Ready);
            }
        }

        public bool TryValidate(out string diagnosticCode)
        {
            if (string.IsNullOrWhiteSpace(SessionId)
                || string.IsNullOrWhiteSpace(PlayerId)
                || StateRevision < 0
                || RoundNumber < 1
                || SeatIndex < 1
                || SeatIndex > 4
                || Gold < 0
                || Level < 1
                || Level > 9
                || TotalDeploymentCost < 0
                || AvailableDeploymentCost < 0
                || AvailableDeploymentCost > TotalDeploymentCost
                || StagingSlotUsage < 0
                || StagingSlotUsage > 13
                || DecisionOrdinal < 0
                || !Enum.IsDefined(typeof(MatchPhase), Phase)
                || !Enum.IsDefined(typeof(MatchControllerKind), ControllerKind))
            {
                diagnosticCode = "match.ai.observation.scalar.invalid";
                return false;
            }
            if ((Level == 9 && CurrentUpgradePrice.HasValue)
                || (Level < 9 && (!CurrentUpgradePrice.HasValue || CurrentUpgradePrice.Value < 0)))
            {
                diagnosticCode = "match.ai.observation.upgrade.invalid";
                return false;
            }
            if (ShopOffers.Count != 6
                || ShopOffers.Any(offer => offer == null)
                || ShopOffers.Select(offer => offer.SlotIndex).Distinct().Count() != 6
                || ShopOffers.Any(offer => offer.SlotIndex < 1 || offer.SlotIndex > 6))
            {
                diagnosticCode = "match.ai.observation.shop.invalid";
                return false;
            }
            foreach (var offer in ShopOffers.Where(offer => !offer.IsEmpty))
            {
                if (string.IsNullOrWhiteSpace(offer.TypeId)
                    || !offer.Price.HasValue
                    || offer.Price.Value < 1)
                {
                    diagnosticCode = "match.ai.observation.offer.invalid";
                    return false;
                }
                if (!offer.BaseElite0DeploymentCost.HasValue
                    || offer.BaseElite0DeploymentCost.Value < 0)
                {
                    diagnosticCode = "match.ai.observation.offer.baseCost.invalid";
                    return false;
                }
            }
            if (OwnUnits.Any(unit =>
                    unit == null
                    || string.IsNullOrWhiteSpace(unit.UnitId)
                    || string.IsNullOrWhiteSpace(unit.TypeId)
                    || !Enum.IsDefined(typeof(MatchUnitZone), unit.Zone)
                    || unit.EliteLevel < 0
                    || unit.EliteLevel > 3)
                || OwnUnits.Where(unit => unit != null)
                    .Select(unit => unit.UnitId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != OwnUnits.Count)
            {
                diagnosticCode = "match.ai.observation.unit.invalid";
                return false;
            }
            if (OwnFormation.Any(item =>
                    item == null
                    || string.IsNullOrWhiteSpace(item.UnitId)
                    || item.X < 1
                    || item.X > 9
                    || item.Y < 1
                    || item.Y > 4
                    || (item.X == 5 && item.Y == 1)))
            {
                diagnosticCode = "match.ai.observation.formation.invalid";
                return false;
            }
            if (PublicHumanReadiness.Any(human =>
                    human == null || string.IsNullOrWhiteSpace(human.PlayerId)))
            {
                diagnosticCode = "match.ai.observation.human.invalid";
                return false;
            }
            diagnosticCode = string.Empty;
            return true;
        }

        private string BuildCanonicalSummary()
        {
            var builder = new StringBuilder();
            builder.Append(BotCanonical.Build(
                "BotObservation",
                SessionId,
                StateRevision,
                RoundNumber,
                Phase,
                SeatIndex,
                PlayerId,
                ControllerKind,
                IsEliminated,
                Gold,
                Level,
                CurrentUpgradePrice,
                TotalDeploymentCost,
                AvailableDeploymentCost,
                StagingSlotUsage,
                HostMonotonicNowMs,
                PreparationDeadlineHostMs,
                DecisionOrdinal));
            foreach (var offer in ShopOffers) builder.Append('|').Append(offer?.CanonicalSummary ?? string.Empty);
            foreach (var unit in OwnUnits) builder.Append('|').Append(unit?.CanonicalSummary ?? string.Empty);
            foreach (var item in OwnFormation) builder.Append('|').Append(item?.CanonicalSummary ?? string.Empty);
            foreach (var human in PublicHumanReadiness) builder.Append('|').Append(human?.CanonicalSummary ?? string.Empty);
            return builder.ToString();
        }
    }

    internal static class BotCanonical
    {
        internal static string Build(string kind, params object[] values)
        {
            var builder = new StringBuilder(kind ?? string.Empty);
            foreach (var value in values ?? Array.Empty<object>())
            {
                builder.Append('|');
                if (value == null)
                {
                    builder.Append('~');
                }
                else if (value is bool boolean)
                {
                    builder.Append(boolean ? '1' : '0');
                }
                else if (value is Enum)
                {
                    builder.Append(value.ToString());
                }
                else if (value is IFormattable formattable)
                {
                    builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                }
                else
                {
                    var text = value.ToString() ?? string.Empty;
                    builder.Append(text.Length.ToString(CultureInfo.InvariantCulture))
                        .Append(':')
                        .Append(text);
                }
            }
            return builder.ToString();
        }
    }
}
