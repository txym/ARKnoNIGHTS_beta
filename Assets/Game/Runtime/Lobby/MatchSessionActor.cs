using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ArknoNights.Match;

namespace ArknoNights.Lobby
{
    public enum MatchSessionLifecycle
    {
        Discovering,
        Lobby,
        StartCommitted,
        Match,
        Ended
    }

    public enum MatchSessionStartFailure
    {
        None,
        InvalidConfiguration,
        InvalidLobby,
        PlayerIdentityInvalid,
        CompatibilityMismatch,
        AvatarCapacityInsufficient,
        AuthorityInitializationRejected
    }

    public interface IMatchConnectionControlSink
    {
        void ConnectionLost(string playerId, MatchPhase phase, int roundNumber);
        void RestoreHumanControl(string playerId, MatchPhase phase, int roundNumber);
        void ExplicitQuit(string playerId, MatchPhase phase, int roundNumber);
    }

    public interface IMatchConnectionControlBinding
    {
        void Bind(IMatchBotHost host);
    }

    public sealed class NoOpMatchConnectionControlSink : IMatchConnectionControlSink
    {
        public void ConnectionLost(string playerId, MatchPhase phase, int roundNumber) { }
        public void RestoreHumanControl(string playerId, MatchPhase phase, int roundNumber) { }
        public void ExplicitQuit(string playerId, MatchPhase phase, int roundNumber) { }
    }

    public sealed class LanMatchSessionConfiguration
    {
        public LanMatchSessionConfiguration(
            MatchCompatibilityManifest compatibilityManifest,
            MatchShopCatalog shopCatalog,
            int availableAvatarCount = 4,
            IMatchConnectionControlSink connectionControlSink = null,
            bool allowSyntheticPlayerIdsForTests = false,
            IMatchPreparationEntryParticipant preparationEntryParticipant = null)
        {
            CompatibilityManifest = compatibilityManifest;
            ShopCatalog = shopCatalog;
            AvailableAvatarCount = availableAvatarCount;
            ConnectionControlSink = connectionControlSink ?? new NoOpMatchConnectionControlSink();
            AllowSyntheticPlayerIdsForTests = allowSyntheticPlayerIdsForTests;
            PreparationEntryParticipant = preparationEntryParticipant;
        }

        public MatchCompatibilityManifest CompatibilityManifest { get; }
        public MatchShopCatalog ShopCatalog { get; }
        public int AvailableAvatarCount { get; }
        public IMatchConnectionControlSink ConnectionControlSink { get; }
        public IMatchPreparationEntryParticipant PreparationEntryParticipant { get; }
        internal bool AllowSyntheticPlayerIdsForTests { get; }

        public bool IsValid =>
            CompatibilityManifest != null
            && CompatibilityManifest.IsValid
            && ShopCatalog != null
            && ShopCatalog.IsCompatibleWith(CompatibilityManifest)
            && ShopCatalog.TryValidate(out _, out _)
            && AvailableAvatarCount > 0
            && ConnectionControlSink != null;

        public static LanMatchSessionConfiguration CreateForTests()
        {
            var manifest = new MatchCompatibilityManifest(
                "lan-match-test-1",
                "rules-test-1",
                "battle-test-1",
                new string('a', 64),
                new string('b', 64));
            return new LanMatchSessionConfiguration(
                manifest,
                new MatchShopCatalog(
                    manifest.MatchRulesVersion,
                    manifest.UnitCatalogSha256,
                    new[]
                    {
                        new MatchShopCatalogEntry(
                            "TEST_UNIT",
                            1,
                            true,
                            3,
                            1,
                            1)
                    }),
                allowSyntheticPlayerIdsForTests: true);
        }
    }

    public sealed class MatchSessionStartResult
    {
        internal MatchSessionStartResult(
            MatchSessionStartFailure failure,
            string diagnosticCode,
            MatchSessionHostActor actor)
        {
            Failure = failure;
            DiagnosticCode = diagnosticCode ?? string.Empty;
            Actor = actor;
        }

        public bool Success => Failure == MatchSessionStartFailure.None && Actor != null;
        public MatchSessionStartFailure Failure { get; }
        public string DiagnosticCode { get; }
        public MatchSessionHostActor Actor { get; }
    }

