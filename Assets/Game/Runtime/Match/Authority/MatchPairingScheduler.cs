using System;
using System.Collections.Generic;
using System.Linq;

namespace ArknoNights.Match
{
    internal sealed class MatchPairingSchedule
    {
        internal MatchPairingSchedule(
            int generation,
            int population,
            int offset,
            IReadOnlyList<string> seatOrder,
            IReadOnlyList<MatchSealedPairing> pairings,
            MatchPairingDiagnostics diagnostics)
        {
            Generation = generation;
            Population = population;
            Offset = offset;
            SeatOrder = seatOrder;
            Pairings = pairings;
            Diagnostics = diagnostics;
        }

        internal int Generation { get; }
        internal int Population { get; }
        internal int Offset { get; }
        internal IReadOnlyList<string> SeatOrder { get; }
        internal IReadOnlyList<MatchSealedPairing> Pairings { get; }
        internal MatchPairingDiagnostics Diagnostics { get; }
    }

    internal static class MatchPairingScheduler
    {
        private const string DerivationVersion = "match-pairing-v1";

        internal static bool TryBuild(
            MatchState state,
            out MatchPairingSchedule schedule,
            out string diagnosticCode)
        {
            schedule = null;
            var alive = state.Seats
                .Where(seat => !seat.Eliminated)
                .OrderBy(seat => seat.SeatIndex)
                .ToArray();
            if (alive.Length < 2 || alive.Length > 4)
            {
                diagnosticCode = "match.pairing.population.invalid";
                return false;
            }

            var populationChanged = state.Flow.PairingPopulation != alive.Length
                || state.Flow.PairingSeatOrder.Count != alive.Length
                || state.Flow.PairingSeatOrder.Any(
                    playerId => alive.All(seat =>
                        !string.Equals(seat.PlayerId, playerId, StringComparison.Ordinal)));
            var generation = populationChanged
                ? checked(state.Flow.PairingGeneration + 1)
                : Math.Max(1, state.Flow.PairingGeneration);
            var cycleLength = alive.Length == 4 ? 6 : alive.Length == 3 ? 6 : 2;
            string[] seatOrder;
            int offset;
            MatchPairingDiagnostics diagnostics;
            if (!populationChanged)
            {
                seatOrder = state.Flow.PairingSeatOrder.ToArray();
                offset = (state.Flow.PairingOffset + 1) % cycleLength;
                diagnostics = new MatchPairingDiagnostics(
                    generation,
                    alive.Length,
                    offset,
                    seatOrder,
                    Array.Empty<string>());
            }
            else if (alive.Length == 4 && state.Flow.PairingGeneration == 0)
            {
                seatOrder = alive.Select(seat => seat.PlayerId).ToArray();
                offset = (state.RoundNumber - 1) % cycleLength;
                diagnostics = new MatchPairingDiagnostics(
                    generation,
                    alive.Length,
                    offset,
                    seatOrder,
                    Array.Empty<string>());
            }
            else
            {
                var choice = SelectTransitionCandidate(
                    state.Flow.PairingHistory,
                    alive.Select(seat => seat.PlayerId).ToArray(),
                    alive.ToDictionary(
                        seat => seat.PlayerId,
                        seat => seat.SeatIndex,
                        StringComparer.Ordinal),
                    alive.Length);
                seatOrder = choice.SeatOrder;
                offset = choice.Offset;
                diagnostics = new MatchPairingDiagnostics(
                    generation,
                    alive.Length,
                    offset,
                    seatOrder,
                    choice.RelaxedConstraints);
            }

            var templates = BuildTemplates(seatOrder, alive.Length, offset);
            var snapshots = alive.ToDictionary(
                seat => seat.PlayerId,
                seat => new MatchPlayerCombatSnapshot(
                    seat.PlayerId,
                    seat.Units.Where(unit => unit.Zone == MatchUnitZone.Deployed),
                    seat.TargetedUnitBuffs,
                    seat.GlobalBuffs,
                    seat.SourceEffects),
                StringComparer.Ordinal);
            var pairings = new List<MatchSealedPairing>();
            for (var index = 0; index < templates.Count; index++)
            {
                var template = templates[index];
                var battleId = DeriveIdentity(
                    "battle-id",
                    state,
                    index,
                    generation);
                var battleSeed = DeriveIdentity(
                    "battle-seed",
                    state,
                    index,
                    generation);
                var unsealed = new MatchSealedPairing(
                    battleId,
                    index,
                    template.Kind,
                    template.HomePlayerId,
                    template.AwayPlayerId,
                    template.ShadowOwnerPlayerId,
                    template.SettlementRecipients,
                    battleSeed,
                    string.Empty);
                var hashWriter = new CanonicalSummaryWriter("MatchSealedBattleInput");
                hashWriter.Summary("pairing", unsealed.BuildCanonicalSummary(includeHash: false));
                hashWriter.Summary("home", snapshots[template.HomePlayerId].CanonicalSummary);
                hashWriter.Summary("away", snapshots[template.AwayPlayerId].CanonicalSummary);
                hashWriter.String("rules", state.CompatibilityManifest.MatchRulesVersion);
                hashWriter.String("unitCatalog", state.CompatibilityManifest.UnitCatalogSha256);
                hashWriter.String("abilityCatalog", state.CompatibilityManifest.AbilityCatalogSha256);
                pairings.Add(new MatchSealedPairing(
                    battleId,
                    index,
                    template.Kind,
                    template.HomePlayerId,
                    template.AwayPlayerId,
                    template.ShadowOwnerPlayerId,
                    template.SettlementRecipients,
                    battleSeed,
                    MatchFlowHash.Sha256(hashWriter.ToString())));
            }

            if (!RecipientsAreExact(alive, pairings))
            {
                diagnosticCode = "match.pairing.recipients.invalid";
                return false;
            }

            schedule = new MatchPairingSchedule(
                generation,
                alive.Length,
                offset,
                seatOrder,
                pairings,
                diagnostics);
            diagnosticCode = string.Empty;
            return true;
        }

