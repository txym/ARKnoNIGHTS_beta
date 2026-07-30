using System;
using System.Collections.Generic;
using System.Linq;
using ArknoNights.Match;
using NUnit.Framework;

namespace ArknoNights.MatchAI.Tests
{
    internal static class MatchAITestData
    {
        internal static MatchAuthority CreateAuthority(
            BotController controller = null,
            IReadOnlyList<MatchSeatInitialization> seats = null)
        {
            var result = controller == null
                ? MatchSessionFactory.Create(Request(seats))
                : MatchSessionFactory.Create(
                    Request(seats),
                    new StrictStagingSlotPolicy(),
                    controller);
            Assert.That(result.Success, Is.True, result.DiagnosticCode);
            return result.Authority;
        }

        internal static MatchInitializationRequest Request(
            IReadOnlyList<MatchSeatInitialization> seats = null)
        {
            var manifest = new MatchCompatibilityManifest(
                "protocol-1",
                "rules-1",
                "battle-1",
                new string('a', 64),
                new string('b', 64));
            var catalog = new MatchShopCatalog(
                "rules-1",
                new string('a', 64),
                Enumerable.Range(1, 6).Select(index =>
                    new MatchShopCatalogEntry(
                        (1000 + index).ToString(),
                        1,
                        true,
                        3,
                        2,
                        1000 + index)));
            return new MatchInitializationRequest(
                "session-1",
                "seed-1",
                "player-1",
                manifest,
                catalog,
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

        internal static MatchSeatInitialization[] TwoHumanSeats()
        {
            return new[]
            {
                Seat(1, MatchControllerKind.Human),
                Seat(2, MatchControllerKind.Human),
                Seat(3, MatchControllerKind.NativeBot),
                Seat(4, MatchControllerKind.NativeBot)
            };
        }

        internal static MatchSeatInitialization[] FourHumanSeats()
        {
            return Enumerable.Range(1, 4)
                .Select(index => Seat(index, MatchControllerKind.Human))
                .ToArray();
        }

        internal static MatchSeatInitialization Seat(
            int index,
            MatchControllerKind controller)
        {
            return new MatchSeatInitialization(
                index,
                "player-" + index,
                "Player " + index,
                "avatar-" + index,
                controller);
        }

        internal static MatchAuthority Rebuild(
            MatchAuthority authority,
            BotController controller,
            MatchPhase phase,
            int round)
        {
            var source = authority.ProjectForHostAuthority().State;
            var changed = source.Rebuild(
                source.StateRevision,
                phase,
                round,
                source.Seats,
                source.Pool,
                source.EndReason,
                source.Flow);
            return new MatchAuthority(
                changed,
                new StrictStagingSlotPolicy(),
                controller);
        }

        internal static MatchCommandEnvelope Ready(
            MatchAuthority authority,
            string playerId,
            string commandId)
        {
            return new MatchCommandEnvelope(
                "session-1",
                playerId,
                commandId,
                authority.StateRevision,
                new SetPreparationReadyCommand(true));
        }
    }
}