    public static class MatchSessionBuilder
    {
        public static MatchSessionStartResult Create(
            LobbyRoomSnapshot lobby,
            LanMatchSessionConfiguration configuration,
            IReadOnlyDictionary<string, MatchCompatibilityManifest> joinedManifests,
            long hostMonotonicNowMs)
        {
            if (configuration == null || !configuration.IsValid)
                return Rejected(MatchSessionStartFailure.InvalidConfiguration, "match.session.configuration.invalid");
            if (lobby == null
                || lobby.HasStarted
                || lobby.Members.Count < 1
                || lobby.Members.Count > LobbyRoomSnapshot.MaximumMembers
                || !string.Equals(lobby.HostPlayerId, lobby.Members[0].PlayerId, StringComparison.Ordinal)
                || lobby.Members.Any(member => member == null || member.Profile == null || !member.Profile.IsValid())
                || lobby.Members.Any(member => !member.IsReady))
            {
                return Rejected(MatchSessionStartFailure.InvalidLobby, "match.session.lobby.invalid");
            }
            if (!configuration.AllowSyntheticPlayerIdsForTests
                && lobby.Members.Any(member => !LocalProfileIdentity.IsValid(member.PlayerId)))
            {
                return Rejected(MatchSessionStartFailure.PlayerIdentityInvalid, "match.session.playerId.invalid");
            }
            if (lobby.Members.Any(member =>
                member.Profile.AvatarIndex
                    >= configuration.AvailableAvatarCount))
            {
                return Rejected(
                    MatchSessionStartFailure.AvatarCapacityInsufficient,
                    "match.session.avatar.capacity");
            }
            if (joinedManifests == null
                || lobby.Members.Any(member =>
                    !joinedManifests.TryGetValue(member.PlayerId, out var manifest)
                    || !ManifestEquals(configuration.CompatibilityManifest, manifest)))
            {
                return Rejected(MatchSessionStartFailure.CompatibilityMismatch, "match.session.compatibility.mismatch");
            }

            var sessionId = "match-" + RandomBase64Url(18);
            var matchSeed = RandomBase64Url(32);
            var occupiedAvatars = new HashSet<int>(
                lobby.Members.Select(member => member.Profile.AvatarIndex));
            var availableBotAvatars = Enumerable.Range(0, configuration.AvailableAvatarCount)
                .Where(avatar => !occupiedAvatars.Contains(avatar))
                .ToArray();
            var botCount = LobbyRoomSnapshot.MaximumMembers - lobby.Members.Count;
            if (availableBotAvatars.Length < botCount)
            {
                return Rejected(
                    MatchSessionStartFailure.AvatarCapacityInsufficient,
                    "match.session.avatar.capacity");
            }

            var seats = new List<MatchSeatInitialization>();
            for (var index = 0; index < lobby.Members.Count; index++)
            {
                var member = lobby.Members[index];
                seats.Add(new MatchSeatInitialization(
                    index + 1,
                    member.PlayerId,
                    member.Profile.DisplayName,
                    AvatarId(member.Profile.AvatarIndex),
                    MatchControllerKind.Human));
            }
            for (var botIndex = 0; botIndex < botCount; botIndex++)
            {
                var seatIndex = lobby.Members.Count + botIndex + 1;
                var avatar = availableBotAvatars[botIndex];
                seats.Add(new MatchSeatInitialization(
                    seatIndex,
                    BotPlayerId(sessionId, seatIndex),
                    LobbyProfile.DisplayNameForAvatar(avatar),
                    AvatarId(avatar),
                    MatchControllerKind.NativeBot));
            }

            var request = new MatchInitializationRequest(
                sessionId,
                matchSeed,
                lobby.HostPlayerId,
                configuration.CompatibilityManifest,
                configuration.ShopCatalog,
                seats);
            var initialized = configuration.PreparationEntryParticipant == null
                ? MatchSessionFactory.Create(request)
                : MatchSessionFactory.Create(
                    request,
                    new StrictStagingSlotPolicy(),
                    configuration.PreparationEntryParticipant);
            if (!initialized.Success)
            {
                return Rejected(
                    MatchSessionStartFailure.AuthorityInitializationRejected,
                    initialized.DiagnosticCode);
            }
            var preparation = initialized.Authority.TryEnterPreparation(1, hostMonotonicNowMs);
            if (!preparation.ChangedState)
            {
                return Rejected(
                    MatchSessionStartFailure.AuthorityInitializationRejected,
                    preparation.DiagnosticCode);
            }

            return new MatchSessionStartResult(
                MatchSessionStartFailure.None,
                string.Empty,
                new MatchSessionHostActor(
                    initialized.Authority,
                    configuration,
                    lobby.Members.Select(member => member.PlayerId),
                    hostMonotonicNowMs));
        }

        private static MatchSessionStartResult Rejected(
            MatchSessionStartFailure failure,
            string diagnosticCode)
        {
            return new MatchSessionStartResult(failure, diagnosticCode, null);
        }

        internal static bool ManifestEquals(
            MatchCompatibilityManifest left,
            MatchCompatibilityManifest right)
        {
            return left != null
                && right != null
                && string.Equals(left.ProtocolVersion, right.ProtocolVersion, StringComparison.Ordinal)
                && string.Equals(left.MatchRulesVersion, right.MatchRulesVersion, StringComparison.Ordinal)
                && string.Equals(left.BattleCoreVersion, right.BattleCoreVersion, StringComparison.Ordinal)
                && string.Equals(left.UnitCatalogSha256, right.UnitCatalogSha256, StringComparison.Ordinal)
                && string.Equals(left.AbilityCatalogSha256, right.AbilityCatalogSha256, StringComparison.Ordinal);
        }

        private static string RandomBase64Url(int byteCount)
        {
            var bytes = new byte[byteCount];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string BotPlayerId(string sessionId, int seatIndex)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(
                    Encoding.UTF8.GetBytes(sessionId + "|seat|" + seatIndex));
                var builder = new StringBuilder("lan-");
                for (var index = 0; index < 16; index++)
                    builder.Append(bytes[index].ToString("x2"));
                return builder.ToString();
            }
        }