        internal static MatchSealedRoundPlan BuildPlan(
            MatchState state,
            MatchPairingSchedule schedule)
        {
            var snapshots = state.Seats
                .Where(seat => !seat.Eliminated)
                .Select(seat => new MatchPlayerCombatSnapshot(
                    seat.PlayerId,
                    seat.Units.Where(unit => unit.Zone == MatchUnitZone.Deployed),
                    seat.TargetedUnitBuffs,
                    seat.GlobalBuffs,
                    seat.SourceEffects))
                .ToArray();
            var unsealed = new MatchSealedRoundPlan(
                state.SessionId,
                state.RoundNumber,
                schedule.Generation,
                schedule.Pairings,
                snapshots,
                state.CompatibilityManifest.MatchRulesVersion,
                state.CompatibilityManifest.UnitCatalogSha256,
                state.CompatibilityManifest.AbilityCatalogSha256,
                string.Empty);
            return new MatchSealedRoundPlan(
                unsealed.SessionId,
                unsealed.RoundNumber,
                unsealed.PairingGeneration,
                unsealed.Pairings,
                unsealed.PlayerCombatSnapshots,
                unsealed.MatchRulesVersion,
                unsealed.UnitCatalogSha256,
                unsealed.AbilityCatalogSha256,
                MatchFlowHash.Sha256(unsealed.BuildCanonicalSummary(includeHash: false)));
        }

