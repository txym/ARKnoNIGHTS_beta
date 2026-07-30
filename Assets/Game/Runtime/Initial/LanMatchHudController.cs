using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using ArknoNights.Deployment;
using ArknoNights.Lobby;
using ArknoNights.Match;
using ArknoNights.Player;
using ArknoNights.UI;
using ArknoNights.UI.FormalHud.ShopReady;
using ArknoNights.UI.PlayerListObserver;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class LanMatchHudController : MonoBehaviour
{
    private LanMatchRuntimeController runtime;
    private UnitCatalog unitCatalog;
    private MatchShopCatalog shopCatalog;
    private StagingHudController stagingHud;
    private StateDrivenDeploymentController deployment;
    private BattleHudSceneCoordinator sceneCoordinator;
    private FormalBattleHudController formalHud;
    private ShopReadyHudController shopReady;
    private PlayerListHudController playerList;
    private Canvas formalCanvas;
    private bool ownsShopReady;
    private bool ownsPlayerList;
    private bool formalHudBound;
    private bool playerListVisible = true;
    private string observedPlayerId;
    private long renderedRevision = -1;
    private int renderedCountdown = -1;
    private IReadOnlyList<PlayerListEntryPresentation> playerEntries =
        new ReadOnlyCollection<PlayerListEntryPresentation>(
            Array.Empty<PlayerListEntryPresentation>());

    public bool UsesFormalBattleHud => formalCanvas != null;

    public void Initialize(
        LanMatchRuntimeController source,
        UnitCatalog units,
        MatchShopCatalog shop)
    {
        runtime = source ?? throw new ArgumentNullException(nameof(source));
        unitCatalog = units ?? throw new ArgumentNullException(nameof(units));
        shopCatalog = shop ?? throw new ArgumentNullException(nameof(shop));
        observedPlayerId = runtime.LocalPlayerId;
        BindFormalHud();
        Refresh();
    }

    public bool TryObservePlayer(string playerId)
    {
        if (runtime?.Snapshot?.PublicState?.Seats == null
            || !runtime.Snapshot.PublicState.Seats.Any(item =>
                item != null
                && string.Equals(
                    item.PlayerId,
                    playerId,
                    StringComparison.Ordinal)))
        {
            return false;
        }

        observedPlayerId = playerId;
        ClearSelection();
        formalHud?.ClearSelectionForSceneTransition();
        Refresh();
        return true;
    }

    public void Tick()
    {
        if (runtime?.Snapshot?.PublicState == null) return;
        var countdown = Mathf.CeilToInt(
            runtime.PreparationRemainingMilliseconds / 1000f);
        if (runtime.Snapshot.StateRevision != renderedRevision
            || countdown != renderedCountdown)
        {
            Refresh();
        }
    }

    public void Refresh()
    {
        if (runtime?.Snapshot?.PublicState == null) return;
        var snapshot = runtime.Snapshot;
        var publicState = snapshot.PublicState;
        var revisionChanged =
            renderedRevision != snapshot.StateRevision;
        renderedRevision = snapshot.StateRevision;
        renderedCountdown = Mathf.CeilToInt(
            runtime.PreparationRemainingMilliseconds / 1000f);

        var localSeat = publicState.Seats?.FirstOrDefault(item =>
            item != null
            && string.Equals(
                item.PlayerId,
                runtime.LocalPlayerId,
                StringComparison.Ordinal));
        if (localSeat == null) return;
        if (!publicState.Seats.Any(item =>
                item != null
                && string.Equals(
                    item.PlayerId,
                    observedPlayerId,
                    StringComparison.Ordinal)))
        {
            observedPlayerId = runtime.LocalPlayerId;
        }

        if (localSeat.Ready
            || localSeat.Eliminated
            || !IsPreparation(publicState))
        {
            ClearSelection();
        }

        playerEntries = BuildPlayerEntries(publicState.Seats);
        playerList?.Refresh();

        var observed = publicState.Seats.First(item =>
            string.Equals(
                item.PlayerId,
                observedPlayerId,
                StringComparison.Ordinal));
        var observedProjection =
            BuildPlayerProjection(
                observed,
                snapshot.OwnerPrivateState);
        var canMutateObserved = CanMutateFormation(publicState)
            && string.Equals(
                observedPlayerId,
                runtime.LocalPlayerId,
                StringComparison.Ordinal);
        if (stagingHud != null)
        {
            stagingHud.SetExternalDisplayedSnapshot(
                observedProjection,
                !canMutateObserved);
        }
        deployment?.SetPreparationViewsVisible(
            IsPreparation(publicState));
        deployment?.ApplyExternalSnapshot(
            observedProjection,
            canMutateObserved);

        if (shopReady != null)
        {
            shopReady.SetPreparationPhase(IsPreparation(publicState));
            shopReady.ApplyExternalState(
                BuildShopProjection(
                    snapshot.OwnerPrivateState,
                    localSeat,
                    publicState),
                revisionChanged);
        }

        if (formalHud != null)
        {
            formalHud.SetSessionHudValues(
                snapshot.OwnerPrivateState?.Gold ?? 0,
                localSeat.Life,
                ResolveOpponentName(publicState));
        }

        UpdateStatus();
    }

    public void ShowCommandResult(MatchOperationResultPayload result)
    {
        if (result == null) return;
        var accepted =
            string.Equals(
                result.ResultCode,
                MatchCommandCode.Accepted.ToString(),
                StringComparison.Ordinal)
            || string.Equals(
                result.ResultCode,
                MatchCommandCode.AcceptedNoChange.ToString(),
                StringComparison.Ordinal)
            || string.Equals(
                result.ResultCode,
                MatchCommandCode.PoolExhaustedDiagnostic.ToString(),
                StringComparison.Ordinal);
        if (string.Equals(
                result.OriginPlayerId,
                runtime.LocalPlayerId,
                StringComparison.Ordinal))
        {
            if (string.Equals(
                    result.CommandKind,
                    MatchCommandKind.PurchaseShopOffer.ToString(),
                    StringComparison.Ordinal))
            {
                shopReady?.ResolveExternalPurchase(
                    result.ShopSlotIndex,
                    result.PrimaryUnitId,
                    accepted);
            }
            else if (string.Equals(
                         result.CommandKind,
                         MatchCommandKind.PurchaseLevelUpgrade
                             .ToString(),
                         StringComparison.Ordinal))
            {
                shopReady?.ResolveExternalUpgrade(accepted);
            }
            deployment?.ResolveExternalOperation(
                result.PrimaryUnitId);
        }
        stagingHud?.ShowExternalStatus(
            accepted ? string.Empty : "操作失败：" + result.StableDetailCode);
    }

    public void ShowStatus(string value)
    {
        stagingHud?.ShowExternalStatus(value);
    }

    public void ResetLocalInteractionState()
    {
        ClearSelection();
        shopReady?.ClearExternalPendingConfirmation();
        deployment?.ClearExternalPendingOperations();
    }

    private void BindFormalHud()
    {
        stagingHud = FindObjectOfType<StagingHudController>();
        if (stagingHud == null)
        {
            Debug.LogWarning(
                "[LanMatch][hud.formal.missing] Running without a visual HUD.",
                this);
            return;
        }

        formalCanvas = stagingHud.GetComponentInChildren<Canvas>(true);
        if (formalCanvas == null)
        {
            Debug.LogError("[LanMatch][hud.canvas.missing]", this);
            return;
        }
        formalCanvas.gameObject.SetActive(true);

        sceneCoordinator =
            stagingHud.GetComponent<BattleHudSceneCoordinator>();
        shopReady = sceneCoordinator?.ShopReady
            ?? stagingHud.GetComponentInChildren<ShopReadyHudController>(true);
        playerList = sceneCoordinator?.PlayerList
            ?? stagingHud.GetComponentInChildren<PlayerListHudController>(true);
        formalHud = stagingHud.GetComponent<FormalBattleHudController>();
        deployment =
            stagingHud.GetComponent<StateDrivenDeploymentController>();

        if (shopReady == null)
        {
            var root = CreateFullCanvasRoot(
                "ShopReadyHud",
                formalCanvas.transform);
            shopReady = root.gameObject.AddComponent<ShopReadyHudController>();
            ownsShopReady = true;
        }
        if (playerList == null)
        {
            var root = CreateFullCanvasRoot(
                "PlayerListPanel",
                formalCanvas.transform);
            playerList = root.gameObject.AddComponent<PlayerListHudController>();
            ownsPlayerList = true;
        }

        sceneCoordinator?.SetExternalMatchMode(true);
        deployment?.InitializeExternal(
            stagingHud,
            SendDeploy,
            SendReplace,
            SendRelocate,
            SendRetreat);

        shopReady.InitializeExternal(
            SendRefresh,
            SubmitUpgradeFromFormalHud,
            SubmitPurchaseFromFormalHud,
            SendToggleFreeze,
            SendReady);
        shopReady.ShopVisibilityChanged += HandleShopVisibilityChanged;
        playerList.SetExternalSource(
            () => playerEntries,
            () => playerListVisible,
            playerId => runtime != null
                && runtime.TryObservePlayer(playerId));

        if (formalHud != null)
        {
            formalHud.SetExternalMatchRuntime(runtime);
            formalHud.SelectionChanged += HandleFormalSelectionChanged;
            formalHudBound = true;
        }
    }

    private void HandleFormalSelectionChanged(bool selected)
    {
        playerListVisible = !selected;
        if (selected) shopReady?.SetShopVisible(false);
        playerList?.Refresh();
    }

    private void HandleShopVisibilityChanged(bool visible)
    {
        if (visible) formalHud?.ClearSelectionForSceneTransition();
    }

    private IReadOnlyList<PlayerListEntryPresentation> BuildPlayerEntries(
        IEnumerable<PublicMatchSeatWire> seats)
    {
        return new ReadOnlyCollection<PlayerListEntryPresentation>(
            (seats ?? Array.Empty<PublicMatchSeatWire>())
                .Where(item => item != null)
                .OrderBy(item => item.SeatIndex)
                .Select(item => new PlayerListEntryPresentation(
                    item.PlayerId,
                    item.DisplayName,
                    "UI/Lobby/Home/" + AvatarSpriteName(item.AvatarId),
                    item.Life,
                    !string.Equals(
                        item.ConnectionState,
                        PublicConnectionState.LostConnection.ToString(),
                        StringComparison.Ordinal),
                    false,
                    string.Equals(
                        item.PlayerId,
                        runtime.LocalPlayerId,
                        StringComparison.Ordinal),
                    string.Equals(
                        item.PlayerId,
                        observedPlayerId,
                        StringComparison.Ordinal)))
                .ToArray());
    }

    private PlayerStateSnapshot BuildPlayerProjection(
        PublicMatchSeatWire observed,
        OwnerMatchStateWire owner)
    {
        var units = (observed.Units ?? Array.Empty<MatchUnitWire>())
            .Where(item => item != null)
            .Select(item => PlayerUnitSnapshot.CreateProjection(
                item.UnitId,
                item.TypeId,
                ParsePlayerZone(item.Zone),
                item.EliteLevel,
                ProjectBuffs(observed, item),
                item.HasFormation
                    ? new LocalFormationCoordinate(
                        item.FormationX,
                        item.FormationY)
                    : (LocalFormationCoordinate?)null))
            .ToArray();

        var stacks = string.Equals(
                observed.PlayerId,
                runtime.LocalPlayerId,
                StringComparison.Ordinal)
            && owner?.StagingStacks != null
                ? owner.StagingStacks
                    .Select(item => BuildStackProjection(
                        item.TypeId,
                        item.EliteLevel,
                        item.DeploymentCost,
                        item.UnitIds,
                        observed))
                    .ToArray()
                : BuildPublicStacks(observed);

        return PlayerStateSnapshot.CreateProjection(
            observed.PlayerId,
            owner?.AvailableDeploymentCost ?? 0,
            renderedRevision,
            units,
            stacks);
    }

    private IReadOnlyList<StagingStackSnapshot> BuildPublicStacks(
        PublicMatchSeatWire seat)
    {
        var stacks = (seat.Units ?? Array.Empty<MatchUnitWire>())
            .Where(item =>
                item != null
                && string.Equals(
                    item.Zone,
                    MatchUnitZone.Staging.ToString(),
                    StringComparison.Ordinal))
            .GroupBy(
                item => StagingBuffKey(seat, item),
                StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group
                    .OrderBy(item => item.UnitId, StringComparer.Ordinal)
                    .First();
                var hasEntry = shopCatalog.TryGet(
                    first.TypeId,
                    out var entry);
                return new
                {
                    First = first,
                    Units = group
                        .OrderBy(item => item.UnitId, StringComparer.Ordinal)
                        .ToArray(),
                    Cost = hasEntry
                        ? MatchEliteRules.GetDeploymentCost(
                            entry,
                            first.EliteLevel)
                        : int.MaxValue,
                    NumericTypeId = hasEntry
                        ? entry.NumericTypeId
                        : long.MaxValue,
                    BuffKey = group.Key
                };
            })
            .OrderBy(item => item.Cost)
            .ThenBy(item => item.NumericTypeId)
            .ThenBy(item => item.First.EliteLevel)
            .ThenBy(item => item.BuffKey, StringComparer.Ordinal)
            .ThenBy(item => item.First.UnitId, StringComparer.Ordinal)
            .Select(item => BuildStackProjection(
                item.First.TypeId,
                item.First.EliteLevel,
                item.Cost,
                item.Units.Select(unit => unit.UnitId),
                seat))
            .ToArray();
        return new ReadOnlyCollection<StagingStackSnapshot>(stacks);
    }

    private StagingStackSnapshot BuildStackProjection(
        string typeId,
        int eliteLevel,
        int deploymentCost,
        IEnumerable<string> unitIds,
        PublicMatchSeatWire seat)
    {
        var ids = (unitIds ?? Array.Empty<string>())
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var first = (seat.Units ?? Array.Empty<MatchUnitWire>())
            .FirstOrDefault(item =>
                item != null
                && ids.Contains(item.UnitId));
        shopCatalog.TryGet(typeId, out var shopEntry);
        unitCatalog.TryGet(typeId, out var unitEntry);
        return StagingStackSnapshot.CreateProjection(
            typeId,
            deploymentCost,
            unitEntry?.PortraitResourcePath ?? string.Empty,
            shopEntry?.Rarity ?? unitEntry?.Rarity ?? 1,
            eliteLevel,
            first == null
                ? Array.Empty<PlayerBuffSnapshot>()
                : ProjectBuffs(seat, first),
            ids);
    }

    private static IEnumerable<PlayerBuffSnapshot> ProjectBuffs(
        PublicMatchSeatWire seat,
        MatchUnitWire unit)
    {
        var inline = (unit.Buffs ?? Array.Empty<MatchBuffWire>())
            .OrderBy(item => item.BuffId, StringComparer.Ordinal)
            .ThenBy(item => item.CanonicalPayload, StringComparer.Ordinal)
            .Select(item => new PlayerBuffSnapshot(
                item.BuffId,
                item.CanonicalPayload));
        var targeted =
            (seat.TargetedUnitBuffs ?? Array.Empty<MatchTargetedBuffWire>())
            .Where(item => string.Equals(
                item.TargetUnitId,
                unit.UnitId,
                StringComparison.Ordinal))
            .OrderBy(item => item.BuffInstanceId, StringComparer.Ordinal)
            .Select(item => new PlayerBuffSnapshot(
                item.BuffTypeId + "/" + item.BuffInstanceId,
                item.CanonicalPayload));
        return inline.Concat(targeted).ToArray();
    }

    private ShopReadyHudState BuildShopProjection(
        OwnerMatchStateWire owner,
        PublicMatchSeatWire localSeat,
        PublicMatchStateWire publicState)
    {
        var preparation = IsPreparation(publicState);
        var battle = string.Equals(
            publicState.Phase,
            MatchPhase.Battle.ToString(),
            StringComparison.Ordinal);
        var commandsEnabled =
            runtime.CanSendCommands && (preparation || battle);
        var offers = owner?.ShopOffers
            ?? Array.Empty<MatchShopOfferWire>();
        var slots = offers
            .OrderBy(item => item.SlotIndex)
            .Select(item =>
            {
                var isEmpty = string.IsNullOrEmpty(item.UnitId);
                shopCatalog.TryGet(item.TypeId, out var shopEntry);
                unitCatalog.TryGet(item.TypeId, out var unitEntry);
                var rarity = item.HasRarity
                    ? item.Rarity
                    : shopEntry?.Rarity ?? unitEntry?.Rarity ?? 1;
                return ShopReadySlotViewState.CreateProjection(
                    item.SlotIndex,
                    item.TypeId,
                    item.UnitId,
                    rarity,
                    shopEntry?.BaseDeploymentCost
                    ?? unitEntry?.DeploymentCost
                    ?? 0,
                    rarity,
                    unitEntry?.DisplayNameZhHans ?? item.TypeId,
                    unitEntry?.PortraitResourcePath ?? string.Empty,
                    isEmpty,
                    item.IsFrozen,
                    commandsEnabled
                    && !isEmpty
                    && owner != null
                    && owner.Gold >= rarity,
                    commandsEnabled && !isEmpty);
            })
            .ToArray();
        return ShopReadyHudState.CreateProjection(
            owner?.Level ?? 1,
            owner?.Gold ?? 0,
            owner?.CurrentUpgradePrice ?? 0,
            localSeat.Ready,
            shopReady != null && shopReady.IsShopVisible,
            ShopReadyConfirmation.None,
            CanMutateFormation(publicState),
            commandsEnabled,
            slots);
    }

    private void UpdateStatus()
    {
        if (runtime.IsReconnecting)
        {
            stagingHud?.ShowExternalStatus("正在重连，只读");
            return;
        }
        if (runtime.HasBattleFailure)
        {
            stagingHud?.ShowExternalStatus(
                "同步错误：" + runtime.BattleFailureDiagnostic);
            return;
        }
        stagingHud?.ShowExternalStatus(string.Empty);
    }

    private bool CanMutateFormation(PublicMatchStateWire publicState)
    {
        if (!runtime.CanSendCommands || !IsPreparation(publicState))
            return false;
        var local = publicState.Seats.FirstOrDefault(item =>
            item != null
            && string.Equals(
                item.PlayerId,
                runtime.LocalPlayerId,
                StringComparison.Ordinal));
        return local != null && !local.Ready && !local.Eliminated;
    }

    private void SendRefresh()
    {
        runtime.SendCommand(new MatchCommandWirePayload
        {
            CommandKind = MatchCommandKind.RefreshShop.ToString()
        });
    }

    private void SendToggleFreeze()
    {
        runtime.SendCommand(new MatchCommandWirePayload
        {
            CommandKind = MatchCommandKind.ToggleShopFreeze.ToString()
        });
    }

    private void SendReady()
    {
        var local = runtime.Snapshot.PublicState.Seats.First(item =>
            string.Equals(
                item.PlayerId,
                runtime.LocalPlayerId,
                StringComparison.Ordinal));
        runtime.SendCommand(new MatchCommandWirePayload
        {
            CommandKind =
                MatchCommandKind.SetPreparationReady.ToString(),
            DesiredReady = !local.Ready
        });
    }

    private void SendDeploy(
        string unitId,
        int x,
        int y)
    {
        runtime.SendCommand(new MatchCommandWirePayload
        {
            CommandKind = MatchCommandKind.DeployUnit.ToString(),
            UnitId = unitId,
            TargetX = x,
            TargetY = y,
            ExpectedAvailableCost =
                runtime.Snapshot.OwnerPrivateState
                    ?.AvailableDeploymentCost ?? 0
        });
    }

    private void SendRelocate(
        string unitId,
        int x,
        int y)
    {
        runtime.SendCommand(new MatchCommandWirePayload
        {
            CommandKind =
                MatchCommandKind.RelocateOrSwapUnit.ToString(),
            UnitId = unitId,
            TargetX = x,
            TargetY = y
        });
    }

    private void SendReplace(
        string stagingUnitId,
        string expectedDeployedUnitId,
        int x,
        int y)
    {
        runtime.SendCommand(new MatchCommandWirePayload
        {
            CommandKind =
                MatchCommandKind.ReplaceDeployedUnit.ToString(),
            StagingUnitId = stagingUnitId,
            ExpectedDeployedUnitId = expectedDeployedUnitId,
            TargetX = x,
            TargetY = y
        });
    }

    private void SendRetreat(string unitId)
    {
        runtime.SendCommand(new MatchCommandWirePayload
        {
            CommandKind = MatchCommandKind.RetreatUnit.ToString(),
            UnitId = unitId
        });
    }

    private void SubmitPurchaseFromFormalHud(int slotIndex)
    {
        var offer = runtime.Snapshot.OwnerPrivateState?.ShopOffers
            ?.FirstOrDefault(item => item.SlotIndex == slotIndex);
        if (offer == null || string.IsNullOrEmpty(offer.UnitId)) return;
        SubmitPurchase(offer);
    }

    private void SubmitUpgradeFromFormalHud()
    {
        SubmitUpgrade(runtime.Snapshot.OwnerPrivateState);
    }

    private void SubmitPurchase(MatchShopOfferWire offer)
    {
        runtime.SendCommand(new MatchCommandWirePayload
        {
            CommandKind =
                MatchCommandKind.PurchaseShopOffer.ToString(),
            SlotIndex = offer.SlotIndex,
            ExpectedUnitId = offer.UnitId
        });
    }

    private void SubmitUpgrade(OwnerMatchStateWire owner)
    {
        if (owner == null) return;
        runtime.SendCommand(new MatchCommandWirePayload
        {
            CommandKind =
                MatchCommandKind.PurchaseLevelUpgrade.ToString(),
            ExpectedCurrentLevel = owner.Level,
            ExpectedCurrentPrice = owner.CurrentUpgradePrice
        });
    }

    private string ResolveOpponentName(PublicMatchStateWire publicState)
    {
        var pairing = (publicState.Pairings
                ?? Array.Empty<PublicMatchPairingWire>())
            .FirstOrDefault(item =>
                string.Equals(
                    item.HomePlayerId,
                    observedPlayerId,
                    StringComparison.Ordinal)
                || string.Equals(
                    item.AwayPlayerId,
                    observedPlayerId,
                    StringComparison.Ordinal));
        if (pairing == null) return "待定";
        var opponentId = string.Equals(
                pairing.HomePlayerId,
                observedPlayerId,
                StringComparison.Ordinal)
            ? pairing.AwayPlayerId
            : pairing.HomePlayerId;
        return publicState.Seats
            .FirstOrDefault(item =>
                string.Equals(
                    item.PlayerId,
                    opponentId,
                    StringComparison.Ordinal))
            ?.DisplayName ?? "待定";
    }

    private static PlayerUnitZone ParsePlayerZone(string value)
    {
        return string.Equals(
            value,
            MatchUnitZone.Deployed.ToString(),
            StringComparison.Ordinal)
            ? PlayerUnitZone.Deployed
            : PlayerUnitZone.Staging;
    }

    private static bool IsPreparation(PublicMatchStateWire publicState)
    {
        return publicState != null
            && string.Equals(
                publicState.Phase,
                MatchPhase.Preparation.ToString(),
                StringComparison.Ordinal);
    }

    private static string StagingBuffKey(
        PublicMatchSeatWire seat,
        MatchUnitWire unit)
    {
        var inline = string.Join(
            "\u001e",
            (unit.Buffs ?? Array.Empty<MatchBuffWire>())
                .OrderBy(item => item.BuffId, StringComparer.Ordinal)
                .ThenBy(
                    item => item.CanonicalPayload,
                    StringComparer.Ordinal)
                .Select(item =>
                    item.BuffId + "\u001f" + item.CanonicalPayload));
        var targeted = string.Join(
            "\u001e",
            (seat.TargetedUnitBuffs
                 ?? Array.Empty<MatchTargetedBuffWire>())
            .Where(item => string.Equals(
                item.TargetUnitId,
                unit.UnitId,
                StringComparison.Ordinal))
            .OrderBy(
                item => item.BuffInstanceId,
                StringComparer.Ordinal)
            .Select(item =>
                item.BuffInstanceId
                + "\u001f" + item.BuffTypeId
                + "\u001f" + item.TargetUnitId
                + "\u001f" + item.CanonicalPayload
                + "\u001f" + item.DiscardPolicy));
        return unit.TypeId
            + "\u001d" + unit.EliteLevel
            + "\u001d" + inline
            + "\u001d" + targeted;
    }

    private static string AvatarSpriteName(string avatarId)
    {
        switch (avatarId)
        {
            case "avatar-0": return "icon_amiy";
            case "avatar-1": return "icon_clementi";
            case "avatar-2": return "icon_kirar";
            default: return "icon_zumam";
        }
    }

    private void ClearSelection()
    {
        stagingHud?.ClearStagingSelection();
    }

    public void DisposeHud()
    {
        if (shopReady != null)
        {
            shopReady.ShopVisibilityChanged -=
                HandleShopVisibilityChanged;
            shopReady.ClearExternalMode();
        }
        playerList?.ClearExternalSource();
        if (formalHudBound && formalHud != null)
        {
            formalHud.SelectionChanged -=
                HandleFormalSelectionChanged;
            formalHud.ClearExternalMatchRuntime();
        }
        formalHudBound = false;
        stagingHud?.RestoreLocalDisplay();
        stagingHud?.ShowExternalStatus(string.Empty);
        deployment?.RestoreHudInputAfterExternalMatch();
        sceneCoordinator?.SetExternalMatchMode(false);
        if (ownsShopReady && shopReady != null)
            Destroy(shopReady.gameObject);
        if (ownsPlayerList && playerList != null)
            Destroy(playerList.gameObject);
        runtime = null;
        unitCatalog = null;
        shopCatalog = null;
        stagingHud = null;
        deployment = null;
        sceneCoordinator = null;
        formalHud = null;
        shopReady = null;
        playerList = null;
        formalCanvas = null;
    }

    private static RectTransform CreateFullCanvasRoot(
        string name,
        Transform parent)
    {
        var value = new GameObject(name, typeof(RectTransform))
            .GetComponent<RectTransform>();
        value.transform.SetParent(parent, false);
        value.anchorMin = Vector2.zero;
        value.anchorMax = Vector2.one;
        value.pivot = new Vector2(.5f, .5f);
        value.offsetMin = Vector2.zero;
        value.offsetMax = Vector2.zero;
        return value;
    }
}