        private static string AvatarId(int avatarIndex)
        {
            return "avatar-" + avatarIndex;
        }
    }

    public sealed class MatchSessionParticipant
    {
        internal MatchSessionParticipant(
            int seatIndex,
            string playerId,
            bool isHuman,
            byte[] reconnectVerifier)
        {
            SeatIndex = seatIndex;
            PlayerId = playerId;
            IsHuman = isHuman;
            ReconnectVerifier = reconnectVerifier == null ? null : (byte[])reconnectVerifier.Clone();
        }

        public int SeatIndex { get; }
        public string PlayerId { get; }
        public bool IsHuman { get; }
        public string ActiveConnectionId { get; internal set; }
        public long ConnectionGeneration { get; internal set; }
        public bool ExplicitlyQuit { get; internal set; }
        public bool ReconnectCredentialActive =>
            ReconnectVerifier != null;
        internal byte[] ReconnectVerifier { get; private set; }

        internal void InvalidateReconnectVerifier()
        {
            if (ReconnectVerifier != null)
                Array.Clear(ReconnectVerifier, 0, ReconnectVerifier.Length);
            ReconnectVerifier = null;
        }
    }

    public sealed class MatchSessionDispatch
    {
        internal MatchSessionDispatch(
            string connectionId,
            MatchWireKind kind,
            object payload,
            long hostAcceptSequence,
            string closeConnectionId = null)
        {
            ConnectionId = connectionId;
            Kind = kind;
            Payload = payload;
            HostAcceptSequence = hostAcceptSequence;
            CloseConnectionId = closeConnectionId;
        }

        public string ConnectionId { get; }
        public MatchWireKind Kind { get; }
        public object Payload { get; }
        public long HostAcceptSequence { get; }
        public string CloseConnectionId { get; }
    }

    public sealed class MatchBattleTransportEvent
    {
        internal MatchBattleTransportEvent(
            string playerId,
            MatchWireKind kind,
            object payload,
            long hostAcceptSequence)
        {
            PlayerId = playerId;
            Kind = kind;
            Payload = payload;
            HostAcceptSequence = hostAcceptSequence;
        }

        public string PlayerId { get; }
        public MatchWireKind Kind { get; }
        public object Payload { get; }
        public long HostAcceptSequence { get; }
    }

    public sealed class MatchSessionHostActor
    {
        private readonly MatchAuthority authority;
        private readonly LanMatchSessionConfiguration configuration;
        private readonly IMatchConnectionControlSink connectionControlSink;
        private readonly string sessionId;
        private readonly string hostPlayerId;
        private readonly string matchSeed;
        private readonly Dictionary<string, MatchSessionParticipant> participants;
        private readonly Dictionary<string, string> pendingInitializationTokens;
        private readonly ConcurrentQueue<InboundItem> inbound = new ConcurrentQueue<InboundItem>();
        private readonly List<MatchSessionDispatch> dispatches = new List<MatchSessionDispatch>();
        private readonly int actorThreadId;
        private long hostAcceptSequence;
        private long lastClockAdvanceMs;
        private bool endedBroadcast;
        private MatchBattleSealPayload currentBattleSeal;
        private MatchPlaybackStartPayload currentPlaybackStart;
        private MatchPlaybackClockPayload currentPlaybackClock;

        internal MatchSessionHostActor(
            MatchAuthority authority,
            LanMatchSessionConfiguration configuration,
            IEnumerable<string> humanPlayerIds,
            long hostMonotonicNowMs)
        {
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            connectionControlSink = configuration.ConnectionControlSink;
            actorThreadId = Environment.CurrentManagedThreadId;
            if (connectionControlSink is IMatchConnectionControlBinding binding)
                binding.Bind(authority);
            lastClockAdvanceMs = hostMonotonicNowMs;
            var hostState = authority.ProjectForHostAuthority();
            sessionId = hostState.SessionId;
            hostPlayerId = hostState.HostPlayerId;
            matchSeed = hostState.MatchSeed;
            var humanSet = new HashSet<string>(humanPlayerIds, StringComparer.Ordinal);
            participants = new Dictionary<string, MatchSessionParticipant>(StringComparer.Ordinal);
            pendingInitializationTokens = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var seat in hostState.Seats)
            {
                IssuedReconnectToken issued = null;
                if (humanSet.Contains(seat.PlayerId))
                {
                    issued = ReconnectTokenIssuer.Issue();
                    pendingInitializationTokens.Add(seat.PlayerId, issued.RawToken);
                }
                participants.Add(
                    seat.PlayerId,
                    new MatchSessionParticipant(
                        seat.SeatIndex,
                        seat.PlayerId,
                        humanSet.Contains(seat.PlayerId),
                        issued?.VerifierSha256));
            }
            Lifecycle = MatchSessionLifecycle.StartCommitted;
        }

        public event Action<MatchBattleTransportEvent> BattleTransportAccepted;

        public MatchSessionLifecycle Lifecycle { get; private set; }
        public string SessionId => sessionId;
        public string HostPlayerId => hostPlayerId;
        public string MatchSeed => matchSeed;
        public long StateRevision => authority.StateRevision;
        public long HostAcceptSequence => hostAcceptSequence;
        public IReadOnlyCollection<MatchSessionParticipant> Participants =>
            new ReadOnlyCollection<MatchSessionParticipant>(
                participants.Values.OrderBy(item => item.SeatIndex).ToArray());

        public HostMatchSnapshot ProjectHostState()
        {
            AssertActorThread();
            return authority.ProjectForHostAuthority();
        }

        public IMatchBotHost BotHost
        {
            get
            {
                AssertActorThread();
                return authority;
            }
        }

        public bool BindFrozenConnection(
            string playerId,
            string connectionId,
            long connectionGeneration)
        {
            AssertActorThread();
            if (!participants.TryGetValue(playerId ?? string.Empty, out var participant)
                || !participant.IsHuman
                || participant.ExplicitlyQuit
                || string.IsNullOrWhiteSpace(connectionId)
                || connectionGeneration <= participant.ConnectionGeneration)
            {
                return false;
            }
            participant.ActiveConnectionId = connectionId;
            participant.ConnectionGeneration = connectionGeneration;
            return true;
        }

        public MatchInitializedPayload TakeInitialization(string playerId)
        {
            AssertActorThread();
            if (!participants.TryGetValue(playerId ?? string.Empty, out var participant)
                || !participant.IsHuman
                || string.IsNullOrWhiteSpace(participant.ActiveConnectionId)
                || !pendingInitializationTokens.TryGetValue(playerId, out var rawToken))
            {
                return null;
            }
            pendingInitializationTokens.Remove(playerId);
            var snapshot = ProjectScoped(playerId);
            return new MatchInitializedPayload
            {
                PlayerId = playerId,
                SeatIndex = participant.SeatIndex,
                ConnectionGeneration = participant.ConnectionGeneration,
                HostPlayerId = HostPlayerId,
                MatchSeed = MatchSeed,
                ReconnectToken = rawToken,
                Manifest = MatchCompatibilityWire.FromDomain(configuration.CompatibilityManifest),
                Snapshot = snapshot,
                Clock = ProjectClock()
            };
        }

        public void MarkMatchRunning()
        {
            AssertActorThread();
            if (Lifecycle == MatchSessionLifecycle.StartCommitted)
                Lifecycle = MatchSessionLifecycle.Match;
        }

        public void EnqueueRemote(
            string connectionId,
            long connectionGeneration,
            string boundPlayerId,
            MatchWireEnvelope envelope)
        {
            inbound.Enqueue(new InboundItem(
                InboundKind.Wire,
                connectionId,
                connectionGeneration,
                boundPlayerId,
                envelope,
                null));
        }

        public void EnqueueHostCommand(MatchCommandWirePayload command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var host = participants[HostPlayerId];
            command.PlayerId = HostPlayerId;
            command.ConnectionGeneration =
                host.ConnectionGeneration;
            inbound.Enqueue(new InboundItem(
                InboundKind.HostCommand,
                host.ActiveConnectionId,
                host.ConnectionGeneration,
                HostPlayerId,
                null,
                command));
        }

        public void EnqueueHostBattleContract(
            MatchWireKind kind,
            object payload)
        {
            if (kind != MatchWireKind.FirstChunkReady
                && kind != MatchWireKind.FinalSecondHash
                && kind != MatchWireKind.ClientBattleFailure)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            var frame = MatchProtocol.Encode(
                kind,
                SessionId,
                "host-local-" + Guid.NewGuid().ToString("N"),
                payload,
                MatchWireDirection.ClientToHost);
            if (!MatchProtocol.TryDecode(
                frame,
                MatchWireDirection.ClientToHost,
                out var envelope,
                out var error))
            {
                throw new InvalidOperationException(
                    "Host Battle contract is invalid: " + error + ".");
            }
            var host = participants[HostPlayerId];
            inbound.Enqueue(new InboundItem(
                InboundKind.Wire,
                host.ActiveConnectionId,
                host.ConnectionGeneration,
                HostPlayerId,
                envelope,
                null));
        }

        public void EnqueueConnectionLost(
            string connectionId,
            long connectionGeneration,
            string playerId)
        {
            inbound.Enqueue(new InboundItem(
                InboundKind.ConnectionLost,
                connectionId,
                connectionGeneration,
                playerId,
                null,
                null));
        }

        public IReadOnlyList<MatchSessionDispatch> Tick(long hostMonotonicNowMs)
        {
            AssertActorThread();
            dispatches.Clear();
            while (inbound.TryDequeue(out var item))
            {
                var sequence = checked(++hostAcceptSequence);
                switch (item.Kind)
                {
                    case InboundKind.ConnectionLost:
                        ProcessConnectionLost(item, sequence);
                        break;
                    case InboundKind.HostCommand:
                        ProcessCommand(
                            item,
                            (MatchCommandWirePayload)item.Payload,
                            sequence);
                        break;
                    case InboundKind.Wire:
                        ProcessWire(item, sequence);
                        break;
                }
            }

            var snapshot = authority.ProjectForHostAuthority();
            if (Lifecycle == MatchSessionLifecycle.Match
                && snapshot.Phase == MatchPhase.Preparation
                && hostMonotonicNowMs >= lastClockAdvanceMs
                && (hostMonotonicNowMs - lastClockAdvanceMs >= 1000
                    || hostMonotonicNowMs >= snapshot.Flow.PreparationDeadlineHostMonotonicMs))
            {
                var sequence = checked(++hostAcceptSequence);
                lastClockAdvanceMs = hostMonotonicNowMs;
                var result = authority.AdvancePreparationClock(hostMonotonicNowMs);
                if (result.ChangedState)
                {
                    PublishSnapshots(sequence);
                    PublishClock(sequence);
                }
            }
            PublishEndIfNeeded();
            return dispatches.ToArray();
        }

        public ScopedSnapshotPayload ProjectScoped(string playerId)
        {
            AssertActorThread();
            var snapshot = authority.ProjectForPlayer(playerId);
            var state = snapshot.Owner == null
                ? MatchLocalConnectionState.Spectating
                : MatchLocalConnectionState.Connected;
            var projected = MatchSnapshotWireProjector.Project(snapshot, state);
            if (!MatchSnapshotWireProjector.ContainsOnlyRecipientPrivateState(projected, playerId))
                throw new InvalidOperationException("match.snapshot.privacy.invalid");
            return projected;
        }

        public IReadOnlyList<MatchSessionDispatch> PublishBattleSeal(
            MatchBattleSealPayload seal)
        {
            AssertActorThread();
            dispatches.Clear();
            if (seal == null
                || !HasCurrentBattleScope(
                    seal.RoundNumber,
                    seal.CanonicalInputHash))
            {
                return Array.Empty<MatchSessionDispatch>();
            }
            currentBattleSeal = new MatchBattleSealPayload
            {
                RoundNumber = seal.RoundNumber,
                BattleSetId = seal.BattleSetId,
                CanonicalInputHash = seal.CanonicalInputHash,
                SealedPayload = seal.SealedPayload,
                BattleInputs = seal.BattleInputs == null
                    ? null
                    : seal.BattleInputs.Select(item =>
                        new MatchBattleInputHashWire
                        {
                            BattleId = item.BattleId,
                            InputSha256 = item.InputSha256,
                            SealedInputHash = item.SealedInputHash
                        }).ToArray()
            };
            var sequence = checked(++hostAcceptSequence);
            foreach (var participant in ActiveHumans())
                Dispatch(
                    participant.ActiveConnectionId,
                    MatchWireKind.BattleSeal,
                    seal,
                    sequence);
            return dispatches.ToArray();
        }

        public IReadOnlyList<MatchSessionDispatch> PublishPlaybackStart(
            MatchPlaybackStartPayload start)
        {
            AssertActorThread();
            dispatches.Clear();
            if (start == null
                || !HasCurrentBattleScope(
                    start.RoundNumber,
                    start.CanonicalInputHash)
                || !MatchesCurrentBattleSet(start.BattleSetId))
            {
                return Array.Empty<MatchSessionDispatch>();
            }
            currentPlaybackStart = new MatchPlaybackStartPayload
            {
                RoundNumber = start.RoundNumber,
                BattleSetId = start.BattleSetId,
                CanonicalInputHash = start.CanonicalInputHash,
                HostMonotonicStartMs =
                    start.HostMonotonicStartMs,
                StartTick = start.StartTick
            };
            var sequence = checked(++hostAcceptSequence);
            foreach (var participant in ActiveHumans())
                Dispatch(
                    participant.ActiveConnectionId,
                    MatchWireKind.PlaybackStart,
                    start,
                    sequence);
            return dispatches.ToArray();
        }

        public IReadOnlyList<MatchSessionDispatch> PublishPlaybackClock(
            MatchPlaybackClockPayload clock)
        {
            AssertActorThread();
            dispatches.Clear();
            if (clock == null
                || !HasCurrentBattleScope(
                    clock.RoundNumber,
                    clock.CanonicalInputHash)
                || !MatchesCurrentBattleSet(clock.BattleSetId))
            {
                return Array.Empty<MatchSessionDispatch>();
            }
            currentPlaybackClock = new MatchPlaybackClockPayload
            {
                RoundNumber = clock.RoundNumber,
                BattleSetId = clock.BattleSetId,
                CanonicalInputHash = clock.CanonicalInputHash,
                HostMonotonicNowMs =
                    clock.HostMonotonicNowMs,
                CurrentTick = clock.CurrentTick
            };
            var sequence = checked(++hostAcceptSequence);
            foreach (var participant in ActiveHumans())
                Dispatch(participant.ActiveConnectionId, MatchWireKind.PlaybackClock, clock, sequence);
            return dispatches.ToArray();
        }

        public IReadOnlyList<MatchSessionDispatch> AbortByHost(string stableReason)
        {
            AssertActorThread();
            dispatches.Clear();
            var sequence = checked(++hostAcceptSequence);
            authority.AbortMatchNoContest(
                string.IsNullOrWhiteSpace(stableReason)
                    ? "match.host.explicitQuit"
                    : stableReason);
            PublishSnapshots(sequence);
            PublishEndIfNeeded();
            return dispatches.ToArray();
        }

        public IReadOnlyList<MatchSessionDispatch> CompleteBattleRound(
            IReadOnlyList<MatchBattleResolution> resolutions,
            long hostMonotonicNowMs,
            out string diagnosticCode)
        {
            AssertActorThread();
            dispatches.Clear();
            var snapshot = authority.ProjectForHostAuthority();
            var plan = snapshot.Flow.SealedRoundPlan;
            if (Lifecycle != MatchSessionLifecycle.Match
                || snapshot.Phase != MatchPhase.Battle
                || plan == null
                || resolutions == null
                || resolutions.Count != plan.Pairings.Count
                || resolutions.Any(item => item == null)
                || resolutions.Any(item =>
                    string.IsNullOrWhiteSpace(item.BattleId)
                    || item.HomeLifeDamage < 0
                    || item.AwayLifeDamage < 0
                    || item.EndTick < 0
                    || !Enum.IsDefined(
                        typeof(MatchBattleOutcome),
                        item.Outcome)
                    || !Enum.IsDefined(
                        typeof(MatchBattleTerminalReason),
                        item.TerminalReason)
                    || item.Outcome != MatchBattleResolution.DeriveOutcome(
                        item.HomeLifeDamage,
                        item.AwayLifeDamage))
                || resolutions.GroupBy(item => item.BattleId, StringComparer.Ordinal)
                    .Any(group => group.Count() != 1)
                || plan.Pairings.Any(pairing =>
                    !resolutions.Any(item =>
                        string.Equals(item.BattleId, pairing.BattleId, StringComparison.Ordinal)
                        && string.Equals(
                            item.SealedInputHash,
                            pairing.SealedInputHash,
                            StringComparison.Ordinal))))
            {
                diagnosticCode = "match.session.battleCompletion.invalid";
                return Array.Empty<MatchSessionDispatch>();
            }

            foreach (var resolution in resolutions.OrderBy(item => item.BattleId, StringComparer.Ordinal))
            {
                var submitted = authority.TrySubmitBattleResolution(resolution);
                if (!submitted.Accepted)
                {
                    diagnosticCode = submitted.DiagnosticCode;
                    return Array.Empty<MatchSessionDispatch>();
                }
            }
            var playback = authority.TryMarkBattlePlaybackCompleted();
            if (!playback.Accepted)
            {
                diagnosticCode = playback.DiagnosticCode;
                return Array.Empty<MatchSessionDispatch>();
            }
            var settlement = authority.TryCommitRoundSettlement(hostMonotonicNowMs);
            if (!settlement.Accepted)
            {
                diagnosticCode = settlement.DiagnosticCode;
                return Array.Empty<MatchSessionDispatch>();
            }

            var sequence = checked(++hostAcceptSequence);
            currentBattleSeal = null;
            currentPlaybackStart = null;
            currentPlaybackClock = null;
            PublishSnapshots(sequence);
            PublishClock(sequence);
            PublishEndIfNeeded();
            diagnosticCode = string.Empty;
            return dispatches.ToArray();
        }

        private void ProcessWire(InboundItem item, long sequence)
        {
            if (item.Envelope == null
                || !Enum.TryParse(item.Envelope.Kind, false, out MatchWireKind kind))
            {
                Dispatch(item.ConnectionId, MatchWireKind.Reject, new MatchRejectPayload
                {
                    Code = "MalformedRequest",
                    StableDetailCode = "match.wire.invalid"
                }, sequence);
                return;
            }
            if (kind != MatchWireKind.Command
                && kind != MatchWireKind.ReconnectRequest
                && !string.Equals(
                    item.Envelope.SessionId,
                    SessionId,
                    StringComparison.Ordinal))
            {
                Dispatch(item.ConnectionId, MatchWireKind.Reject, new MatchRejectPayload
                {
                    Code = MatchCommandCode.SessionMismatch.ToString(),
                    StableDetailCode = "match.session.mismatch"
                }, sequence);
                return;
            }
            switch (kind)
            {
                case MatchWireKind.Command:
                    if (MatchProtocol.TryDeserializePayload(item.Envelope, out MatchCommandWirePayload command))
                        ProcessCommand(item, command, sequence);
                    break;
                case MatchWireKind.SnapshotRequest:
                    if (IsActiveBinding(item)
                        && MatchProtocol.TryDeserializePayload(item.Envelope, out MatchSnapshotRequestPayload _))
                    {
                        Dispatch(item.ConnectionId, MatchWireKind.ScopedSnapshot, ProjectScoped(item.BoundPlayerId), sequence);
                    }
                    break;
                case MatchWireKind.ReconnectRequest:
                    if (MatchProtocol.TryDeserializePayload(item.Envelope, out MatchReconnectRequestPayload reconnect))
                        ProcessReconnect(item, reconnect, sequence);
                    break;
                case MatchWireKind.ExplicitQuit:
                    if (MatchProtocol.TryDeserializePayload(item.Envelope, out MatchExplicitQuitPayload quit))
                        ProcessExplicitQuit(item, quit, sequence);
                    break;
                case MatchWireKind.FirstChunkReady:
                case MatchWireKind.FinalSecondHash:
                case MatchWireKind.ClientBattleFailure:
                    ProcessBattleContract(item, kind, sequence);
                    break;
                case MatchWireKind.Ping:
                    if (IsActiveBinding(item)
                        && MatchProtocol.TryDeserializePayload(item.Envelope, out MatchHeartbeatPayload heartbeat)
                        && heartbeat.ConnectionGeneration == item.ConnectionGeneration)
                    {
                        Dispatch(item.ConnectionId, MatchWireKind.Pong, heartbeat, sequence);
                    }
                    break;
            }
        }

        private void ProcessCommand(
            InboundItem item,
            MatchCommandWirePayload command,
            long sequence)
        {
            MatchCommandAckPayload ack;
            if (command == null
                || !string.Equals(command.PlayerId, item.BoundPlayerId, StringComparison.Ordinal)
                || command.ConnectionGeneration != item.ConnectionGeneration
                || !IsActiveBinding(item)
                || (item.Envelope != null
                    && !string.Equals(item.Envelope.SessionId, SessionId, StringComparison.Ordinal)))
            {
                ack = RejectedAck(
                    command?.CommandId,
                    MatchCommandCode.ConnectionRejected,
                    "match.command.connection.rejected",
                    sequence);
            }
            else if (!command.TryToDomain(SessionId, out var domain))
            {
                ack = RejectedAck(
                    command.CommandId,
                    MatchCommandCode.InvalidPayload,
                    "match.command.payload.invalid",
                    sequence);
            }
            else
            {
                var result = authority.Execute(domain);
                ack = new MatchCommandAckPayload
                {
                    CommandId = result.CommandId,
                    ResultCode = result.Code.ToString(),
                    CurrentStateRevision = result.CurrentStateRevision,
                    AcceptedStateRevision = result.AcceptedStateRevision.GetValueOrDefault(),
                    HasAcceptedStateRevision = result.AcceptedStateRevision.HasValue,
                    DidChangeState = result.ChangedState,
                    StableDetailCode = result.DiagnosticCode,
                    HostAcceptSequence = sequence
                };
            }
            Dispatch(item.ConnectionId, MatchWireKind.CommandAck, ack, sequence);
            if (ack.DidChangeState) PublishSnapshots(sequence);
            else if (command != null && command.KnownStateRevision < authority.StateRevision)
                Dispatch(item.ConnectionId, MatchWireKind.ScopedSnapshot, ProjectScoped(item.BoundPlayerId), sequence);
        }

        private void ProcessConnectionLost(InboundItem item, long sequence)
        {
            if (!IsActiveBinding(item)) return;
            var participant = participants[item.BoundPlayerId];
            participant.ActiveConnectionId = null;
            var snapshot = authority.ProjectForHostAuthority();
            var seat = snapshot.Seats.Single(value =>
                string.Equals(value.PlayerId, item.BoundPlayerId, StringComparison.Ordinal));
            if (!seat.Eliminated)
            {
                var result = authority.TrySetConnectionState(
                    item.BoundPlayerId,
                    MatchConnectionState.DisconnectedGrace);
                if (result.ChangedState) PublishSnapshots(sequence);
                connectionControlSink.ConnectionLost(
                    item.BoundPlayerId,
                    authority.ProjectPublic().Phase,
                    authority.ProjectPublic().RoundNumber);
            }
        }

        private void ProcessReconnect(
            InboundItem item,
            MatchReconnectRequestPayload request,
            long sequence)
        {
            var rejection = ValidateReconnect(item, request, out var participant);
            if (rejection.HasValue)
            {
                Dispatch(item.ConnectionId, MatchWireKind.ReconnectRejected, new MatchReconnectRejectedPayload
                {
                    Code = rejection.Value.ToString(),
                    StableDetailCode = "match.reconnect." + rejection.Value.ToString()
                }, sequence);
                return;
            }

            var previousConnectionId = participant.ActiveConnectionId;
            if (!string.IsNullOrWhiteSpace(previousConnectionId))
            {
                var hostSnapshot = authority.ProjectForHostAuthority();
                var seat = hostSnapshot.Seats.Single(value =>
                    string.Equals(value.PlayerId, participant.PlayerId, StringComparison.Ordinal));
                if (!seat.Eliminated)
                {
                    authority.TrySetConnectionState(
                        participant.PlayerId,
                        MatchConnectionState.DisconnectedGrace);
                }
            }
            participant.ConnectionGeneration = checked(participant.ConnectionGeneration + 1);
            participant.ActiveConnectionId = item.ConnectionId;
            var currentSeat = authority.ProjectForHostAuthority().Seats.Single(value =>
                string.Equals(value.PlayerId, participant.PlayerId, StringComparison.Ordinal));
            if (!currentSeat.Eliminated)
            {
                authority.TrySetConnectionState(
                    participant.PlayerId,
                    MatchConnectionState.Connected);
                connectionControlSink.RestoreHumanControl(
                    participant.PlayerId,
                    authority.ProjectPublic().Phase,
                    authority.ProjectPublic().RoundNumber);
            }

            Dispatch(
                item.ConnectionId,
                MatchWireKind.ReconnectAccepted,
                new MatchReconnectAcceptedPayload
                {
                    PlayerId = participant.PlayerId,
                    SeatIndex = participant.SeatIndex,
                    ConnectionGeneration = participant.ConnectionGeneration,
                    ReconnectToken = string.Empty
                },
                sequence,
                previousConnectionId);
            Dispatch(
                item.ConnectionId,
                MatchWireKind.ScopedSnapshot,
                ProjectScoped(participant.PlayerId),
                sequence);
            PublishSnapshots(sequence, participant.PlayerId);
            Dispatch(item.ConnectionId, MatchWireKind.ClockSync, ProjectClock(), sequence);
            PublishRecoveryBattleState(item.ConnectionId, sequence);
        }

        private MatchReconnectRejectCode? ValidateReconnect(
            InboundItem item,
            MatchReconnectRequestPayload request,
            out MatchSessionParticipant participant)
        {
            participant = null;
            if (request == null
                || item.Envelope == null
                || !string.IsNullOrEmpty(item.BoundPlayerId)
                || item.ConnectionGeneration != 0)
                return MatchReconnectRejectCode.MalformedRequest;
            if (!string.Equals(item.Envelope.SessionId, SessionId, StringComparison.Ordinal))
                return Lifecycle == MatchSessionLifecycle.Ended
                    ? MatchReconnectRejectCode.SessionEnded
                    : MatchReconnectRejectCode.UnknownSession;
            if (!MatchSessionBuilder.ManifestEquals(
                configuration.CompatibilityManifest,
                request.Manifest?.ToDomain()))
            {
                return MatchReconnectRejectCode.CompatibilityMismatch;
            }
            if (!participants.TryGetValue(request.PlayerId ?? string.Empty, out participant)
                || !participant.IsHuman)
            {
                participant = null;
                return MatchReconnectRejectCode.UnknownPlayer;
            }
            if (participant.ExplicitlyQuit)
                return MatchReconnectRejectCode.ExplicitlyQuit;
            if (!ReconnectTokenIssuer.Verify(request.RawToken, participant.ReconnectVerifier))
                return MatchReconnectRejectCode.InvalidToken;
            if (Lifecycle == MatchSessionLifecycle.Ended)
                return MatchReconnectRejectCode.SessionEnded;
            return null;
        }

        private void ProcessExplicitQuit(
            InboundItem item,
            MatchExplicitQuitPayload quit,
            long sequence)
        {
            if (quit == null
                || !string.Equals(quit.PlayerId, item.BoundPlayerId, StringComparison.Ordinal)
                || quit.ConnectionGeneration != item.ConnectionGeneration
                || !IsActiveBinding(item))
            {
                return;
            }
            if (string.Equals(item.BoundPlayerId, HostPlayerId, StringComparison.Ordinal))
            {
                authority.AbortMatchNoContest("match.host.explicitQuit");
                PublishSnapshots(sequence);
                PublishEndIfNeeded();
                return;
            }
            var participant = participants[item.BoundPlayerId];
            participant.ExplicitlyQuit = true;
            participant.InvalidateReconnectVerifier();
            participant.ActiveConnectionId = null;
            var result = authority.TrySetConnectionState(
                item.BoundPlayerId,
                MatchConnectionState.Quit);
            connectionControlSink.ExplicitQuit(
                item.BoundPlayerId,
                authority.ProjectPublic().Phase,
                authority.ProjectPublic().RoundNumber);
            if (result.ChangedState) PublishSnapshots(sequence);
        }

        private void ProcessBattleContract(
            InboundItem item,
            MatchWireKind kind,
            long sequence)
        {
            if (!IsActiveBinding(item)) return;
            object payload = null;
            int round = -1;
            string inputHash = null;
            if (kind == MatchWireKind.FirstChunkReady
                && MatchProtocol.TryDeserializePayload(item.Envelope, out MatchFirstChunkReadyPayload ready))
            {
                payload = ready;
                round = ready.RoundNumber;
                inputHash = ready.CanonicalInputHash;
            }
            else if (kind == MatchWireKind.FinalSecondHash
                && MatchProtocol.TryDeserializePayload(item.Envelope, out MatchFinalSecondHashPayload hash))
            {
                payload = hash;
                round = hash.RoundNumber;
                inputHash = hash.CanonicalInputHash;
            }
            else if (kind == MatchWireKind.ClientBattleFailure
                && MatchProtocol.TryDeserializePayload(item.Envelope, out MatchClientBattleFailurePayload failure))
            {
                payload = failure;
                round = failure.RoundNumber;
                inputHash = failure.CanonicalInputHash;
            }
            var state = authority.ProjectForHostAuthority();
            if (payload == null
                || state.Flow.SealedRoundPlan == null
                || round != state.RoundNumber
                || !string.Equals(
                    inputHash,
                    state.Flow.SealedRoundPlan.CanonicalInputHash,
                    StringComparison.Ordinal)
                || !MatchesBattleContractScope(
                    kind,
                    payload,
                    state.Flow.SealedRoundPlan))
            {
                return;
            }
            BattleTransportAccepted?.Invoke(new MatchBattleTransportEvent(
                item.BoundPlayerId,
                kind,
                payload,
                sequence));
        }

        private void PublishRecoveryBattleState(string connectionId, long sequence)
        {
            var state = authority.ProjectForHostAuthority();
            if (state.Flow.SealedRoundPlan != null)
            {
                var seal = currentBattleSeal;
                if (seal == null
                    || !HasCurrentBattleScope(
                        seal.RoundNumber,
                        seal.CanonicalInputHash))
                {
                    seal = new MatchBattleSealPayload
                    {
                        RoundNumber = state.RoundNumber,
                        BattleSetId = DefaultBattleSetId(
                            state.RoundNumber),
                        CanonicalInputHash =
                            state.Flow.SealedRoundPlan.CanonicalInputHash,
                        SealedPayload =
                            state.Flow.SealedRoundPlan.CanonicalSummary
                    };
                }
                Dispatch(
                    connectionId,
                    MatchWireKind.BattleSeal,
                    seal,
                    sequence);
            }
            if (currentPlaybackStart != null
                && HasCurrentBattleScope(
                    currentPlaybackStart.RoundNumber,
                    currentPlaybackStart.CanonicalInputHash)
                && MatchesCurrentBattleSet(
                    currentPlaybackStart.BattleSetId))
            {
                Dispatch(
                    connectionId,
                    MatchWireKind.PlaybackStart,
                    currentPlaybackStart,
                    sequence);
            }
            if (currentPlaybackClock != null)
            {
                if (HasCurrentBattleScope(
                    currentPlaybackClock.RoundNumber,
                    currentPlaybackClock.CanonicalInputHash)
                    && MatchesCurrentBattleSet(
                        currentPlaybackClock.BattleSetId))
                {
                    Dispatch(
                        connectionId,
                        MatchWireKind.PlaybackClock,
                        currentPlaybackClock,
                        sequence);
                }
            }
        }

        private bool HasCurrentBattleScope(
            int roundNumber,
            string canonicalInputHash)
        {
            var state = authority.ProjectForHostAuthority();
            return state.Flow.SealedRoundPlan != null
                && roundNumber == state.RoundNumber
                && string.Equals(
                    canonicalInputHash,
                    state.Flow.SealedRoundPlan.CanonicalInputHash,
                    StringComparison.Ordinal);
        }

        private bool MatchesCurrentBattleSet(string battleSetId)
        {
            var expected = currentBattleSeal != null
                && HasCurrentBattleScope(
                    currentBattleSeal.RoundNumber,
                    currentBattleSeal.CanonicalInputHash)
                    ? currentBattleSeal.BattleSetId
                    : DefaultBattleSetId(
                        authority.ProjectForHostAuthority().RoundNumber);
            return string.Equals(
                battleSetId,
                expected,
                StringComparison.Ordinal);
        }

        private bool MatchesBattleContractScope(
            MatchWireKind kind,
            object payload,
            MatchSealedRoundPlan plan)
        {
            if (kind == MatchWireKind.FirstChunkReady)
            {
                return MatchesCurrentBattleSet(
                    ((MatchFirstChunkReadyPayload)payload).BattleSetId);
            }
            var battleId = kind == MatchWireKind.FinalSecondHash
                ? ((MatchFinalSecondHashPayload)payload).BattleId
                : ((MatchClientBattleFailurePayload)payload).BattleId;
            return plan.Pairings.Any(pairing =>
                string.Equals(
                    pairing.BattleId,
                    battleId,
                    StringComparison.Ordinal));
        }

        private string DefaultBattleSetId(int roundNumber)
        {
            return SessionId + "-round-" + roundNumber;
        }

        private MatchCommandAckPayload RejectedAck(
            string commandId,
            MatchCommandCode code,
            string detail,
            long sequence)
        {
            return new MatchCommandAckPayload
            {
                CommandId = string.IsNullOrWhiteSpace(commandId) ? "invalid-command" : commandId,
                ResultCode = code.ToString(),
                CurrentStateRevision = authority.StateRevision,
                StableDetailCode = detail,
                HostAcceptSequence = sequence
            };
        }

        private bool IsActiveBinding(InboundItem item)
        {
            return !string.IsNullOrWhiteSpace(item.BoundPlayerId)
                && participants.TryGetValue(item.BoundPlayerId, out var participant)
                && participant.IsHuman
                && !participant.ExplicitlyQuit
                && participant.ConnectionGeneration == item.ConnectionGeneration
                && string.Equals(
                    participant.ActiveConnectionId,
                    item.ConnectionId,
                    StringComparison.Ordinal);
        }

        private IEnumerable<MatchSessionParticipant> ActiveHumans()
        {
            return participants.Values
                .Where(participant =>
                    participant.IsHuman
                    && !participant.ExplicitlyQuit
                    && !string.IsNullOrWhiteSpace(participant.ActiveConnectionId))
                .OrderBy(participant => participant.SeatIndex);
        }

        private void PublishSnapshots(long sequence, string excludedPlayerId = null)
        {
            foreach (var participant in ActiveHumans())
            {
                if (string.Equals(
                    participant.PlayerId,
                    excludedPlayerId,
                    StringComparison.Ordinal))
                {
                    continue;
                }
                Dispatch(
                    participant.ActiveConnectionId,
                    MatchWireKind.ScopedSnapshot,
                    ProjectScoped(participant.PlayerId),
                    sequence);
            }
        }

        private void PublishClock(long sequence)
        {
            var clock = ProjectClock();
            foreach (var participant in ActiveHumans())
                Dispatch(participant.ActiveConnectionId, MatchWireKind.ClockSync, clock, sequence);
        }

        private MatchClockSyncPayload ProjectClock()
        {
            var state = authority.ProjectForHostAuthority();
            return new MatchClockSyncPayload
            {
                RoundNumber = state.RoundNumber,
                Phase = state.Phase.ToString(),
                HostMonotonicNowMs = state.Flow.LastHostMonotonicMs,
                PreparationDeadlineHostMonotonicMs =
                    state.Flow.HasPreparationClock
                        ? state.Flow.PreparationDeadlineHostMonotonicMs
                        : state.Flow.LastHostMonotonicMs
            };
        }

        private void PublishEndIfNeeded()
        {
            var state = authority.ProjectForHostAuthority();
            if (endedBroadcast || state.Phase != MatchPhase.Ended) return;
            endedBroadcast = true;
            Lifecycle = MatchSessionLifecycle.Ended;
            pendingInitializationTokens.Clear();
            foreach (var participant in participants.Values)
                participant.InvalidateReconnectVerifier();
            var payload = new MatchEndedPayload
            {
                EndReason = state.Flow.EndReason.ToString(),
                FinalRevision = state.StateRevision,
                FinalStandings = state.Flow.FinalStandings.Select(standing => new MatchStandingWire
                {
                    PlayerId = standing.PlayerId,
                    Life = standing.Life,
                    Placement = standing.Placement.GetValueOrDefault(),
                    HasPlacement = standing.Placement.HasValue
                }).ToArray()
            };
            foreach (var participant in ActiveHumans())
                Dispatch(
                    participant.ActiveConnectionId,
                    MatchWireKind.MatchEnded,
                    payload,
                    hostAcceptSequence);
        }

        private void Dispatch(
            string connectionId,
            MatchWireKind kind,
            object payload,
            long sequence,
            string closeConnectionId = null)
        {
            if (string.IsNullOrWhiteSpace(connectionId)) return;
            dispatches.Add(new MatchSessionDispatch(
                connectionId,
                kind,
                payload,
                sequence,
                closeConnectionId));
        }

        private void AssertActorThread()
        {
            if (Environment.CurrentManagedThreadId != actorThreadId)
                throw new InvalidOperationException("MatchAuthority may only be accessed by its owning actor thread.");
        }

        private enum InboundKind
        {
            Wire,
            HostCommand,
            ConnectionLost
        }

        private sealed class InboundItem
        {
            public InboundItem(
                InboundKind kind,
                string connectionId,
                long connectionGeneration,
                string boundPlayerId,
                MatchWireEnvelope envelope,
                object payload)
            {
                Kind = kind;
                ConnectionId = connectionId;
                ConnectionGeneration = connectionGeneration;
                BoundPlayerId = boundPlayerId;
                Envelope = envelope;
                Payload = payload;
            }

            public InboundKind Kind { get; }
            public string ConnectionId { get; }
            public long ConnectionGeneration { get; }
            public string BoundPlayerId { get; }
            public MatchWireEnvelope Envelope { get; }
            public object Payload { get; }
        }
    }
}
