using System;
using System.Globalization;

namespace ArknoNights.Match
{
    public abstract class MatchCommandPayload
    {
        public abstract string CanonicalSummary { get; }
    }

    public sealed class SetPreparationReadyCommand : MatchCommandPayload
    {
        public SetPreparationReadyCommand(bool desiredReady)
        {
            DesiredReady = desiredReady;
            var writer = new CanonicalSummaryWriter(nameof(SetPreparationReadyCommand));
            writer.Boolean("desiredReady", DesiredReady);
            CanonicalSummary = writer.ToString();
        }

        public bool DesiredReady { get; }
        public override string CanonicalSummary { get; }
    }

    public sealed class RefreshShopCommand : MatchCommandPayload
    {
        public RefreshShopCommand()
        {
            var writer = new CanonicalSummaryWriter(nameof(RefreshShopCommand));
            CanonicalSummary = writer.ToString();
        }

        public override string CanonicalSummary { get; }
    }

    public sealed class ToggleShopFreezeCommand : MatchCommandPayload
    {
        public ToggleShopFreezeCommand()
        {
            var writer = new CanonicalSummaryWriter(nameof(ToggleShopFreezeCommand));
            CanonicalSummary = writer.ToString();
        }

        public override string CanonicalSummary { get; }
    }

    public sealed class PurchaseShopOfferCommand : MatchCommandPayload
    {
        public PurchaseShopOfferCommand(int slotIndex, string expectedUnitId)
        {
            SlotIndex = slotIndex;
            ExpectedUnitId = expectedUnitId;
            var writer = new CanonicalSummaryWriter(nameof(PurchaseShopOfferCommand));
            writer.Integer("slotIndex", SlotIndex);
            writer.String("expectedUnitId", ExpectedUnitId);
            CanonicalSummary = writer.ToString();
        }

        public int SlotIndex { get; }
        public string ExpectedUnitId { get; }
        public override string CanonicalSummary { get; }
    }

    public sealed class PurchaseLevelUpgradeCommand : MatchCommandPayload
    {
        public PurchaseLevelUpgradeCommand(int expectedCurrentLevel, int expectedCurrentPrice)
        {
            ExpectedCurrentLevel = expectedCurrentLevel;
            ExpectedCurrentPrice = expectedCurrentPrice;
            var writer = new CanonicalSummaryWriter(nameof(PurchaseLevelUpgradeCommand));
            writer.Integer("expectedCurrentLevel", ExpectedCurrentLevel);
            writer.Integer("expectedCurrentPrice", ExpectedCurrentPrice);
            CanonicalSummary = writer.ToString();
        }

        public int ExpectedCurrentLevel { get; }
        public int ExpectedCurrentPrice { get; }
        public override string CanonicalSummary { get; }
    }

    public sealed class MatchCommandEnvelope
    {
        public MatchCommandEnvelope(
            string sessionId,
            string playerId,
            string commandId,
            long knownStateRevision,
            MatchCommandPayload payload)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            CommandId = commandId;
            KnownStateRevision = knownStateRevision;
            Payload = payload;

            var contentWriter = new CanonicalSummaryWriter("MatchCommandContent");
            contentWriter.String("sessionId", SessionId);
            contentWriter.String("playerId", PlayerId);
            contentWriter.Integer("knownRevision", KnownStateRevision);
            contentWriter.Summary("payload", Payload == null ? string.Empty : Payload.CanonicalSummary);
            CanonicalContentSummary = contentWriter.ToString();

            var writer = new CanonicalSummaryWriter(nameof(MatchCommandEnvelope));
            writer.String("commandId", CommandId);
            writer.Summary("content", CanonicalContentSummary);
            CanonicalSummary = writer.ToString();
        }

        public string SessionId { get; }
        public string PlayerId { get; }
        public string CommandId { get; }
        public long KnownStateRevision { get; }
        public MatchCommandPayload Payload { get; }
        public string CanonicalContentSummary { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class MatchCommandResult
    {
        internal MatchCommandResult(
            string commandId,
            MatchCommandCode code,
            long currentStateRevision,
            long? acceptedStateRevision,
            bool changedState,
            string diagnosticCode)
        {
            CommandId = commandId ?? string.Empty;
            Code = code;
            CurrentStateRevision = currentStateRevision;
            AcceptedStateRevision = acceptedStateRevision;
            ChangedState = changedState;
            DiagnosticCode = diagnosticCode ?? string.Empty;

            var writer = new CanonicalSummaryWriter(nameof(MatchCommandResult));
            writer.String("commandId", CommandId);
            writer.EnumValue("code", Code);
            writer.Integer("currentRevision", CurrentStateRevision);
            writer.String("acceptedRevision", AcceptedStateRevision.HasValue
                ? AcceptedStateRevision.Value.ToString(CultureInfo.InvariantCulture)
                : "null");
            writer.Boolean("changed", ChangedState);
            writer.String("diagnostic", DiagnosticCode);
            CanonicalSummary = writer.ToString();
        }

        public string CommandId { get; }
        public MatchCommandCode Code { get; }
        public bool Accepted =>
            Code == MatchCommandCode.Accepted
            || Code == MatchCommandCode.AcceptedNoChange
            || Code == MatchCommandCode.PoolExhaustedDiagnostic;
        public long CurrentStateRevision { get; }
        public long? AcceptedStateRevision { get; }
        public bool ChangedState { get; }
        public string DiagnosticCode { get; }
        public string CanonicalSummary { get; }
    }
}
