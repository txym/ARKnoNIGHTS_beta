using ArknoNights.Match;

namespace ArknoNights.MatchAI
{
    public sealed class PendingTakeover
    {
        public PendingTakeover(
            string playerId,
            int disconnectedRound,
            MatchPhase disconnectedPhase,
            int effectivePreparationRound,
            bool reconnectAllowed)
        {
            PlayerId = playerId ?? string.Empty;
            DisconnectedRound = disconnectedRound;
            DisconnectedPhase = disconnectedPhase;
            EffectivePreparationRound = effectivePreparationRound;
            ReconnectAllowed = reconnectAllowed;
            CanonicalSummary = BotCanonical.Build(
                "PendingTakeover",
                PlayerId,
                DisconnectedRound,
                DisconnectedPhase,
                EffectivePreparationRound,
                ReconnectAllowed);
        }

        public string PlayerId { get; }
        public int DisconnectedRound { get; }
        public MatchPhase DisconnectedPhase { get; }
        public int EffectivePreparationRound { get; }
        public bool ReconnectAllowed { get; }
        public string CanonicalSummary { get; }
    }

    public sealed class BotLifecycleResult
    {
        internal BotLifecycleResult(
            MatchCommandCode code,
            string diagnosticCode,
            bool changed)
        {
            Code = code;
            DiagnosticCode = diagnosticCode ?? string.Empty;
            Changed = changed;
            CanonicalSummary = BotCanonical.Build(
                "BotLifecycleResult",
                Code,
                DiagnosticCode,
                Changed);
        }

        public MatchCommandCode Code { get; }
        public bool Accepted =>
            Code == MatchCommandCode.Accepted
            || Code == MatchCommandCode.AcceptedNoChange
            || Code == MatchCommandCode.PoolExhaustedDiagnostic;
        public string DiagnosticCode { get; }
        public bool Changed { get; }
        public string CanonicalSummary { get; }
    }
}
