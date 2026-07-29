using System.Collections.Generic;

namespace ArknoNights.Match.Tests
{
    internal static class MatchTestData
    {
        internal static MatchCompatibilityManifest Manifest(
            string protocolVersion = "protocol-1",
            string rulesVersion = "rules-1",
            string battleVersion = "battle-1",
            char unitHashCharacter = 'a',
            char abilityHashCharacter = 'b')
        {
            return new MatchCompatibilityManifest(
                protocolVersion,
                rulesVersion,
                battleVersion,
                new string(unitHashCharacter, 64),
                new string(abilityHashCharacter, 64));
        }

        internal static MatchInitializationRequest Request(
            IReadOnlyList<MatchSeatInitialization> seats = null,
            string sessionId = "session-1",
            string matchSeed = "seed-1",
            string hostPlayerId = "player-1",
            MatchCompatibilityManifest manifest = null)
        {
            return new MatchInitializationRequest(
                sessionId,
                matchSeed,
                hostPlayerId,
                manifest ?? Manifest(),
                seats ?? OneHumanSeats());
        }

        internal static MatchSeatInitialization[] OneHumanSeats()
        {
            return new[]
            {
                Seat(1, MatchControllerKind.Human),
                Seat(2, MatchControllerKind.NativeBot),
                Seat(3, MatchControllerKind.NativeBot),
                Seat(4, MatchControllerKind.NativeBot)
            };
        }

        internal static MatchSeatInitialization[] FourHumanSeats()
        {
            return new[]
            {
                Seat(1, MatchControllerKind.Human),
                Seat(2, MatchControllerKind.Human),
                Seat(3, MatchControllerKind.Human),
                Seat(4, MatchControllerKind.Human)
            };
        }

        internal static MatchSeatInitialization Seat(
            int seatIndex,
            MatchControllerKind controllerKind,
            string playerId = null,
            string displayName = null,
            string avatarId = null)
        {
            return new MatchSeatInitialization(
                seatIndex,
                playerId ?? "player-" + seatIndex,
                displayName ?? "Player " + seatIndex,
                avatarId ?? "avatar-" + seatIndex,
                controllerKind);
        }

        internal static MatchAuthority CreateAuthority(IReadOnlyList<MatchSeatInitialization> seats = null)
        {
            var result = MatchSessionFactory.Create(Request(seats));
            if (!result.Success)
            {
                throw new System.InvalidOperationException(result.Code + ": " + result.DiagnosticCode);
            }

            return result.Authority;
        }

        internal static MatchCommandEnvelope Ready(
            MatchAuthority authority,
            string playerId,
            string commandId,
            bool desiredReady,
            long? knownRevision = null,
            string sessionId = "session-1")
        {
            return new MatchCommandEnvelope(
                sessionId,
                playerId,
                commandId,
                knownRevision ?? authority.StateRevision,
                new SetPreparationReadyCommand(desiredReady));
        }
    }
}
