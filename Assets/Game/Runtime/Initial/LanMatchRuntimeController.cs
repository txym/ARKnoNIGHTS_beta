using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Demo;
using ArknoNights.Battle.Presentation;
using ArknoNights.Lobby;
using ArknoNights.Match;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class LanMatchRuntimeController : MonoBehaviour
{
    public const long PlaybackStartLeadMilliseconds = 1000;
    private const int ComputationBudgetPerFrame = 40;

    private readonly HashSet<string> readyPlayers =
        new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> reportedFinalHashes =
        new HashSet<string>(StringComparer.Ordinal);
    private readonly Dictionary<string, MatchFinalSecondHashPayload> remoteHashes =
        new Dictionary<string, MatchFinalSecondHashPayload>(StringComparer.Ordinal);
    private readonly HostClockEstimator clockEstimator = new HostClockEstimator();
    private readonly MatchResultClientState projectedState =
        new MatchResultClientState();
    private readonly Stopwatch localClock = Stopwatch.StartNew();

    private LanRoomHost host;
    private LanRoomClient client;
    private LobbyProfile profile;
    private LanMatchRuntimeAssets assets;
    private ScopedSnapshotPayload snapshot;
    private MatchClockSyncPayload clock;
    private MatchBattleSealPayload seal;
    private MatchPlaybackStartPayload playbackStart;
    private MultiBattlePresentationCoordinator battles;
    private LanMatchBattleSet battleSet;
    private LanMatchHudController hud;
    private HashSet<string> barrierPlayers;
    private int activeBattleRound;
    private bool localReadySent;
    private bool settlementSubmitted;
    private bool reconnecting;
    private bool exitRaised;
    private string battleFailureDiagnostic = string.Empty;

    public event Action<string> ExitRequested;

    public string LocalPlayerId => profile == null ? string.Empty : profile.PlayerId;
    public ScopedSnapshotPayload Snapshot => snapshot;
    public bool IsHost => host != null;
    public bool IsReconnecting => reconnecting;
    public bool HasBattle => battles != null;
    public bool HasBattleFailure =>
        !string.IsNullOrEmpty(battleFailureDiagnostic);
    public string BattleFailureDiagnostic => battleFailureDiagnostic;
    public MultiBattlePresentationState BattleState =>
        battles == null ? MultiBattlePresentationState.Idle : battles.State;
    public int PresentationTick =>
        battles == null ? 0 : Mathf.FloorToInt((float)battles.PresentationTick);
    public int BattleRemainingSeconds
    {
        get
        {
            if (snapshot?.PublicState == null
                || !string.Equals(
                    snapshot.PublicState.Phase,
                    MatchPhase.Battle.ToString(),
                    StringComparison.Ordinal))
            {
                return 0;
            }
            var maximumTicks = battles != null
                && battles.AllBattlesTerminal
                    ? battles.GlobalRoundEndTick
                    : CurrentBattleInput?.MaxTicks
                      ?? LanMatchBattleAdapter.MaximumBattleTicks;
            var currentTick = playbackStart == null
                ? 0
                : EstimateAuthoritativeTick();
            var remainingTicks = Math.Max(0, maximumTicks - currentTick);
            return (remainingTicks + BattleInput.TicksPerSecond - 1)
                / BattleInput.TicksPerSecond;
        }
    }
    public IReadOnlyList<BattlePresentationViewState> CurrentBattleViewStates =>
        battles == null
            ? Array.Empty<BattlePresentationViewState>()
            : battles.PresentationViewStates;
    public BattleInput CurrentBattleInput =>
        battles == null
            ? null
            : battles.Matches
                .FirstOrDefault(item =>
                    string.Equals(
                        item.MatchId,
                        battles.SelectedMatchId,
                        StringComparison.Ordinal))
                ?.Input;
    public BattleSide CurrentObserverSide =>
        battles != null && battles.Observer == BattleObserverView.Away
            ? BattleSide.Away
            : BattleSide.Home;
    public long PreparationRemainingMilliseconds
    {
        get
        {
            if (snapshot?.PublicState == null
                || !string.Equals(
                    snapshot.PublicState.Phase,
                    MatchPhase.Preparation.ToString(),
                    StringComparison.Ordinal))
            {
                return 0;
            }
            if (clock == null
                || clock.RoundNumber != snapshot.PublicState.RoundNumber
                || !string.Equals(
                    clock.Phase,
                    MatchPhase.Preparation.ToString(),
                    StringComparison.Ordinal))
                return snapshot.PublicState.PreparationRemainingMs;
            var hostNow = host != null
                ? LanRoomHost.HostMonotonicNowMs
                : clockEstimator.EstimateHostNow(localClock.ElapsedMilliseconds);
            return Math.Max(
                0,
                clock.PreparationDeadlineHostMonotonicMs - hostNow);
        }
    }

    internal bool InitializeHost(
        LanRoomHost roomHost,
        LobbyProfile localProfile,
        LanMatchRuntimeAssets runtimeAssets,
        out string diagnosticCode)
    {
        if (roomHost == null
            || roomHost.SessionActor == null
            || roomHost.HostInitialization == null)
        {
            diagnosticCode = "match.runtime.host.initialization.missing";
            return false;
        }
        DisposeRuntime();
        host = roomHost;
        profile = localProfile;
        assets = runtimeAssets;
        host.HostDispatchReceived += OnHostDispatch;
        host.SessionActor.BattleTransportAccepted += OnBattleTransportAccepted;
        ApplySnapshot(roomHost.HostInitialization.Snapshot);
        ApplyClock(roomHost.HostInitialization.Clock);
        InitializeHud();
        diagnosticCode = string.Empty;
        return true;
    }

    internal bool InitializeGuest(
        LanRoomClient roomClient,
        LobbyProfile localProfile,
        LanMatchRuntimeAssets runtimeAssets,
        out string diagnosticCode)
    {
        if (roomClient == null
            || roomClient.MatchSnapshot == null
            && roomClient.MatchInitialization == null)
        {
            diagnosticCode = "match.runtime.guest.initialization.missing";
            return false;
        }
        DisposeRuntime();
        client = roomClient;
        profile = localProfile;
        assets = runtimeAssets;
        SubscribeClient(client);
        ApplySnapshot(client.MatchSnapshot
            ?? client.MatchInitialization.Snapshot);
        ApplyClock(client.CurrentClock
            ?? client.MatchInitialization?.Clock);
        if (client.CurrentBattleSeal != null)
            ReceiveBattleSeal(client.CurrentBattleSeal);
        if (client.CurrentPlaybackStart != null)
            ReceivePlaybackStart(client.CurrentPlaybackStart);
        InitializeHud();
        diagnosticCode = string.Empty;
        return true;
    }

    public bool RebindClient(
        LanRoomClient roomClient,
        out string diagnosticCode)
    {
        if (roomClient == null || roomClient.MatchSnapshot == null)
        {
            diagnosticCode = "match.runtime.reconnect.snapshot.missing";
            return false;
        }
        if (client != null) UnsubscribeClient(client);
        client = roomClient;
        host = null;
        reconnecting = false;
        SubscribeClient(client);
        ApplySnapshot(client.MatchSnapshot);
        ApplyClock(client.CurrentClock);
        if (client.CurrentBattleSeal != null)
            ReceiveBattleSeal(client.CurrentBattleSeal);
        if (client.CurrentPlaybackStart != null)
            ReceivePlaybackStart(client.CurrentPlaybackStart);
        hud?.Refresh();
        diagnosticCode = string.Empty;
        return true;
    }

    public void SetReconnecting(bool value)
    {
        reconnecting = value;
        hud?.Refresh();
    }

    public bool TryObservePlayer(string playerId)
    {
        var battleChanged = false;
        if (battles != null)
        {
            if (!battles.SelectObservedPlayer(playerId))
                return false;
            battleChanged = true;
        }
        var hudChanged = hud != null && hud.TryObservePlayer(playerId);
        return battleChanged || hudChanged;
    }

    public bool CanSendCommands
    {
        get
        {
            if (reconnecting
                || snapshot?.PublicState == null
                || snapshot.OwnerPrivateState == null
                || !string.Equals(
                    snapshot.OwnerPrivateState.PlayerId,
                    LocalPlayerId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    snapshot.LocalConnectionState,
                    MatchLocalConnectionState.Connected.ToString(),
                    StringComparison.Ordinal))
            {
                return false;
            }
            var localSeat = snapshot.PublicState.Seats?.FirstOrDefault(item =>
                item != null
                && string.Equals(item.PlayerId, LocalPlayerId, StringComparison.Ordinal));
            return localSeat != null && !localSeat.Eliminated;
        }
    }

    public void SendCommand(MatchCommandWirePayload command)
    {
        if (!CanSendCommands || command == null) return;
        command.CommandId = string.IsNullOrWhiteSpace(command.CommandId)
            ? "ui-" + Guid.NewGuid().ToString("N")
            : command.CommandId;
        command.KnownStateRevision = snapshot.StateRevision;
        try
        {
            if (host != null) host.EnqueueHostCommand(command);
            else if (client != null) _ = client.SendCommandAsync(command);
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning(
                "[LanMatch][command.send.failed] "
                + exception.GetType().Name,
                this);
        }
    }

    public void RequestExplicitQuit()
    {
        if (exitRaised) return;
        if (host != null)
            host.AbortMatch("match.host.explicitQuit");
        else if (client != null)
            _ = client.LeaveAsync();
        RaiseExit("Left match.");
    }

    private void Update()
    {
        if (assets == null || snapshot?.PublicState == null)
            return;

        if (string.Equals(
                snapshot.PublicState.Phase,
                MatchPhase.Battle.ToString(),
                StringComparison.Ordinal))
        {
            if (host != null
                && (seal == null
                    || seal.RoundNumber != snapshot.PublicState.RoundNumber))
            {
                BeginHostBattleRound();
            }
            AdvanceBattleRound();
        }
        else if (battles != null
                 && !string.Equals(
                     snapshot.PublicState.Phase,
                     MatchPhase.Battle.ToString(),
                     StringComparison.Ordinal))
        {
            ClearBattleRound();
        }
        hud?.Tick();
    }

    private void BeginHostBattleRound()
    {
        var plan = host.SessionActor.ProjectHostState().Flow.SealedRoundPlan;
        if (plan == null
            || plan.RoundNumber != snapshot.PublicState.RoundNumber)
        {
            AbortHost("match.runtime.battle.plan.missing");
            return;
        }
        var sealedHashes = plan.Pairings.ToDictionary(
            item => item.BattleId,
            item => item.SealedInputHash,
            StringComparer.Ordinal);
        if (!TryPrepareLocalBattles(sealedHashes, out var diagnosticCode))
        {
            AbortHost(diagnosticCode);
            return;
        }
        seal = new MatchBattleSealPayload
        {
            RoundNumber = plan.RoundNumber,
            BattleSetId = snapshot.SessionId + "-round-" + plan.RoundNumber,
            CanonicalInputHash = plan.CanonicalInputHash,
            SealedPayload = plan.CanonicalSummary,
            BattleInputs = battleSet.Requests.Select(item =>
                new MatchBattleInputHashWire
                {
                    BattleId = item.MatchId,
                    InputSha256 = battleSet.InputHashes[item.MatchId],
                    SealedInputHash = item.SealedInputHash
                }).ToArray()
        };
        activeBattleRound = plan.RoundNumber;
        barrierPlayers = new HashSet<string>(
            host.SessionActor.Participants
                .Where(item =>
                    item.IsHuman
                    && !item.ExplicitlyQuit
                    && !string.IsNullOrWhiteSpace(item.ActiveConnectionId))
                .Select(item => item.PlayerId),
            StringComparer.Ordinal);
        host.PublishBattleSeal(seal);
    }

    private void ReceiveBattleSeal(MatchBattleSealPayload received)
    {
        if (received == null
            || snapshot?.PublicState == null
            || !string.Equals(snapshot.SessionId, snapshot.PublicState.SessionId, StringComparison.Ordinal)
            || received.RoundNumber != snapshot.PublicState.RoundNumber
            || received.BattleInputs == null
            || received.BattleInputs.Length != snapshot.PublicState.Pairings.Length)
        {
            ReportLocalBattleFailure(
                received,
                "match.runtime.battle.seal.invalid");
            return;
        }
        if (seal != null
            && seal.RoundNumber == received.RoundNumber
            && battles != null)
        {
            return;
        }
        seal = received;
        activeBattleRound = received.RoundNumber;
        var sealedHashes = received.BattleInputs.ToDictionary(
            item => item.BattleId,
            item => item.SealedInputHash,
            StringComparer.Ordinal);
        if (!TryPrepareLocalBattles(sealedHashes, out var diagnosticCode))
        {
            ReportLocalBattleFailure(received, diagnosticCode);
            return;
        }
        foreach (var item in received.BattleInputs)
        {
            if (!battleSet.InputHashes.TryGetValue(item.BattleId, out var localHash)
                || !string.Equals(localHash, item.InputSha256, StringComparison.OrdinalIgnoreCase))
            {
                ReportLocalBattleFailure(
                    received,
                    "match.runtime.battle.inputHash.mismatch",
                    item.BattleId);
                return;
            }
        }
    }

    private bool TryPrepareLocalBattles(
        IReadOnlyDictionary<string, string> sealedHashes,
        out string diagnosticCode)
    {
        ClearBattlePresentationOnly();
        if (!LanMatchBattleAdapter.TryCreate(
                snapshot,
                assets.Units,
                assets.Abilities,
                sealedHashes,
                out battleSet,
                out diagnosticCode))
        {
            return false;
        }
        var factory = FindObjectOfType<MappedBattlePresentationViewFactory>();
        if (factory == null)
        {
            var factoryObject = new GameObject(
                "LanMatchBattlePresentationFactory");
            factoryObject.transform.SetParent(transform, false);
            factory = factoryObject.AddComponent<MappedBattlePresentationViewFactory>();
        }
        battles = new MultiBattlePresentationCoordinator();
        if (!battles.Prepare(
                battleSet.Requests,
                battleSet.Observations,
                factory,
                LocalPlayerId))
        {
            diagnosticCode = battles.LastError;
            ClearBattlePresentationOnly();
            return false;
        }
        localReadySent = false;
        settlementSubmitted = false;
        battleFailureDiagnostic = string.Empty;
        reportedFinalHashes.Clear();
        remoteHashes.Clear();
        readyPlayers.Clear();
        playbackStart = null;
        hud?.Refresh();
        diagnosticCode = string.Empty;
        return true;
    }

    private void AdvanceBattleRound()
    {
        if (battles == null || seal == null) return;
        if (playbackStart == null)
        {
            if (!battles.AllTracksReady
                && !battles.PumpComputation(ComputationBudgetPerFrame))
            {
                ReportLocalBattleFailure(
                    seal,
                    battles.LastError);
                return;
            }
            ReportPlaybackReadyIfNeeded();
            if (host != null)
                TryStartHostPlayback();
            return;
        }

        var targetTick = EstimateAuthoritativeTick();
        if (!battles.AdvanceToAuthoritativeTick(
                targetTick,
                0))
        {
            ReportLocalBattleFailure(seal, battles.LastError);
            return;
        }
        ReportFinalHashes();
        if (host != null)
        {
            TryCommitHostSettlement();
        }
    }

    private void ReportPlaybackReadyIfNeeded()
    {
        if (localReadySent || !battles.AllTracksReady) return;
        if (!battles.PrepareCompletedPlayback())
        {
            ReportLocalBattleFailure(
                seal,
                "match.runtime.playback.prepareFailed");
            return;
        }
        localReadySent = true;
        var payload = new MatchFirstChunkReadyPayload
        {
            RoundNumber = seal.RoundNumber,
            BattleSetId = seal.BattleSetId,
            CanonicalInputHash = seal.CanonicalInputHash,
            ReadyRevision = snapshot.StateRevision
        };
        try
        {
            if (host != null)
                host.EnqueueHostBattleContract(MatchWireKind.FirstChunkReady, payload);
            else
                _ = client.SendFirstChunkReadyAsync(payload);
        }
        catch (Exception)
        {
            ReportLocalBattleFailure(
                seal,
                "match.runtime.firstChunkReady.sendFailed");
        }
    }

    private void TryStartHostPlayback()
    {
        if (playbackStart != null || barrierPlayers == null) return;
        barrierPlayers.RemoveWhere(playerId =>
        {
            var participant = host.SessionActor.Participants.FirstOrDefault(item =>
                string.Equals(item.PlayerId, playerId, StringComparison.Ordinal));
            return participant == null
                || participant.ExplicitlyQuit
                || string.IsNullOrWhiteSpace(participant.ActiveConnectionId);
        });
        var hostReady = readyPlayers.Contains(LocalPlayerId);
        var allReady = barrierPlayers.All(item => readyPlayers.Contains(item));
        if (!hostReady || !allReady)
            return;
        var now = LanRoomHost.HostMonotonicNowMs;
        var start = new MatchPlaybackStartPayload
        {
            RoundNumber = seal.RoundNumber,
            BattleSetId = seal.BattleSetId,
            CanonicalInputHash = seal.CanonicalInputHash,
            HostMonotonicStartMs = now + PlaybackStartLeadMilliseconds,
            StartTick = 0
        };
        if (!host.PublishPlaybackStart(start))
        {
            AbortHost("match.runtime.playbackStart.publishFailed");
            return;
        }
        ReceivePlaybackStart(start);
    }

    private int EstimateAuthoritativeTick()
    {
        var hostNow = host != null
            ? LanRoomHost.HostMonotonicNowMs
            : clockEstimator.EstimateHostNow(localClock.ElapsedMilliseconds);
        var elapsed = Math.Max(0, hostNow - playbackStart.HostMonotonicStartMs);
        var tick = (int)Math.Min(
            int.MaxValue,
            elapsed * BattleInput.TicksPerSecond / 1000L);
        var maximum = battles.AllBattlesTerminal
            ? battles.GlobalRoundEndTick
            : LanMatchBattleAdapter.MaximumBattleTicks;
        return Math.Max(0, Math.Min(tick, maximum));
    }

    private void ReportFinalHashes()
    {
        foreach (var match in battles.Matches.Where(item => item.IsTerminal))
        {
            if (!reportedFinalHashes.Add(match.MatchId)) continue;
            var final = match.GetFinalSecondHashPayload();
            var payload = new MatchFinalSecondHashPayload
            {
                RoundNumber = seal.RoundNumber,
                BattleId = match.MatchId,
                CanonicalInputHash = seal.CanonicalInputHash,
                BattleInputSha256 = battleSet.InputHashes[match.MatchId],
                FinalSecondSha256 = final.FinalSecondSha256
            };
            try
            {
                if (host != null)
                    host.EnqueueHostBattleContract(MatchWireKind.FinalSecondHash, payload);
                else
                    _ = client.SendFinalSecondHashAsync(payload);
            }
            catch (Exception)
            {
                UnityEngine.Debug.LogWarning(
                    "[LanMatch][finalHash.send.failed] battle="
                    + match.MatchId,
                    this);
            }
        }
        ComparePendingHashes();
    }

    private void TryCommitHostSettlement()
    {
        if (settlementSubmitted
            || battles.State != MultiBattlePresentationState.Completed
            || !battles.AllBattlesTerminal)
        {
            return;
        }
        var mapped = new List<MatchBattleResolution>();
        foreach (var match in battles.Matches.OrderBy(item => item.MatchId, StringComparer.Ordinal))
        {
            var result = match.GetBattleResolution();
            if (!TryMapTerminalReason(
                    result.TerminalReason,
                    out var terminalReason))
            {
                AbortHost("match.runtime.battle.terminalReason.unsupported");
                return;
            }
            mapped.Add(new MatchBattleResolution(
                result.BattleId,
                result.SealedInputHash,
                result.HomeLifeDamage,
                result.AwayLifeDamage,
                MatchBattleResolution.DeriveOutcome(
                    result.HomeLifeDamage,
                    result.AwayLifeDamage),
                terminalReason,
                result.EndTick));
        }
        settlementSubmitted = true;
        if (!host.CompleteBattleRound(mapped, out var diagnosticCode))
        {
            settlementSubmitted = false;
            AbortHost(diagnosticCode);
        }
    }

    private static bool TryMapTerminalReason(
        BattleStopReason source,
        out MatchBattleTerminalReason target)
    {
        switch (source)
        {
            case BattleStopReason.Victory:
                target = MatchBattleTerminalReason.Normal;
                return true;
            case BattleStopReason.MutualAnnihilation:
                target = MatchBattleTerminalReason.MutualElimination;
                return true;
            case BattleStopReason.MaxTicksReached:
                target = MatchBattleTerminalReason.MaximumTicksReached;
                return true;
            default:
                target = default(MatchBattleTerminalReason);
                return false;
        }
    }

    private void OnHostDispatch(MatchSessionDispatch dispatch)
    {
        if (dispatch == null) return;
        switch (dispatch.Kind)
        {
            case MatchWireKind.RecoveryState:
                ApplySnapshot(dispatch.Payload as ScopedSnapshotPayload);
                break;
            case MatchWireKind.OperationResult:
                ReceiveOperationResult(
                    dispatch.Payload as MatchOperationResultPayload);
                break;
            case MatchWireKind.SystemResult:
                ReceiveSystemResult(
                    dispatch.Payload as MatchSystemResultPayload);
                break;
        }
    }

    private void OnBattleTransportAccepted(MatchBattleTransportEvent accepted)
    {
        if (accepted == null) return;
        if (accepted.Kind == MatchWireKind.FirstChunkReady)
        {
            readyPlayers.Add(accepted.PlayerId);
            return;
        }
        if (accepted.Kind == MatchWireKind.FinalSecondHash
            && accepted.Payload is MatchFinalSecondHashPayload hash)
        {
            remoteHashes[accepted.PlayerId + "\u001f" + hash.BattleId] = hash;
            ComparePendingHashes();
        }
        else if (accepted.Kind == MatchWireKind.ClientBattleFailure)
        {
            UnityEngine.Debug.LogWarning(
                "[LanMatch][guest.battle.failed] player="
                + accepted.PlayerId,
                this);
        }
    }

    private void ComparePendingHashes()
    {
        if (battleSet == null || battles == null) return;
        foreach (var pair in remoteHashes.ToArray())
        {
            var remote = pair.Value;
            var local = battles.Matches.FirstOrDefault(item =>
                string.Equals(item.MatchId, remote.BattleId, StringComparison.Ordinal));
            if (local == null || !local.IsTerminal) continue;
            var localFinal = local.GetFinalSecondHashPayload().FinalSecondSha256;
            var localInput = battleSet.InputHashes[remote.BattleId];
            if (!string.Equals(
                    localInput,
                    remote.BattleInputSha256,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    localFinal,
                    remote.FinalSecondSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                UnityEngine.Debug.LogWarning(
                    "[LanMatch][battle.hash.mismatch] session="
                    + snapshot.SessionId
                    + "; round="
                    + seal.RoundNumber
                    + "; battle="
                    + remote.BattleId
                    + "; reporter="
                    + pair.Key.Split('\u001f')[0]
                    + "; localInput="
                    + localInput
                    + "; remoteInput="
                    + remote.BattleInputSha256
                    + "; localFinal="
                    + localFinal
                    + "; remoteFinal="
                    + remote.FinalSecondSha256,
                    this);
            }
            remoteHashes.Remove(pair.Key);
        }
    }

    private void SubscribeClient(LanRoomClient value)
    {
        value.MatchSnapshotChanged += ApplySnapshot;
        value.RecoveryStateApplied += OnRecoveryStateApplied;
        value.ClockSynchronized += ApplyClock;
        value.BattleSealReceived += ReceiveBattleSeal;
        value.PlaybackStarted += ReceivePlaybackStart;
        value.MatchEnded += OnClientEnded;
        value.OperationResultReceived += ReceiveOperationResult;
        value.SystemResultReceived += ReceiveSystemResult;
        value.Reconnecting += OnClientReconnecting;
    }

    private void UnsubscribeClient(LanRoomClient value)
    {
        value.MatchSnapshotChanged -= ApplySnapshot;
        value.RecoveryStateApplied -= OnRecoveryStateApplied;
        value.ClockSynchronized -= ApplyClock;
        value.BattleSealReceived -= ReceiveBattleSeal;
        value.PlaybackStarted -= ReceivePlaybackStart;
        value.MatchEnded -= OnClientEnded;
        value.OperationResultReceived -= ReceiveOperationResult;
        value.SystemResultReceived -= ReceiveSystemResult;
        value.Reconnecting -= OnClientReconnecting;
    }

    private void ApplySnapshot(ScopedSnapshotPayload value)
    {
        if (value == null
            || value.PublicState == null
            || snapshot != null
            && value.StateRevision < snapshot.StateRevision)
        {
            return;
        }
        if (!projectedState.TryApplyRecovery(value))
            return;
        var previous = snapshot;
        snapshot = value;
        SynchronizePreparationClockOnTransition(previous, value);
        reconnecting = false;
        if (string.Equals(
                value.PublicState.Phase,
                MatchPhase.Ended.ToString(),
                StringComparison.Ordinal))
        {
            RaiseExit("Match ended.");
        }
        hud?.Refresh();
    }

    private void ReceiveOperationResult(
        MatchOperationResultPayload result)
    {
        if (result == null) return;
        ApplyDelta(result.Delta);
        hud?.ShowCommandResult(result);
    }

    private void ReceiveSystemResult(
        MatchSystemResultPayload result)
    {
        if (result == null) return;
        ApplyDelta(result.Delta);
        if (host == null) return;
        if (result.BattleSeal != null)
            ReceiveBattleSeal(result.BattleSeal);
        if (result.PlaybackStart != null)
            ReceivePlaybackStart(result.PlaybackStart);
        if (result.MatchEnded != null)
            OnClientEnded(result.MatchEnded);
    }

    private void ApplyDelta(MatchStateDeltaWire delta)
    {
        if (delta == null) return;
        var previous = snapshot;
        var status = projectedState.TryApply(delta);
        if (status != MatchDeltaApplyStatus.Applied)
            return;
        var value = projectedState.Current;
        snapshot = value;
        SynchronizePreparationClockOnTransition(previous, value);
        reconnecting = false;
        if (string.Equals(
                value.PublicState.Phase,
                MatchPhase.Ended.ToString(),
                StringComparison.Ordinal))
        {
            RaiseExit("Match ended.");
        }
        hud?.Refresh();
    }

    private void ApplyClock(MatchClockSyncPayload value)
    {
        if (value == null) return;
        clock = value;
        if (host == null)
        {
            clockEstimator.AddSample(
                value.HostMonotonicNowMs,
                localClock.ElapsedMilliseconds,
                client == null ? 0 : client.LatencyMilliseconds);
        }
        hud?.Refresh();
    }

    private void SynchronizePreparationClockOnTransition(
        ScopedSnapshotPayload previous,
        ScopedSnapshotPayload current)
    {
        var alreadyTrackingCurrentPreparation =
            previous?.PublicState != null
            && string.Equals(
                previous.PublicState.Phase,
                MatchPhase.Preparation.ToString(),
                StringComparison.Ordinal)
            && previous.PublicState.RoundNumber
                == current?.PublicState?.RoundNumber
            && clock != null
            && clock.RoundNumber == current.PublicState.RoundNumber
            && string.Equals(
                clock.Phase,
                MatchPhase.Preparation.ToString(),
                StringComparison.Ordinal);
        if (current?.PublicState == null
            || !string.Equals(
                current.PublicState.Phase,
                MatchPhase.Preparation.ToString(),
                StringComparison.Ordinal)
            || alreadyTrackingCurrentPreparation)
        {
            return;
        }

        var hostNow = host != null
            ? LanRoomHost.HostMonotonicNowMs
            : clockEstimator.EstimateHostNow(localClock.ElapsedMilliseconds);
        var remaining = Math.Max(
            0,
            current.PublicState.PreparationRemainingMs);
        var deadline = hostNow > long.MaxValue - remaining
            ? long.MaxValue
            : hostNow + remaining;
        clock = new MatchClockSyncPayload
        {
            RoundNumber = current.PublicState.RoundNumber,
            Phase = MatchPhase.Preparation.ToString(),
            HostMonotonicNowMs = hostNow,
            PreparationDeadlineHostMonotonicMs = deadline
        };
    }

    private void ReceivePlaybackStart(MatchPlaybackStartPayload value)
    {
        if (!MatchesSeal(value)) return;
        playbackStart = value;
        if (host == null)
        {
            clockEstimator.AddSample(
                value.HostMonotonicStartMs - PlaybackStartLeadMilliseconds,
                localClock.ElapsedMilliseconds,
                client == null ? 0 : client.LatencyMilliseconds);
        }
    }

    private bool MatchesSeal(MatchPlaybackStartPayload value)
    {
        return value != null
            && seal != null
            && value.RoundNumber == seal.RoundNumber
            && string.Equals(value.BattleSetId, seal.BattleSetId, StringComparison.Ordinal)
            && string.Equals(
                value.CanonicalInputHash,
                seal.CanonicalInputHash,
                StringComparison.Ordinal);
    }

    private void OnClientEnded(MatchEndedPayload ignored)
    {
        RaiseExit("Match ended.");
    }

    private void OnClientReconnecting()
    {
        SetReconnecting(true);
    }

    private void OnRecoveryStateApplied()
    {
        hud?.ResetLocalInteractionState();
    }

    private void ReportLocalBattleFailure(
        MatchBattleSealPayload scope,
        string diagnosticCode,
        string battleId = null)
    {
        diagnosticCode = string.IsNullOrWhiteSpace(diagnosticCode)
            ? "match.runtime.battle.failed"
            : diagnosticCode;
        if (host != null)
        {
            AbortHost(diagnosticCode);
            return;
        }
        if (client != null && scope != null)
        {
            try
            {
                _ = client.ReportBattleFailureAsync(
                    new MatchClientBattleFailurePayload
                    {
                        RoundNumber = scope.RoundNumber,
                        BattleId = battleId
                            ?? scope.BattleInputs?.FirstOrDefault()?.BattleId
                            ?? "unknown-battle",
                        CanonicalInputHash = scope.CanonicalInputHash,
                        StableDetailCode = diagnosticCode
                    });
            }
            catch (Exception) { }
        }
        battleFailureDiagnostic = diagnosticCode;
        ClearBattlePresentationOnly();
        battleSet = null;
        hud?.ShowStatus("Synchronization error: " + diagnosticCode);
    }

    private void AbortHost(string diagnosticCode)
    {
        if (host == null) return;
        UnityEngine.Debug.LogError(
            "[LanMatch][host.abort] code=" + diagnosticCode,
            this);
        host.AbortMatch(diagnosticCode);
        RaiseExit("Match stopped: " + diagnosticCode);
    }

    private void InitializeHud()
    {
        hud = GetComponentInChildren<LanMatchHudController>(true);
        if (hud == null)
        {
            var hudObject = new GameObject("LanMatchHud");
            hudObject.transform.SetParent(transform, false);
            hud = hudObject.AddComponent<LanMatchHudController>();
        }
        hud.Initialize(this, assets.Units, assets.Shop);
    }

    private void RaiseExit(string status)
    {
        if (exitRaised) return;
        exitRaised = true;
        ExitRequested?.Invoke(status);
    }

    private void ClearBattleRound()
    {
        ClearBattlePresentationOnly();
        battleSet = null;
        seal = null;
        playbackStart = null;
        barrierPlayers = null;
        activeBattleRound = 0;
        localReadySent = false;
        settlementSubmitted = false;
        battleFailureDiagnostic = string.Empty;
        readyPlayers.Clear();
        reportedFinalHashes.Clear();
        remoteHashes.Clear();
        hud?.Refresh();
    }

    private void ClearBattlePresentationOnly()
    {
        battles?.Dispose();
        battles = null;
    }

    public void DisposeRuntime()
    {
        if (host != null)
        {
            host.HostDispatchReceived -= OnHostDispatch;
            if (host.SessionActor != null)
                host.SessionActor.BattleTransportAccepted -= OnBattleTransportAccepted;
        }
        if (client != null) UnsubscribeClient(client);
        if (hud != null)
        {
            var activeHud = hud;
            hud = null;
            activeHud.DisposeHud();
            Destroy(activeHud.gameObject);
        }
        ClearBattleRound();
        host = null;
        client = null;
        profile = null;
        assets = null;
        snapshot = null;
        clock = null;
        reconnecting = false;
        exitRaised = false;
    }

    private void OnDestroy()
    {
        DisposeRuntime();
    }
}

public sealed class HostClockEstimator
{
    private const int MaximumSamples = 7;
    private readonly Queue<long> offsets = new Queue<long>();
    private long lastEstimate;

    public void AddSample(
        long hostMonotonicNowMs,
        long localMonotonicNowMs,
        long roundTripMilliseconds)
    {
        if (hostMonotonicNowMs < 0 || localMonotonicNowMs < 0) return;
        var oneWay = Math.Max(0, roundTripMilliseconds) / 2;
        offsets.Enqueue(hostMonotonicNowMs + oneWay - localMonotonicNowMs);
        while (offsets.Count > MaximumSamples) offsets.Dequeue();
    }

    public long EstimateHostNow(long localMonotonicNowMs)
    {
        var ordered = offsets.OrderBy(item => item).ToArray();
        var offset = ordered.Length == 0
            ? 0
            : ordered[ordered.Length / 2];
        var estimate = Math.Max(0, localMonotonicNowMs + offset);
        lastEstimate = Math.Max(lastEstimate, estimate);
        return lastEstimate;
    }
}