        private static TransitionChoice SelectTransitionCandidate(
            IReadOnlyList<MatchPairingHistoryEntry> history,
            IReadOnlyList<string> normalizedSeatOrder,
            IReadOnlyDictionary<string, int> seatIndexByPlayer,
            int population)
        {
            var cycleLength = population == 3 ? 6 : population == 2 ? 2 : 6;
            var mappings = population == 3
                ? Permutations(normalizedSeatOrder).ToArray()
                : new[] { normalizedSeatOrder.ToArray() };
            var candidates = mappings
                .SelectMany(mapping => Enumerable.Range(0, cycleLength)
                    .Select(offset => new TransitionChoice(
                        mapping,
                        string.Join(
                            "\u001f",
                            mapping.Select(playerId => seatIndexByPlayer[playerId].ToString(
                                System.Globalization.CultureInfo.InvariantCulture))),
                        offset,
                        BuildTemplates(mapping, population, offset),
                        history)))
                .GroupBy(choice => choice.TemplateSignature, StringComparer.Ordinal)
                .Select(group => group
                    .OrderBy(choice => choice.Offset)
                    .ThenBy(choice => choice.MappingSignature, StringComparer.Ordinal)
                    .First())
                .ToList();
            var relaxed = new List<string>();
            var minimumRepeatedPairs = candidates.Min(
                candidate => candidate.RepeatedPreviousRoundPairs);
            candidates = candidates
                .Where(candidate =>
                    candidate.RepeatedPreviousRoundPairs == minimumRepeatedPairs)
                .ToList();
            var withoutRepeatedOnlyShadow = candidates
                .Where(candidate => !candidate.RepeatsOnlyShadowRecipient)
                .ToList();
            if (withoutRepeatedOnlyShadow.Count > 0)
            {
                candidates = withoutRepeatedOnlyShadow;
            }
            else
            {
                relaxed.Add("consecutive-only-shadow-unavoidable");
            }
            var withoutThirdRole = candidates
                .Where(candidate => !candidate.CreatesThirdConsecutiveRole)
                .ToList();
            if (withoutThirdRole.Count > 0)
            {
                candidates = withoutThirdRole;
            }
            else
            {
                relaxed.Add("third-consecutive-role-unavoidable");
            }
            var selected = candidates
                .OrderBy(candidate => candidate.MaximumRoleStreak)
                .ThenBy(candidate => candidate.MaximumRoleImbalance)
                .ThenBy(candidate => candidate.TotalRoleImbalance)
                .ThenBy(candidate => candidate.ShadowAssignmentImbalance)
                .ThenBy(candidate => candidate.Offset)
                .ThenBy(candidate => candidate.MappingSignature, StringComparer.Ordinal)
                .First();
            selected.RelaxedConstraints = relaxed;
            return selected;
        }

        private static IEnumerable<string[]> Permutations(IReadOnlyList<string> items)
        {
            for (var first = 0; first < items.Count; first++)
            {
                for (var second = 0; second < items.Count; second++)
                {
                    if (second == first)
                    {
                        continue;
                    }
                    for (var third = 0; third < items.Count; third++)
                    {
                        if (third != first && third != second)
                        {
                            yield return new[] { items[first], items[second], items[third] };
                        }
                    }
                }
            }
        }

        private static IReadOnlyList<PairingTemplate> BuildTemplates(
            IReadOnlyList<string> seats,
            int population,
            int offset)
        {
            if (population == 4)
            {
                var table = new[]
                {
                    new[] { 0, 1, 2, 3 },
                    new[] { 0, 2, 1, 3 },
                    new[] { 3, 0, 2, 1 },
                    new[] { 1, 0, 3, 2 },
                    new[] { 0, 3, 1, 2 },
                    new[] { 2, 0, 3, 1 }
                };
                var row = table[offset % 6];
                return new[]
                {
                    Official(seats[row[0]], seats[row[1]]),
                    Official(seats[row[2]], seats[row[3]])
                };
            }
            if (population == 3)
            {
                var a = seats[0];
                var b = seats[1];
                var c = seats[2];
                switch (offset % 6)
                {
                    case 0: return Three(Official(a, b), Shadow(c, a, a));
                    case 1: return Three(Official(b, c), Shadow(a, b, b));
                    case 2: return Three(Official(c, a), Shadow(b, c, c));
                    case 3: return Three(Official(b, a), Shadow(a, c, a));
                    case 4: return Three(Official(c, b), Shadow(b, a, b));
                    default: return Three(Official(a, c), Shadow(c, b, c));
                }
            }
            return new[]
            {
                offset % 2 == 0
                    ? Official(seats[0], seats[1])
                    : Official(seats[1], seats[0])
            };
        }

        private static PairingTemplate[] Three(
            PairingTemplate official,
            PairingTemplate shadow)
        {
            return new[] { official, shadow };
        }

        private static PairingTemplate Official(string home, string away)
        {
            return new PairingTemplate(
                MatchPairingKind.Official,
                home,
                away,
                string.Empty,
                new[] { home, away });
        }

        private static PairingTemplate Shadow(string home, string away, string owner)
        {
            var recipient = string.Equals(home, owner, StringComparison.Ordinal)
                ? away
                : home;
            return new PairingTemplate(
                MatchPairingKind.Shadow,
                home,
                away,
                owner,
                new[] { recipient });
        }

