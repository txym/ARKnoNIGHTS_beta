using System;
using System.Collections.Generic;
using System.Linq;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using ArknoNights.Deployment;
using ArknoNights.Lobby;
using ArknoNights.Match;
using ArknoNights.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class LanMatchHudController : MonoBehaviour
{
    private static readonly Color PanelColor = new Color(.06f, .08f, .1f, .92f);
    private static readonly Color ButtonColor = new Color(.18f, .22f, .25f, .96f);
    private static readonly Color AccentColor = new Color(.92f, .58f, .16f, 1f);

    private LanMatchRuntimeController runtime;
    private UnitCatalog unitCatalog;
    private MatchShopCatalog shopCatalog;
    private Canvas offlineCanvas;
    private Canvas canvas;
    private RectTransform playerRows;
    private RectTransform shopRows;
    private RectTransform stagingRows;
    private Text statusText;
    private Text ownerText;
    private Text commandText;
    private Text observedText;
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

    public void Initialize(
        LanMatchRuntimeController source,
        UnitCatalog units,
        MatchShopCatalog shop)
    {
        runtime = source ?? throw new ArgumentNullException(nameof(source));
        unitCatalog = units ?? throw new ArgumentNullException(nameof(units));
        shopCatalog = shop ?? throw new ArgumentNullException(nameof(shop));
        observedPlayerId = runtime.LocalPlayerId;
        SuppressOfflineHud();
        BuildCanvas();
        Refresh();
    }

    public bool TryObservePlayer(string playerId)
    {
        if (runtime?.Snapshot?.PublicState?.Seats == null
            || !runtime.Snapshot.PublicState.Seats.Any(item =>
                item != null
                && string.Equals(item.PlayerId, playerId, StringComparison.Ordinal)))
        {
            return false;
        }
        observedPlayerId = playerId;
        ClearSelection();
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
        if (runtime?.Snapshot?.PublicState == null
            || playerRows == null)
        {
            return;
        }
        var revisionChanged =
            renderedRevision != runtime.Snapshot.StateRevision;
        renderedRevision = runtime.Snapshot.StateRevision;
        if (revisionChanged) ClearPendingConfirmations();
        renderedCountdown = Mathf.CeilToInt(
            runtime.PreparationRemainingMilliseconds / 1000f);
        var publicState = runtime.Snapshot.PublicState;
        var localSeat = publicState.Seats.FirstOrDefault(item =>
            item != null
            && string.Equals(item.PlayerId, runtime.LocalPlayerId, StringComparison.Ordinal));
        if (localSeat == null) return;
        if (!publicState.Seats.Any(item =>
                item != null
                && string.Equals(item.PlayerId, observedPlayerId, StringComparison.Ordinal)))
        {
            observedPlayerId = runtime.LocalPlayerId;
        }
        if (localSeat.Ready
            || localSeat.Eliminated
            || !string.Equals(
                publicState.Phase,
                MatchPhase.Preparation.ToString(),
                StringComparison.Ordinal))
        {
            ClearSelection();
        }

        RebuildPlayers(publicState.Seats);
        RebuildShop(runtime.Snapshot.OwnerPrivateState, localSeat);
        RebuildStaging(publicState);
        RebuildFormation(publicState);
        UpdateStatus(publicState, localSeat);
    }

    public void ShowCommandResult(MatchCommandAckPayload ack)
    {
        if (commandText == null || ack == null) return;
        commandText.text = string.Equals(
            ack.ResultCode,
            MatchCommandCode.Accepted.ToString(),
            StringComparison.Ordinal)
            ? string.Empty
            : "Rejected: " + ack.StableDetailCode;
    }

    public void ShowStatus(string value)
    {
        if (commandText != null) commandText.text = value ?? string.Empty;
    }

    private void SuppressOfflineHud()
    {
        var staging = FindObjectOfType<StagingHudController>();
        if (staging == null) return;
        offlineCanvas = staging.GetComponentInChildren<Canvas>(true);
        if (offlineCanvas != null)
            offlineCanvas.gameObject.SetActive(false);
        var scene = staging.GetComponent<BattleHudSceneCoordinator>();
        if (scene != null) scene.enabled = false;
        var deployment = staging.GetComponent<StateDrivenDeploymentController>();
        if (deployment != null) deployment.SetInteractionEnabled(false);
        var formal = staging.GetComponent<FormalBattleHudController>();
        if (formal != null) formal.enabled = false;
    }

    private void BuildCanvas()
    {
        if (EventSystem.current == null)
            new GameObject(
                "LanMatchEventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
        var canvasObject = new GameObject(
            "LanMatchHudCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        var root = canvasObject.transform as RectTransform;
        Stretch(root);
        playerRows = Panel("Players", root, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(18f, -18f), new Vector2(250f, 650f), new Vector2(0f, 1f));
        shopRows = Panel("OwnerShop", root, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-18f, -18f), new Vector2(360f, 720f), new Vector2(1f, 1f));
        stagingRows = Panel("ObservedStaging", root, new Vector2(.5f, 0f), new Vector2(.5f, 0f),
            new Vector2(0f, 18f), new Vector2(1160f, 170f), new Vector2(.5f, 0f));
        stagingRows.GetComponent<VerticalLayoutGroup>().enabled = false;
        statusText = Label("Status", root, 30, TextAnchor.UpperCenter, Color.white);
        SetRect(statusText.rectTransform, new Vector2(.5f, 1f), new Vector2(.5f, 1f),
            new Vector2(0f, -18f), new Vector2(900f, 92f), new Vector2(.5f, 1f));
        commandText = Label("CommandStatus", root, 20, TextAnchor.MiddleCenter,
            new Color(1f, .75f, .35f));
        SetRect(commandText.rectTransform, new Vector2(.5f, 0f), new Vector2(.5f, 0f),
            new Vector2(0f, 194f), new Vector2(900f, 40f), new Vector2(.5f, 0f));
    }

    private void RebuildPlayers(IEnumerable<PublicMatchSeatWire> seats)
    {
        ClearChildren(playerRows);
        var title = Label("Title", playerRows, 22, TextAnchor.MiddleLeft, AccentColor);
        title.text = "PLAYERS";
        AddVertical(title.rectTransform, 44f);
        foreach (var seat in seats.Where(item => item != null).OrderBy(item => item.SeatIndex))
        {
            var button = UiButton(
                "Player_" + seat.PlayerId,
                playerRows,
                seat.DisplayName
                + "\nLife "
                + seat.Life
                + "  " + seat.ConnectionState.ToUpperInvariant()
                + (seat.Eliminated ? "  #" + seat.Placement : string.Empty)
                + (seat.Ready ? "  READY" : string.Empty),
                () => runtime.TryObservePlayer(seat.PlayerId));
            AddVertical(button.GetComponent<RectTransform>(), 92f);
            var image = button.GetComponent<Image>();
            image.color = string.Equals(seat.PlayerId, observedPlayerId, StringComparison.Ordinal)
                ? AccentColor
                : ButtonColor;
            var avatar = new GameObject(
                "Avatar",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image)).GetComponent<Image>();
            avatar.transform.SetParent(button.transform, false);
            avatar.sprite = Resources.Load<Sprite>(
                "Home/" + AvatarSpriteName(seat.AvatarId));
            avatar.preserveAspect = true;
            SetRect(avatar.rectTransform, new Vector2(0f, .5f), new Vector2(0f, .5f),
                new Vector2(10f, 0f), new Vector2(68f, 68f), new Vector2(0f, .5f));
            avatar.raycastTarget = false;
            var label = button.GetComponentInChildren<Text>();
            label.rectTransform.offsetMin = new Vector2(84f, 4f);
        }
    }

    private void RebuildShop(
        OwnerMatchStateWire owner,
        PublicMatchSeatWire localSeat)
    {
        ClearChildren(shopRows);
        ownerText = Label("OwnerSummary", shopRows, 22, TextAnchor.MiddleLeft, Color.white);
        ownerText.text = owner == null
            ? "SPECTATING"
            : "Gold " + owner.Gold
              + "   Lv." + owner.Level
              + "\nCost " + owner.AvailableDeploymentCost
              + "/" + owner.TotalDeploymentCost;
        AddVertical(ownerText.rectTransform, 72f);
        var preparation = string.Equals(
            runtime.Snapshot.PublicState.Phase,
            MatchPhase.Preparation.ToString(),
            StringComparison.Ordinal);
        var battle = string.Equals(
            runtime.Snapshot.PublicState.Phase,
            MatchPhase.Battle.ToString(),
            StringComparison.Ordinal);
        var canShop = runtime.CanSendCommands && (preparation || battle);
        foreach (var offer in (owner?.ShopOffers ?? Array.Empty<MatchShopOfferWire>())
                     .OrderBy(item => item.SlotIndex))
        {
            var captured = offer;
            var display = string.IsNullOrEmpty(offer.UnitId)
                ? "Empty"
                : UnitName(offer.TypeId)
                  + "  ★" + offer.Rarity
                  + (offer.IsFrozen ? "  [Frozen]" : string.Empty);
            var button = UiButton(
                "Shop_" + offer.SlotIndex,
                shopRows,
                display,
                () => ConfirmPurchase(captured));
            button.interactable = canShop && !string.IsNullOrEmpty(offer.UnitId);
            AddVertical(button.GetComponent<RectTransform>(), 62f);
        }
        var refresh = UiButton("Refresh", shopRows, "Refresh", () =>
            runtime.SendCommand(new MatchCommandWirePayload
            {
                CommandKind = MatchCommandKind.RefreshShop.ToString()
            }));
        refresh.interactable = canShop;
        AddVertical(refresh.GetComponent<RectTransform>(), 50f);
        var freeze = UiButton("Freeze", shopRows, "Toggle Freeze", () =>
            runtime.SendCommand(new MatchCommandWirePayload
            {
                CommandKind = MatchCommandKind.ToggleShopFreeze.ToString()
            }));
        freeze.interactable = canShop;
        AddVertical(freeze.GetComponent<RectTransform>(), 50f);
        var upgrade = UiButton(
            "Upgrade",
            shopRows,
            owner == null ? "Upgrade" : "Upgrade (" + owner.CurrentUpgradePrice + ")",
            () => ConfirmUpgrade(owner));
        upgrade.interactable = canShop && owner != null;
        AddVertical(upgrade.GetComponent<RectTransform>(), 50f);
        var ready = UiButton(
            "Ready",
            shopRows,
            localSeat.Ready ? "Cancel Ready" : "Ready",
            () => runtime.SendCommand(new MatchCommandWirePayload
            {
                CommandKind = MatchCommandKind.SetPreparationReady.ToString(),
                DesiredReady = !localSeat.Ready
            }));
        ready.gameObject.SetActive(preparation);
        ready.interactable = runtime.CanSendCommands && preparation;
        AddVertical(ready.GetComponent<RectTransform>(), 54f);
        var quit = UiButton("Quit", shopRows, "Return to Main Menu",
            runtime.RequestExplicitQuit);
        AddVertical(quit.GetComponent<RectTransform>(), 48f);
    }

    private void RebuildStaging(PublicMatchStateWire publicState)
    {
        ClearChildren(stagingRows);
        var preparation = string.Equals(
            publicState.Phase,
            MatchPhase.Preparation.ToString(),
            StringComparison.Ordinal);
        stagingRows.gameObject.SetActive(preparation);
        if (!preparation) return;
        var observed = publicState.Seats.First(item =>
            string.Equals(item.PlayerId, observedPlayerId, StringComparison.Ordinal));
        observedText = Label("Observed", stagingRows, 20, TextAnchor.MiddleLeft, AccentColor);
        observedText.text = "Observed: " + observed.DisplayName;
        SetRect(observedText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(18f, -8f), new Vector2(320f, 34f), new Vector2(0f, 1f));
        var staging = (observed.Units ?? Array.Empty<MatchUnitWire>())
            .Where(item => string.Equals(
                item.Zone,
                MatchUnitZone.Staging.ToString(),
                StringComparison.Ordinal))
            .GroupBy(
                item => StagingBuffKey(observed, item),
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
                    Units = group.ToArray(),
                    First = first,
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
            .OrderBy(group => group.Cost)
            .ThenBy(group => group.NumericTypeId)
            .ThenBy(group => group.First.EliteLevel)
            .ThenBy(group => group.BuffKey, StringComparer.Ordinal)
            .ThenBy(group => group.First.UnitId, StringComparer.Ordinal)
            .ToArray();
        var x = 18f;
        foreach (var stack in staging)
        {
            var chosen = stack.First;
            var captured = chosen.UnitId;
            var button = UiButton(
                "Staging_" + captured,
                stagingRows,
                UnitName(chosen.TypeId)
                + "\nE" + chosen.EliteLevel
                + " ×" + stack.Units.Length,
                () =>
                {
                    if (!CanMutateFormation(publicState)) return;
                    selectedStagingUnitId = captured;
                    selectedDeployedUnitId = null;
                    pendingReplacePosition = null;
                    commandText.text = "Select a deployment cell.";
                });
            button.interactable = CanMutateFormation(publicState)
                && string.Equals(observedPlayerId, runtime.LocalPlayerId, StringComparison.Ordinal);
            var rect = button.GetComponent<RectTransform>();
            SetRect(rect, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(x, 18f), new Vector2(150f, 96f), Vector2.zero);
            x += 158f;
        }
    }

    private void RebuildFormation(PublicMatchStateWire publicState)
    {
        ClearFormation();
        if (!string.Equals(
                publicState.Phase,
                MatchPhase.Preparation.ToString(),
                StringComparison.Ordinal))
        {
            return;
        }
        var observed = publicState.Seats.First(item =>
            string.Equals(item.PlayerId, observedPlayerId, StringComparison.Ordinal));
        foreach (var unit in (observed.Units ?? Array.Empty<MatchUnitWire>())
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
            instance.transform.position = BattlefieldWorldProjection.Default.ToWorld(
                new FixedPosition(unit.FormationX * 100, unit.FormationY * 100),
                BattleObserverView.Home);
            formationViews.Add(
                unit.UnitId,
                instance.GetComponent<PreparationUnitView>());
        }
    }

    private void UpdateStatus(
        PublicMatchStateWire publicState,
        PublicMatchSeatWire localSeat)
    {
        if (runtime.IsReconnecting)
        {
            statusText.text = "RECONNECTING — read only";
            return;
        }
        if (string.Equals(
                publicState.Phase,
                MatchPhase.Preparation.ToString(),
                StringComparison.Ordinal))
        {
            statusText.text = "ROUND "
                + publicState.RoundNumber
                + "  PREPARATION  "
                + Mathf.Max(0, renderedCountdown)
                + "s"
                + (localSeat.Ready ? "  READY" : string.Empty);
            return;
        }
        if (string.Equals(
                publicState.Phase,
                MatchPhase.Battle.ToString(),
                StringComparison.Ordinal))
        {
            if (runtime.HasBattleFailure)
            {
                statusText.text = "ROUND "
                    + publicState.RoundNumber
                    + "  BATTLE  SYNCHRONIZATION ERROR";
                return;
            }
            statusText.text = "ROUND "
                + publicState.RoundNumber
                + "  BATTLE  TICK "
                + runtime.PresentationTick
                + (runtime.BattleState == ArknoNights.Battle.Demo.MultiBattlePresentationState.Buffering
                    ? "  SYNCING"
                    : string.Empty);
            return;
        }
        statusText.text = publicState.Phase;
    }

    private void HandleWorldInput()
    {
        var publicState = runtime?.Snapshot?.PublicState;
        if (publicState == null
            || !CanMutateFormation(publicState)
            || !string.Equals(observedPlayerId, runtime.LocalPlayerId, StringComparison.Ordinal)
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
            commandText.text = "Select a destination; right-click retreats.";
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
                CommandKind = MatchCommandKind.RelocateOrSwapUnit.ToString(),
                UnitId = selectedDeployedUnitId,
                TargetX = x,
                TargetY = y
            });
            ClearSelection();
            return;
        }
        if (string.IsNullOrEmpty(selectedStagingUnitId)) return;
        var local = publicState.Seats.First(item =>
            string.Equals(item.PlayerId, runtime.LocalPlayerId, StringComparison.Ordinal));
        var occupant = (local.Units ?? Array.Empty<MatchUnitWire>()).FirstOrDefault(item =>
            item.HasFormation
            && item.FormationX == x
            && item.FormationY == y
            && string.Equals(item.Zone, MatchUnitZone.Deployed.ToString(), StringComparison.Ordinal));
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
                commandText.text = "Click the occupied cell again to replace.";
                return;
            }
            runtime.SendCommand(new MatchCommandWirePayload
            {
                CommandKind = MatchCommandKind.ReplaceDeployedUnit.ToString(),
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
                    runtime.Snapshot.OwnerPrivateState.AvailableDeploymentCost
            });
        }
        ClearSelection();
    }

    private bool CanMutateFormation(PublicMatchStateWire publicState)
    {
        if (!runtime.CanSendCommands
            || !string.Equals(
                publicState.Phase,
                MatchPhase.Preparation.ToString(),
                StringComparison.Ordinal))
        {
            return false;
        }
        var local = publicState.Seats.FirstOrDefault(item =>
            item != null
            && string.Equals(item.PlayerId, runtime.LocalPlayerId, StringComparison.Ordinal));
        return local != null && !local.Ready && !local.Eliminated;
    }

    private string UnitName(string typeId)
    {
        return unitCatalog.TryGet(typeId, out var entry)
            && !string.IsNullOrWhiteSpace(entry.DisplayNameZhHans)
            ? entry.DisplayNameZhHans
            : typeId;
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
            commandText.text = "Click the same offer again to confirm purchase.";
            return;
        }
        ClearPendingConfirmations();
        runtime.SendCommand(new MatchCommandWirePayload
        {
            CommandKind = MatchCommandKind.PurchaseShopOffer.ToString(),
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
            commandText.text = "Click Upgrade again to confirm.";
            return;
        }
        ClearPendingConfirmations();
        runtime.SendCommand(new MatchCommandWirePayload
        {
            CommandKind = MatchCommandKind.PurchaseLevelUpgrade.ToString(),
            ExpectedCurrentLevel = owner.Level,
            ExpectedCurrentPrice = owner.CurrentUpgradePrice
        });
    }

    private static string StagingBuffKey(
        PublicMatchSeatWire seat,
        MatchUnitWire unit)
    {
        var inline = string.Join(
            "\u001e",
            (unit.Buffs ?? Array.Empty<MatchBuffWire>())
                .OrderBy(item => item.BuffId, StringComparer.Ordinal)
                .ThenBy(item => item.CanonicalPayload, StringComparer.Ordinal)
                .Select(item =>
                    item.BuffId + "\u001f" + item.CanonicalPayload));
        var targeted = string.Join(
            "\u001e",
            (seat.TargetedUnitBuffs ?? Array.Empty<MatchTargetedBuffWire>())
                .Where(item => string.Equals(
                    item.TargetUnitId,
                    unit.UnitId,
                    StringComparison.Ordinal))
                .OrderBy(item => item.BuffInstanceId, StringComparer.Ordinal)
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
        if (offlineCanvas != null)
            offlineCanvas.gameObject.SetActive(true);
        if (canvas != null)
            Destroy(canvas.gameObject);
        runtime = null;
        unitCatalog = null;
        shopCatalog = null;
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

    private static RectTransform Panel(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 position,
        Vector2 size,
        Vector2 pivot)
    {
        var value = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(VerticalLayoutGroup));
        value.transform.SetParent(parent, false);
        var image = value.GetComponent<Image>();
        image.color = PanelColor;
        var layout = value.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8f;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        var rect = value.GetComponent<RectTransform>();
        SetRect(rect, anchorMin, anchorMax, position, size, pivot);
        return rect;
    }

    private static Button UiButton(
        string name,
        Transform parent,
        string label,
        Action clicked)
    {
        var value = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        value.transform.SetParent(parent, false);
        var image = value.GetComponent<Image>();
        image.color = ButtonColor;
        var button = value.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => clicked());
        var text = Label("Label", value.transform, 19, TextAnchor.MiddleCenter, Color.white);
        Stretch(text.rectTransform);
        text.text = label;
        return button;
    }

    private static Text Label(
        string name,
        Transform parent,
        int fontSize,
        TextAnchor anchor,
        Color color)
    {
        var value = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Text));
        value.transform.SetParent(parent, false);
        var text = value.GetComponent<Text>();
        text.font = StagingHudController.FormalUiFont;
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static void AddVertical(RectTransform rect, float height)
    {
        var layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
    }

    private static void ClearChildren(Transform root)
    {
        if (root == null) return;
        foreach (Transform child in root)
            Destroy(child.gameObject);
    }

    private static void Stretch(RectTransform target)
    {
        target.anchorMin = Vector2.zero;
        target.anchorMax = Vector2.one;
        target.pivot = new Vector2(.5f, .5f);
        target.offsetMin = Vector2.zero;
        target.offsetMax = Vector2.zero;
    }

    private static void SetRect(
        RectTransform target,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 position,
        Vector2 size,
        Vector2 pivot)
    {
        target.anchorMin = anchorMin;
        target.anchorMax = anchorMax;
        target.pivot = pivot;
        target.anchoredPosition = position;
        target.sizeDelta = size;
    }
}
