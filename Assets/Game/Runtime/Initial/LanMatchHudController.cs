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
using UnityEngine.EventSystems;
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
    private string selectedStagingUnitId;
    private string selectedDeployedUnitId;
    private string pendingReplaceUnitId;
    private MatchFormationPosition? pendingReplacePosition;
    private int pendingPurchaseSlot = -1;
    private string pendingPurchaseUnitId;
    private int pendingUpgradeLevel = -1;
    private int pendingUpgradePrice = -1;
    private long renderedRevision = -1;
    private int renderedCountdown = -1;
    private readonly Dictionary<string, PreparationUnitView> formationViews =
        new Dictionary<string, PreparationUnitView>(StringComparer.Ordinal);
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
        HandleWorldInput();
    }

    public void Refresh()
    {
        if (runtime?.Snapshot?.PublicState == null) return;
        var snapshot = runtime.Snapshot;
        var publicState = snapshot.PublicState;
        var revisionChanged =
            renderedRevision != snapshot.StateRevision;
        renderedRevision = snapshot.StateRevision;
        if (revisionChanged) ClearPendingConfirmations();
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
        if (stagingHud != null)
        {
            stagingHud.SetExternalDisplayedSnapshot(
                BuildPlayerProjection(observed, snapshot.OwnerPrivateState),
                !CanMutateFormation(publicState)
                || !string.Equals(
                    observedPlayerId,
                    runtime.LocalPlayerId,
                    StringComparison.Ordinal));
        }

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
        RebuildFormation(publicState);
    }

    public void ShowCommandResult(MatchCommandAckPayload ack)
    {
        if (ack == null) return;
        var accepted = string.Equals(
            ack.ResultCode,
            MatchCommandCode.Accepted.ToString(),
            StringComparison.Ordinal);
        stagingHud?.ShowExternalStatus(
            accepted ? string.Empty : "操作失败：" + ack.StableDetailCode);
    }

    public void ShowStatus(string value)
    {
        stagingHud?.ShowExternalStatus(value);
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
        deployment?.SuspendHudInputForExternalMatch();
        stagingHud.SetStagingDragStartedHandler(HandleStagingSelected);
        stagingHud.SetStagingSelectionChangedHandler(
            HandleStagingSelected);

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

    private void RebuildFormation(PublicMatchStateWire publicState)
    {
        ClearFormation();
        if (!IsPreparation(publicState)) return;
        var observed = publicState.Seats.First(item =>
            string.Equals(
                item.PlayerId,
                observedPlayerId,
                StringComparison.Ordinal));
        foreach (var unit in (observed.Units
                     ?? Array.Empty<MatchUnitWire>())
                 .Where(item =>
                     string.Equals(
                         item.Zone,
                         MatchUnitZone.Deployed.ToString(),
                         StringComparison.Ordinal)
                     && item.HasFormation))
        {
            if (!PreparationUnitViewBuilder.TryCreate(
                    unit.UnitId,
                    unit.TypeId,
                    transform,
                    false,
                    out var instance,
                    out var diagnostic))
            {
                Debug.LogWarning(
                    "[LanMatch][formation.create.failed] " + diagnostic,
                    this);
                continue;
            }
            instance.name = "LanFormation_" + unit.UnitId;
            instance.transform.position =
                BattlefieldWorldProjection.Default.ToWorld(
                    new FixedPosition(
                        unit.FormationX * 100,
                        unit.FormationY * 100),
                    BattleObserverView.Home);
            formationViews.Add(
                unit.UnitId,
                instance.GetComponent<PreparationUnitView>());
        }
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

    private void HandleStagingSelected(string slotId)
    {
        if (string.IsNullOrEmpty(slotId))
        {
            selectedStagingUnitId = null;
            return;
        }
        var publicState = runtime?.Snapshot?.PublicState;
        if (publicState == null
            || !CanMutateFormation(publicState)
            || !string.Equals(
                observedPlayerId,
                runtime.LocalPlayerId,
                StringComparison.Ordinal))
        {
            stagingHud?.ClearStagingSelection();
            return;
        }
        var stack = stagingHud?.DisplayedSnapshot?.StagingSlots
            .FirstOrDefault(item =>
                string.Equals(
                    StagingHudController.BuildSlotId(item),
                    slotId,
                    StringComparison.Ordinal));
        selectedStagingUnitId = stack?.UnitIds.FirstOrDefault();
        selectedDeployedUnitId = null;
        pendingReplacePosition = null;
        pendingReplaceUnitId = null;
    }

    private void HandleWorldInput()
    {
        var publicState = runtime?.Snapshot?.PublicState;
        if (publicState == null
            || !CanMutateFormation(publicState)
            || !string.Equals(
                observedPlayerId,
                runtime.LocalPlayerId,
                StringComparison.Ordinal)
            || EventSystem.current != null
            && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }
        if (Input.GetMouseButtonUp(1)
            && !string.IsNullOrEmpty(selectedDeployedUnitId))
        {
            runtime.SendCommand(new MatchCommandWirePayload
            {
                CommandKind = MatchCommandKind.RetreatUnit.ToString(),
                UnitId = selectedDeployedUnitId
            });
            ClearSelection();
            return;
        }
        if (!Input.GetMouseButtonUp(0) || Camera.main == null) return;
        var hits = Physics.RaycastAll(
                Camera.main.ScreenPointToRay(Input.mousePosition),
                2000f)
            .OrderBy(item => item.distance)
            .ToArray();
        var clickedView = hits
            .Select(hit =>
                hit.collider.GetComponentInParent<PreparationUnitView>())
            .FirstOrDefault(view =>
                view != null
                && formationViews.ContainsKey(view.PlayerUnitId));
        if (clickedView != null
            && string.IsNullOrEmpty(selectedStagingUnitId)
            && string.IsNullOrEmpty(selectedDeployedUnitId))
        {
            selectedDeployedUnitId = clickedView.PlayerUnitId;
            selectedStagingUnitId = null;
            pendingReplacePosition = null;
            formalHud?.SelectObservedPreparationUnitForHud(
                clickedView.PlayerUnitId);
            return;
        }
        if (hits.Length == 0) return;
        var clickedUnit = clickedView == null
            ? null
            : publicState.Seats
                .First(item => string.Equals(
                    item.PlayerId,
                    runtime.LocalPlayerId,
                    StringComparison.Ordinal))
                .Units
                .FirstOrDefault(item => string.Equals(
                    item.UnitId,
                    clickedView.PlayerUnitId,
                    StringComparison.Ordinal));
        var x = clickedUnit != null && clickedUnit.HasFormation
            ? clickedUnit.FormationX
            : Mathf.FloorToInt((hits[0].point.x + 50f) / 100f);
        var y = clickedUnit != null && clickedUnit.HasFormation
            ? clickedUnit.FormationY
            : Mathf.FloorToInt((hits[0].point.z + 50f) / 100f);
        var target = new MatchFormationPosition(x, y);
        if (!target.IsValid) return;
        if (!string.IsNullOrEmpty(selectedDeployedUnitId))
        {
            runtime.SendCommand(new MatchCommandWirePayload
            {
                CommandKind =
                    MatchCommandKind.RelocateOrSwapUnit.ToString(),
                UnitId = selectedDeployedUnitId,
                TargetX = x,
                TargetY = y
            });
            ClearSelection();
            return;
        }
        if (string.IsNullOrEmpty(selectedStagingUnitId)) return;
        var local = publicState.Seats.First(item =>
            string.Equals(
                item.PlayerId,
                runtime.LocalPlayerId,
                StringComparison.Ordinal));
        var occupant = (local.Units ?? Array.Empty<MatchUnitWire>())
            .FirstOrDefault(item =>
                item.HasFormation
                && item.FormationX == x
                && item.FormationY == y
                && string.Equals(
                    item.Zone,
                    MatchUnitZone.Deployed.ToString(),
                    StringComparison.Ordinal));
        if (occupant != null)
        {
            if (!pendingReplacePosition.HasValue
                || pendingReplacePosition.Value.X != x
                || pendingReplacePosition.Value.Y != y
                || !string.Equals(
                    pendingReplaceUnitId,
                    occupant.UnitId,
                    StringComparison.Ordinal))
            {
                pendingReplacePosition = target;
                pendingReplaceUnitId = occupant.UnitId;
                return;
            }
            runtime.SendCommand(new MatchCommandWirePayload
            {
                CommandKind =
                    MatchCommandKind.ReplaceDeployedUnit.ToString(),
                StagingUnitId = selectedStagingUnitId,
                ExpectedDeployedUnitId = occupant.UnitId,
                TargetX = x,
                TargetY = y
            });
        }
        else
        {
            runtime.SendCommand(new MatchCommandWirePayload
            {
                CommandKind = MatchCommandKind.DeployUnit.ToString(),
                UnitId = selectedStagingUnitId,
                TargetX = x,
                TargetY = y,
                ExpectedAvailableCost =
                    runtime.Snapshot.OwnerPrivateState
                        .AvailableDeploymentCost
            });
        }
        ClearSelection();
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

    private void ConfirmPurchase(MatchShopOfferWire offer)
    {
        if (offer == null || string.IsNullOrEmpty(offer.UnitId)) return;
        if (pendingPurchaseSlot != offer.SlotIndex
            || !string.Equals(
                pendingPurchaseUnitId,
                offer.UnitId,
                StringComparison.Ordinal))
        {
            ClearPendingConfirmations();
            pendingPurchaseSlot = offer.SlotIndex;
            pendingPurchaseUnitId = offer.UnitId;
            return;
        }
        ClearPendingConfirmations();
        SubmitPurchase(offer);
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

    private void ConfirmUpgrade(OwnerMatchStateWire owner)
    {
        if (owner == null) return;
        if (pendingUpgradeLevel != owner.Level
            || pendingUpgradePrice != owner.CurrentUpgradePrice)
        {
            ClearPendingConfirmations();
            pendingUpgradeLevel = owner.Level;
            pendingUpgradePrice = owner.CurrentUpgradePrice;
            return;
        }
        ClearPendingConfirmations();
        SubmitUpgrade(owner);
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

    private void ClearPendingConfirmations()
    {
        pendingPurchaseSlot = -1;
        pendingPurchaseUnitId = null;
        pendingUpgradeLevel = -1;
        pendingUpgradePrice = -1;
    }

    private void ClearSelection()
    {
        selectedStagingUnitId = null;
        selectedDeployedUnitId = null;
        pendingReplaceUnitId = null;
        pendingReplacePosition = null;
        stagingHud?.ClearStagingSelection();
    }

    private void ClearFormation()
    {
        foreach (var view in formationViews.Values)
            if (view != null) Destroy(view.gameObject);
        formationViews.Clear();
    }

    public void DisposeHud()
    {
        ClearFormation();
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