        private static bool RecipientsAreExact(
            IEnumerable<MatchSeatState> alive,
            IEnumerable<MatchSealedPairing> pairings)
        {
            var counts = pairings
                .SelectMany(pairing => pairing.SettlementRecipients)
                .GroupBy(playerId => playerId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            return alive.All(seat =>
                counts.TryGetValue(seat.PlayerId, out var count) && count == 1);
        }

        private static string DeriveIdentity(
            string purpose,
            MatchState state,
            int battleIndex,
            int generation)
        {
            var writer = new CanonicalSummaryWriter("MatchPairingIdentity");
            writer.String("version", DerivationVersion);
            writer.String("purpose", purpose);
            writer.String("sessionId", state.SessionId);
            writer.Integer("round", state.RoundNumber);
            writer.Integer("battleIndex", battleIndex);
            writer.Integer("generation", generation);
            return MatchFlowHash.Sha256(writer.ToString());
        }

        private sealed class TransitionChoice
        {
            internal TransitionChoice(
                IReadOnlyList<string> seatOrder,
                string seatIndexMappingSignature,
                int offset,
                IReadOnlyList<PairingTemplate> templates,
                IReadOnlyList<MatchPairingHistoryEntry> history)
            {
                SeatOrder = seatOrder.ToArray();
                Offset = offset;
                MappingSignature = seatIndexMappingSignature;
                TemplateSignature = string.Join(
                    "\u001e",
                    templates.Select(template =>
                        ((int)template.Kind).ToString(
                            System.Globalization.CultureInfo.InvariantCulture)
                        + "\u001f" + template.HomePlayerId
                        + "\u001f" + template.AwayPlayerId
                        + "\u001f" + template.ShadowOwnerPlayerId));

                var lastRound = history.Count == 0
                    ? int.MinValue
                    : history.Max(item => item.RoundNumber);
                var previousRound = history
                    .Where(item => item.RoundNumber == lastRound)
                    .ToArray();
                RepeatedPreviousRoundPairs = templates.Count(template =>
                    previousRound.Any(previous => SamePair(
                        template.HomePlayerId,
                        template.AwayPlayerId,
                        previous.HomePlayerId,
                        previous.AwayPlayerId)));

                var previousOnlyShadow = new HashSet<string>(
                    previousRound
                        .Where(item => item.Kind == MatchPairingKind.Shadow)
                        .Select(ShadowRecipient),
                    StringComparer.Ordinal);
                var nextOnlyShadow = templates
                    .Where(template => template.Kind == MatchPairingKind.Shadow)
                    .Select(ShadowRecipient)
                    .ToArray();
                RepeatsOnlyShadowRecipient = nextOnlyShadow.Any(
                    playerId => previousOnlyShadow.Contains(playerId));

                var allPlayers = SeatOrder;
                var maximumRoleStreak = 0;
                var maximumRoleImbalance = 0;
                var totalRoleImbalance = 0;
                foreach (var playerId in allPlayers)
                {
                    var roles = history
                        .OrderBy(item => item.RoundNumber)
                        .ThenBy(item => item.BattleIndex)
                        .SelectMany(item => RolesFor(item, playerId))
                        .Concat(templates.SelectMany(
                            template => RolesFor(template, playerId)))
                        .ToArray();
                    maximumRoleStreak = Math.Max(
                        maximumRoleStreak,
                        TrailingRunLength(roles));
                    var homeCount = roles.Count(role => role == MatchBattleRole.Home);
                    var awayCount = roles.Length - homeCount;
                    var imbalance = Math.Abs(homeCount - awayCount);
                    maximumRoleImbalance = Math.Max(maximumRoleImbalance, imbalance);
                    totalRoleImbalance += imbalance;
                }
                MaximumRoleStreak = maximumRoleStreak;
                CreatesThirdConsecutiveRole = maximumRoleStreak >= 3;
                MaximumRoleImbalance = maximumRoleImbalance;
                TotalRoleImbalance = totalRoleImbalance;

                var ownerCounts = allPlayers.ToDictionary(
                    playerId => playerId,
                    playerId => history.Count(item =>
                        item.Kind == MatchPairingKind.Shadow
                        && string.Equals(
                            item.ShadowOwnerPlayerId,
                            playerId,
                            StringComparison.Ordinal))
                        + templates.Count(template =>
                            template.Kind == MatchPairingKind.Shadow
                            && string.Equals(
                                template.ShadowOwnerPlayerId,
                                playerId,
                                StringComparison.Ordinal)),
                    StringComparer.Ordinal);
                var recipientCounts = allPlayers.ToDictionary(
                    playerId => playerId,
                    playerId => history.Count(item =>
                        item.Kind == MatchPairingKind.Shadow
                        && string.Equals(
                            ShadowRecipient(item),
                            playerId,
                            StringComparison.Ordinal))
                        + templates.Count(template =>
                            template.Kind == MatchPairingKind.Shadow
                            && string.Equals(
                                ShadowRecipient(template),
                                playerId,
                                StringComparison.Ordinal)),
                    StringComparer.Ordinal);
                ShadowAssignmentImbalance =
                    (ownerCounts.Values.Max() - ownerCounts.Values.Min())
                    + (recipientCounts.Values.Max() - recipientCounts.Values.Min());
                RelaxedConstraints = Array.Empty<string>();
            }

            internal string[] SeatOrder { get; }
            internal int Offset { get; }
            internal string MappingSignature { get; }
            internal string TemplateSignature { get; }
            internal int RepeatedPreviousRoundPairs { get; }
            internal bool RepeatsOnlyShadowRecipient { get; }
            internal int MaximumRoleStreak { get; }
            internal bool CreatesThirdConsecutiveRole { get; }
            internal int MaximumRoleImbalance { get; }
            internal int TotalRoleImbalance { get; }
            internal int ShadowAssignmentImbalance { get; }
            internal IReadOnlyList<string> RelaxedConstraints { get; set; }

            private static bool SamePair(
                string firstHome,
                string firstAway,
                string secondHome,
                string secondAway)
            {
                return string.Equals(firstHome, secondHome, StringComparison.Ordinal)
                        && string.Equals(firstAway, secondAway, StringComparison.Ordinal)
                    || string.Equals(firstHome, secondAway, StringComparison.Ordinal)
                        && string.Equals(firstAway, secondHome, StringComparison.Ordinal);
            }

            private static string ShadowRecipient(MatchPairingHistoryEntry item)
            {
                return string.Equals(
                    item.HomePlayerId,
                    item.ShadowOwnerPlayerId,
                    StringComparison.Ordinal)
                    ? item.AwayPlayerId
                    : item.HomePlayerId;
            }

            private static string ShadowRecipient(PairingTemplate item)
            {
                return item.SettlementRecipients.Single();
            }

            private static IEnumerable<MatchBattleRole> RolesFor(
                MatchPairingHistoryEntry item,
                string playerId)
            {
                if (string.Equals(item.HomePlayerId, playerId, StringComparison.Ordinal))
                {
                    yield return MatchBattleRole.Home;
                }
                if (string.Equals(item.AwayPlayerId, playerId, StringComparison.Ordinal))
                {
                    yield return MatchBattleRole.Away;
                }
            }

            private static IEnumerable<MatchBattleRole> RolesFor(
                PairingTemplate item,
                string playerId)
            {
                if (string.Equals(item.HomePlayerId, playerId, StringComparison.Ordinal))
                {
                    yield return MatchBattleRole.Home;
                }
                if (string.Equals(item.AwayPlayerId, playerId, StringComparison.Ordinal))
                {
                    yield return MatchBattleRole.Away;
                }
            }

            private static int TrailingRunLength(IReadOnlyList<MatchBattleRole> roles)
            {
                if (roles.Count == 0)
                {
                    return 0;
                }
                var last = roles[roles.Count - 1];
                var length = 1;
                for (var index = roles.Count - 2; index >= 0 && roles[index] == last; index--)
                {
                    length++;
                }
                return length;
            }
        }

        private sealed class PairingTemplate
        {
            internal PairingTemplate(
                MatchPairingKind kind,
                string homePlayerId,
                string awayPlayerId,
                string shadowOwnerPlayerId,
                IReadOnlyList<string> settlementRecipients)
            {
                Kind = kind;
                HomePlayerId = homePlayerId;
                AwayPlayerId = awayPlayerId;
                ShadowOwnerPlayerId = shadowOwnerPlayerId;
                SettlementRecipients = settlementRecipients;
            }

            internal MatchPairingKind Kind { get; }
            internal string HomePlayerId { get; }
            internal string AwayPlayerId { get; }
            internal string ShadowOwnerPlayerId { get; }
            internal IReadOnlyList<string> SettlementRecipients { get; }
        }
    }
}
