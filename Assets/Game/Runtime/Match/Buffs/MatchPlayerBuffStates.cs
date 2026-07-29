using System;

namespace ArknoNights.Match
{
    public enum MatchTargetedBuffDiscardPolicy
    {
        Unspecified = 0,
        RemoveWithTarget = 1
    }

    public sealed class PlayerTargetedUnitBuffState
    {
        public PlayerTargetedUnitBuffState(
            string buffInstanceId,
            string buffTypeId,
            string targetUnitId,
            string canonicalPayload,
            MatchTargetedBuffDiscardPolicy discardPolicy)
        {
            BuffInstanceId = buffInstanceId;
            BuffTypeId = buffTypeId;
            TargetUnitId = targetUnitId;
            CanonicalPayload = canonicalPayload;
            DiscardPolicy = discardPolicy;
            CanonicalSummary = BuildCanonicalSummary();
        }

        public string BuffInstanceId { get; }
        public string BuffTypeId { get; }
        public string TargetUnitId { get; }
        public string CanonicalPayload { get; }
        public MatchTargetedBuffDiscardPolicy DiscardPolicy { get; }
        public string CanonicalSummary { get; }

        internal bool IsValid =>
            !string.IsNullOrWhiteSpace(BuffInstanceId)
            && !string.IsNullOrWhiteSpace(BuffTypeId)
            && !string.IsNullOrWhiteSpace(TargetUnitId)
            && CanonicalPayload != null
            && Enum.IsDefined(typeof(MatchTargetedBuffDiscardPolicy), DiscardPolicy);

        internal PlayerTargetedUnitBuffState WithTarget(string targetUnitId)
        {
            return new PlayerTargetedUnitBuffState(
                BuffInstanceId,
                BuffTypeId,
                targetUnitId,
                CanonicalPayload,
                DiscardPolicy);
        }

        private string BuildCanonicalSummary()
        {
            var writer = new CanonicalSummaryWriter(nameof(PlayerTargetedUnitBuffState));
            writer.String("buffInstanceId", BuffInstanceId);
            writer.String("buffTypeId", BuffTypeId);
            writer.String("targetUnitId", TargetUnitId);
            writer.String("payload", CanonicalPayload);
            writer.EnumValue("discardPolicy", DiscardPolicy);
            return writer.ToString();
        }
    }

    public sealed class PlayerGlobalBuffState
    {
        public PlayerGlobalBuffState(
            string buffInstanceId,
            string buffTypeId,
            string canonicalPayload)
        {
            BuffInstanceId = buffInstanceId;
            BuffTypeId = buffTypeId;
            CanonicalPayload = canonicalPayload;
            var writer = new CanonicalSummaryWriter(nameof(PlayerGlobalBuffState));
            writer.String("buffInstanceId", BuffInstanceId);
            writer.String("buffTypeId", BuffTypeId);
            writer.String("payload", CanonicalPayload);
            CanonicalSummary = writer.ToString();
        }

        public string BuffInstanceId { get; }
        public string BuffTypeId { get; }
        public string CanonicalPayload { get; }
        public string CanonicalSummary { get; }

        internal bool IsValid =>
            !string.IsNullOrWhiteSpace(BuffInstanceId)
            && !string.IsNullOrWhiteSpace(BuffTypeId)
            && CanonicalPayload != null;
    }

    public sealed class PlayerSourceEffectState
    {
        public PlayerSourceEffectState(
            string effectInstanceId,
            string effectTypeId,
            string canonicalPayload)
        {
            EffectInstanceId = effectInstanceId;
            EffectTypeId = effectTypeId;
            CanonicalPayload = canonicalPayload;
            var writer = new CanonicalSummaryWriter(nameof(PlayerSourceEffectState));
            writer.String("effectInstanceId", EffectInstanceId);
            writer.String("effectTypeId", EffectTypeId);
            writer.String("payload", CanonicalPayload);
            CanonicalSummary = writer.ToString();
        }

        public string EffectInstanceId { get; }
        public string EffectTypeId { get; }
        public string CanonicalPayload { get; }
        public string CanonicalSummary { get; }

        internal bool IsValid =>
            !string.IsNullOrWhiteSpace(EffectInstanceId)
            && !string.IsNullOrWhiteSpace(EffectTypeId)
            && CanonicalPayload != null;
    }
}
