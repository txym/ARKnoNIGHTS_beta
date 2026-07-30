using System;
using System.Collections.Generic;
using System.Linq;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Player;
using ArknoNights.UI;
using Spine.Unity;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ArknoNights.Deployment
{
    /// <summary>Runtime-only scene bridge so the UI assembly never depends on Assembly-CSharp.</summary>
    internal static class PreparationDeploymentBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ScheduleAttachToFormalHud()
        {
            new GameObject("PreparationDeploymentBootstrap").AddComponent<PreparationDeploymentBootstrapRunner>();
        }
    }

    internal sealed class PreparationDeploymentBootstrapRunner : MonoBehaviour
    {
        private void Start()
        {
            var hud = UnityEngine.Object.FindObjectOfType<StagingHudController>();
            if (hud != null && hud.InitializationSucceeded)
            {
                var controller = hud.GetComponent<StateDrivenDeploymentController>() ?? hud.gameObject.AddComponent<StateDrivenDeploymentController>();
                controller.Initialize(hud);
            }
            Destroy(gameObject);
        }
    }

    public enum PreparationInteractionState
    {
        Idle,
        Dragging,
        SelectedDeployed,
        Disabled
    }

    public enum PreparationDragSource
    {
        Staging,
        DeployedRelocation
    }

    /// <summary>Explicit, one-based projection for the preparation map. It is a visual/input mapping only.</summary>
    public static class PreparationGridProjection
    {
        public const float CellWorldSize = 100f;
        public const float UnitWorldY = 0f;

        public static Vector3 ToWorld(LocalFormationCoordinate coordinate) => new Vector3(coordinate.X * CellWorldSize, UnitWorldY, coordinate.Y * CellWorldSize);

        public static bool TryWorldToCoordinate(Vector3 worldPosition, out LocalFormationCoordinate coordinate)
        {
            var x = Mathf.FloorToInt((worldPosition.x + CellWorldSize * 0.5f) / CellWorldSize);
            var y = Mathf.FloorToInt((worldPosition.z + CellWorldSize * 0.5f) / CellWorldSize);
            return LocalFormationCoordinate.TryCreate(x, y, out coordinate);
        }
    }

    /// <summary>Temporary drag data. It intentionally holds no PlayerState reference and cannot mutate it before commit.</summary>
    public sealed class PreparationDragSession
    {
        public PreparationDragSession(string unitId, string typeId, PreparationDragSource source, LocalFormationCoordinate? origin)
        {
            UnitId = unitId ?? string.Empty;
            TypeId = typeId ?? string.Empty;
            Source = source;
            Origin = origin;
        }

        public string UnitId { get; }
        public string TypeId { get; }
        public PreparationDragSource Source { get; }
        public LocalFormationCoordinate? Origin { get; }
        public LocalFormationCoordinate? Candidate { get; private set; }

        public void SetCandidate(Vector3 worldPosition)
        {
            Candidate = PreparationGridProjection.TryWorldToCoordinate(worldPosition, out var coordinate) ? coordinate : (LocalFormationCoordinate?)null;
        }
    }

    /// <summary>
    /// UI-003 interaction owner. UI and world rays supply candidate input only; all deployed/staging state changes
    /// are committed by PlayerState.TryDeploy/TryRetreat and subsequently projected by the view coordinator.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StateDrivenDeploymentController : MonoBehaviour
    {
        private const string AtlasPath = "UI/Texture/SpriteAtlasTexture-UI_BATTLE (Group 0)-2048x2048-fmt34_Merged";

        [SerializeField] private Camera worldCamera;
        [SerializeField] private bool interactionEnabled = true;

        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private StagingHudController hud;
        private PreparationUnitViewCoordinator viewCoordinator;
        private PreparationDragSession dragSession;
        private GameObject dragPreview;
        private PreparationUnitView relocatingView;
        private PreparationUnitView pendingRelocationView;
        private Vector3 pendingRelocationScreenPosition;
        private PreparationSelectionIndicator selectionIndicator;
        private string selectedUnitId;
        private bool submittedCurrentDrag;
        private bool initialized;
        private bool externalMode;
        private PlayerStateSnapshot externalSnapshot;
        private Action<string, int, int> externalDeploy;
        private Action<string, string, int, int> externalReplace;
        private Action<string, int, int> externalRelocate;
        private Action<string> externalRetreat;
        private readonly HashSet<string> pendingExternalUnitIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameObject>
            pendingExternalDeployViews =
                new Dictionary<string, GameObject>(
                    StringComparer.Ordinal);

        public PreparationInteractionState State { get; private set; } = PreparationInteractionState.Idle;
        public bool InteractionEnabled => interactionEnabled;
        public string SelectedUnitId => selectedUnitId;
        public int PreparationViewCount => viewCoordinator == null ? 0 : viewCoordinator.ViewCount;
        public bool PreparationViewsVisible =>
            viewCoordinator != null
            && viewCoordinator.gameObject.activeSelf;
        private PlayerStateSnapshot CurrentSnapshot =>
            externalMode
                ? externalSnapshot ?? hud?.DisplayedSnapshot
                : hud?.Snapshot;
        /// <summary>Stable PlayerState unit ID selection notification for read-only HUD consumers.</summary>
        public event Action<string> DeployedSelectionChanged;

        /// <summary>UI-004 keeps preparation projections separate from BattleDemoViews without changing PlayerState.</summary>
        public void SetPreparationViewsVisible(bool visible)
        {
            if (!visible) CancelDrag("preparation.views.hidden", false);
            if (viewCoordinator != null) viewCoordinator.gameObject.SetActive(visible);
        }

        private void Start()
        {
            if (initialized) return;
            var sceneHud = GetComponent<StagingHudController>();
            if (sceneHud != null && sceneHud.InitializationSucceeded) Initialize(sceneHud);
        }

        public void Initialize(StagingHudController sourceHud)
        {
            if (initialized && hud == sourceHud) return;
            ShutdownBindings();
            hud = sourceHud;
            if (hud == null || !hud.InitializationSucceeded)
            {
                State = PreparationInteractionState.Disabled;
                return;
            }

            worldCamera = worldCamera ? worldCamera : Camera.main;
            foreach (var sprite in Resources.LoadAll<Sprite>(AtlasPath))
                if (sprite != null && !string.IsNullOrEmpty(sprite.name)) sprites[sprite.name] = sprite;

            var root = new GameObject("PreparationUnitViews");
            root.transform.SetParent(transform, false);
            viewCoordinator = root.AddComponent<PreparationUnitViewCoordinator>();
            viewCoordinator.Initialize(hud.PlayerState);
            hud.SetStagingDragStartedHandler(slotId => BeginDragFromSlot(slotId));
            hud.SetStagingSelectionChangedHandler(HandleStagingSelectionChanged);
            initialized = true;
            State = interactionEnabled ? PreparationInteractionState.Idle : PreparationInteractionState.Disabled;
        }

        public void InitializeExternal(
            StagingHudController sourceHud,
            Action<string, int, int> deploy,
            Action<string, string, int, int> replace,
            Action<string, int, int> relocate,
            Action<string> retreat)
        {
            Initialize(sourceHud);
            if (!initialized) return;
            externalMode = true;
            externalDeploy = deploy;
            externalReplace = replace;
            externalRelocate = relocate;
            externalRetreat = retreat;
            externalSnapshot = sourceHud.DisplayedSnapshot;
            viewCoordinator.InitializeExternal(
                externalSnapshot);
            enabled = true;
            SetPreparationViewsVisible(true);
            SetInteractionEnabled(true);
        }

        public void ApplyExternalSnapshot(
            PlayerStateSnapshot projection,
            bool canInteract)
        {
            if (!externalMode || projection == null) return;
            externalSnapshot = projection;
            viewCoordinator?.ReconcileExternal(
                projection,
                pendingExternalUnitIds);
            SetInteractionEnabled(canInteract);
        }

        public void ResolveExternalOperation(
            string unitId)
        {
            if (!externalMode
                || string.IsNullOrWhiteSpace(unitId))
            {
                return;
            }
            pendingExternalUnitIds.Remove(unitId);
            if (pendingExternalDeployViews.TryGetValue(
                    unitId,
                    out var pendingView))
            {
                pendingExternalDeployViews.Remove(unitId);
                if (pendingView != null)
                    Destroy(pendingView);
            }
            if (externalSnapshot != null)
            {
                viewCoordinator?.ReconcileExternal(
                    externalSnapshot,
                    pendingExternalUnitIds);
                if (string.Equals(
                        selectedUnitId,
                        unitId,
                        StringComparison.Ordinal)
                    && !externalSnapshot.Units.Any(unit =>
                        string.Equals(
                            unit.UnitId,
                            unitId,
                            StringComparison.Ordinal)
                        && unit.Zone
                            == PlayerUnitZone.Deployed))
                {
                    ClearSelection();
                }
            }
        }

        public void ClearExternalPendingOperations()
        {
            if (!externalMode) return;
            CancelDrag("external.recovery", false);
            foreach (var pending in
                     pendingExternalDeployViews.Values)
            {
                if (pending != null) Destroy(pending);
            }
            pendingExternalDeployViews.Clear();
            pendingExternalUnitIds.Clear();
            ClearSelection();
            if (externalSnapshot != null)
            {
                viewCoordinator?.ReconcileExternal(
                    externalSnapshot,
                    pendingExternalUnitIds);
            }
        }

        private void Update()
        {
            if (!initialized || !interactionEnabled) return;
            if (State == PreparationInteractionState.Dragging)
            {
                UpdateDragPreview(Input.mousePosition);
                if (Input.GetMouseButtonUp(0))
                {
                    if (IsPointerOverUi()) CancelDrag("input.release.over-ui");
                    else CommitCurrentDrag();
                }
                if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) CancelDrag("input.cancelled");
                return;
            }

            if (State == PreparationInteractionState.Idle
                || State
                    == PreparationInteractionState
                        .SelectedDeployed)
            {
                if (Input.GetMouseButtonDown(0) && !IsPointerOverUi())
                {
                    pendingRelocationView =
                        FindDeployedViewAtScreen(
                            Input.mousePosition);
                    pendingRelocationScreenPosition = Input.mousePosition;
                }
                if (pendingRelocationView != null && Input.GetMouseButton(0) &&
                    (Input.mousePosition - pendingRelocationScreenPosition).sqrMagnitude >= GetDragThresholdSquared())
                {
                    var view = pendingRelocationView;
                    pendingRelocationView = null;
                    if (BeginRelocateSelected(view)) UpdateDragPreview(Input.mousePosition);
                    return;
                }
                if (Input.GetMouseButtonUp(0))
                {
                    pendingRelocationView = null;
                    if (!IsPointerOverUi()) ProcessWorldClick(Input.mousePosition);
                }
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) CancelDrag("application.focus.lost");
        }

        private void OnDisable()
        {
            CancelDrag("controller.disabled", false);
            ClearSelection();
        }

        private void OnDestroy()
        {
            ShutdownBindings();
        }

        /// <summary>UI-004 phase boundary. Disabling always tears down transient preview and selection input.</summary>
        public void SetInteractionEnabled(bool enabled)
        {
            if (interactionEnabled == enabled)
            {
                if (enabled
                    && initialized
                    && State == PreparationInteractionState.Disabled)
                {
                    State = PreparationInteractionState.Idle;
                }
                return;
            }
            interactionEnabled = enabled;
            if (!enabled)
            {
                CancelDrag("interaction.locked", false);
                ClearSelection();
                State = PreparationInteractionState.Disabled;
                return;
            }
            State = initialized ? PreparationInteractionState.Idle : PreparationInteractionState.Disabled;
        }

        public void SuspendHudInputForExternalMatch()
        {
            SetInteractionEnabled(false);
            SetPreparationViewsVisible(false);
            if (hud != null)
            {
                hud.SetStagingDragStartedHandler(null);
                hud.SetStagingSelectionChangedHandler(null);
            }
            enabled = false;
        }

        public void RestoreHudInputAfterExternalMatch()
        {
            if (externalMode)
            {
                var sourceHud = hud;
                ShutdownBindings();
                Initialize(sourceHud);
                return;
            }
            enabled = true;
            if (hud == null) return;
            hud.SetStagingDragStartedHandler(slotId => BeginDragFromSlot(slotId));
            hud.SetStagingSelectionChangedHandler(HandleStagingSelectionChanged);
            SetPreparationViewsVisible(true);
            SetInteractionEnabled(true);
        }

        /// <summary>Public for UI events and PlayMode smoke tests. It selects the first stable unit ID in the stack.</summary>
        public bool BeginDragFromSlot(string slotId)
        {
            if (!initialized || !interactionEnabled || State == PreparationInteractionState.Dragging) return false;
            var current = CurrentSnapshot;
            var stack = current?.StagingSlots.FirstOrDefault(slot => string.Equals(StagingHudController.BuildSlotId(slot), slotId, StringComparison.Ordinal));
            if (stack == null || stack.UnitIds.Count == 0) return false;
            var unitId = stack.UnitIds[0];
            var unit = current.Units.FirstOrDefault(item => string.Equals(item.UnitId, unitId, StringComparison.Ordinal));
            if (unit == null
                || unit.Zone != PlayerUnitZone.Staging
                || pendingExternalUnitIds.Contains(unitId))
            {
                return false;
            }

            ClearSelection();
            hud.ClearStagingSelection();
            dragSession = new PreparationDragSession(unit.UnitId, unit.TypeId, PreparationDragSource.Staging, null);
            if (!PreparationUnitViewBuilder.TryCreate(unit.UnitId, unit.TypeId, unit.EliteLevel, transform, true, out dragPreview, out var diagnostic))
            {
                Debug.LogError("[PreparationDeployment][drag.preview.create.failed] unit=" + unit.UnitId + "; type=" + unit.TypeId + "; code=" + diagnostic, this);
                dragSession = null;
                return false;
            }

            submittedCurrentDrag = false;
            State = PreparationInteractionState.Dragging;
            UpdateDragPreview(Input.mousePosition);
            Debug.Log("[PreparationDeployment][drag.started] unit=" + unit.UnitId + "; type=" + unit.TypeId + "; slot=" + slotId, this);
            return true;
        }

        public void SetDragWorldPositionForTests(Vector3 position)
        {
            if (State != PreparationInteractionState.Dragging || dragSession == null) return;
            UpdateDragPreview(position, true);
        }

        public PlayerOperationResult CommitCurrentDragForTests() => CommitCurrentDrag();

        public PlayerOperationResult ReleaseCurrentDragForTests(bool pointerOverUi)
        {
            if (State != PreparationInteractionState.Dragging || dragSession == null) return null;
            if (pointerOverUi)
            {
                CancelDrag("input.release.over-ui");
                return null;
            }
            return CommitCurrentDrag();
        }

        public bool SelectDeployedForTests(string unitId)
        {
            if (viewCoordinator == null || !viewCoordinator.TryGetView(unitId, out var view)) return false;
            SelectView(view);
            return selectedUnitId != null;
        }

        public bool BeginRelocateSelectedForTests(string unitId)
        {
            if (viewCoordinator == null || !viewCoordinator.TryGetView(unitId, out var view)) return false;
            return BeginRelocateSelected(view);
        }

        public PlayerOperationResult RetreatSelectedForTests() => RetreatSelected();

        private void UpdateDragPreview(Vector3 screenPosition)
        {
            if (!TryGetWorldFromScreen(screenPosition, out var worldPosition))
            {
                // Keep the last visible preview if a camera cannot intersect the board plane for one frame.
                return;
            }
            UpdateDragPreview(worldPosition, true);
        }

        private void UpdateDragPreview(Vector3 worldPosition, bool isWorldPosition)
        {
            if (dragSession == null) return;
            // Preview is deliberately free-following, including the away half and off-board space.
            // The one-based snap only occurs after the release submits the PlayerState command.
            if (dragSession.Source == PreparationDragSource.Staging)
            {
                if (!dragPreview) return;
                dragPreview.SetActive(true);
                dragPreview.transform.position = worldPosition;
                return;
            }
            if (relocatingView != null) relocatingView.transform.position = worldPosition;
        }

        private PlayerOperationResult CommitCurrentDrag()
        {
            if (State != PreparationInteractionState.Dragging || dragSession == null || submittedCurrentDrag) return null;
            submittedCurrentDrag = true;
            var before = CurrentSnapshot;
            if (dragPreview) dragSession.SetCandidate(dragPreview.transform.position);
            else if (relocatingView != null) dragSession.SetCandidate(relocatingView.transform.position);
            var candidate = dragSession.Candidate;
            if (externalMode)
            {
                var submittedUnitId = dragSession.UnitId;
                var submittedSource = dragSession.Source;
                var submittedX = candidate?.X ?? 0;
                var submittedY = candidate?.Y ?? 0;
                var replacedUnit = submittedSource
                        == PreparationDragSource.Staging
                    && candidate.HasValue
                        ? before.Units.FirstOrDefault(unit =>
                            unit.Zone
                                == PlayerUnitZone.Deployed
                            && unit.Formation.HasValue
                            && unit.Formation.Value.Equals(
                                candidate.Value))
                        : null;
                pendingExternalUnitIds.Add(submittedUnitId);
                if (submittedSource == PreparationDragSource.Staging
                    && dragPreview != null)
                {
                    pendingExternalDeployViews[submittedUnitId] =
                        dragPreview;
                    dragPreview = null;
                }
                else if (submittedSource
                    == PreparationDragSource.DeployedRelocation)
                {
                    relocatingView = null;
                }
                if (submittedSource
                    == PreparationDragSource.DeployedRelocation)
                {
                    externalRelocate?.Invoke(
                        submittedUnitId,
                        submittedX,
                        submittedY);
                }
                else
                {
                    if (replacedUnit != null)
                        externalReplace?.Invoke(
                            submittedUnitId,
                            replacedUnit.UnitId,
                            submittedX,
                            submittedY);
                    else
                        externalDeploy?.Invoke(
                            submittedUnitId,
                            submittedX,
                            submittedY);
                }
                CancelDrag("drag.committed");
                return null;
            }
            // Even an off-board release is submitted once to the authoritative command so callers receive
            // the same stable CoordinateOutOfBounds result rather than a presentation-only failure.
            var result = dragSession.Source == PreparationDragSource.DeployedRelocation
                ? (candidate.HasValue
                    ? hud.PlayerState.TryRelocateDeployed(dragSession.UnitId, candidate.Value.X, candidate.Value.Y)
                    : hud.PlayerState.TryRelocateDeployed(dragSession.UnitId, 0, 0))
                : candidate.HasValue
                  && before.Units.FirstOrDefault(unit =>
                      unit.Zone == PlayerUnitZone.Deployed
                      && unit.Formation.HasValue
                      && unit.Formation.Value.Equals(
                          candidate.Value))
                  is PlayerUnitSnapshot occupied
                    ? hud.PlayerState.TryReplaceDeployed(
                        dragSession.UnitId,
                        occupied.UnitId,
                        candidate.Value.X,
                        candidate.Value.Y)
                    : (candidate.HasValue
                        ? hud.PlayerState.TryDeploy(dragSession.UnitId, candidate.Value.X, candidate.Value.Y)
                        : hud.PlayerState.TryDeploy(dragSession.UnitId, 0, 0));

            if (dragSession.Source == PreparationDragSource.DeployedRelocation)
            {
                var isNoOp = result != null && result.Success && result.Snapshot.Version == before.Version;
                var isSwap = candidate.HasValue && before.Units.Any(unit =>
                    unit.Zone == PlayerUnitZone.Deployed && unit.Formation.HasValue && unit.Formation.Value.Equals(candidate.Value));
                if (result != null && result.Success && !isNoOp) relocatingView = null;
                var outcome = result == null ? "invalid" :
                    result.Success ? (isNoOp ? "noop" : (isSwap ? "swap" : "success")) :
                    result.Code == PlayerOperationCode.CoordinateIsGate ? "gate" : "invalid";
                Debug.Log("[PreparationDeployment][relocate." + outcome + "] unit=" + dragSession.UnitId + "; origin=" + (dragSession.Origin.HasValue ? dragSession.Origin.Value.ToString() : "<none>") + "; target=" + (candidate.HasValue ? candidate.Value.ToString() : "<none>") + "; stateChanged=" + (result != null && before.CanonicalSummary != result.Snapshot.CanonicalSummary), this);
            }
            else if (result != null && result.Success)
            {
                Debug.Log("[PreparationDeployment][deploy.success] unit=" + dragSession.UnitId + "; type=" + dragSession.TypeId + "; from=Staging; to=Deployed(" + candidate.Value + "); cost=" + before.DeploymentCost + "->" + result.Snapshot.DeploymentCost + "; slots=" + before.StagingSlots.Count + "->" + result.Snapshot.StagingSlots.Count, this);
            }
            else
            {
                Debug.LogWarning("[PreparationDeployment][deploy.failed] code=" + result.Code + "; unit=" + dragSession.UnitId + "; candidate=" + (candidate.HasValue ? candidate.Value.ToString() : "<none>") + "; stateUnchanged=" + (before.CanonicalSummary == result.Snapshot.CanonicalSummary), this);
            }

            CancelDrag("drag.committed");
            return result;
        }

        private void CancelDrag(string reason, bool restoreSelectionIndicator = true)
        {
            var wasRelocating = dragSession != null && dragSession.Source == PreparationDragSource.DeployedRelocation;
            if (wasRelocating &&
                relocatingView != null && dragSession.Origin.HasValue)
            {
                relocatingView.transform.position = PreparationGridProjection.ToWorld(dragSession.Origin.Value);
            }
            if (dragPreview) Destroy(dragPreview);
            dragPreview = null;
            dragSession = null;
            relocatingView = null;
            pendingRelocationView = null;
            submittedCurrentDrag = false;
            if (wasRelocating && !string.Equals(reason, "drag.committed", StringComparison.Ordinal))
                Debug.Log("[PreparationDeployment][relocate.cancel] reason=" + reason + "; unit=" + (selectedUnitId ?? "<none>"), this);
            if (State == PreparationInteractionState.Dragging)
                State = interactionEnabled
                    ? (wasRelocating && !string.IsNullOrEmpty(selectedUnitId) ? PreparationInteractionState.SelectedDeployed : PreparationInteractionState.Idle)
                    : PreparationInteractionState.Disabled;
            if (wasRelocating && restoreSelectionIndicator && interactionEnabled) RebindSelectedView();
        }

        private bool BeginRelocateSelected(PreparationUnitView view)
        {
            if (!initialized
                || !interactionEnabled
                || State == PreparationInteractionState.Dragging
                || view == null
                || view.IsPreview)
                return false;
            var unit = CurrentSnapshot?.Units.FirstOrDefault(item =>
                string.Equals(
                    item.UnitId,
                    view.PlayerUnitId,
                    StringComparison.Ordinal));
            if (unit == null
                || unit.Zone != PlayerUnitZone.Deployed
                || !unit.Formation.HasValue
                || pendingExternalUnitIds.Contains(unit.UnitId))
            {
                return false;
            }

            if (!string.Equals(
                    selectedUnitId,
                    unit.UnitId,
                    StringComparison.Ordinal))
                SelectView(view);
            dragSession = new PreparationDragSession(unit.UnitId, unit.TypeId, PreparationDragSource.DeployedRelocation, unit.Formation);
            relocatingView = view;
            submittedCurrentDrag = false;
            if (selectionIndicator != null) selectionIndicator.gameObject.SetActive(false);
            State = PreparationInteractionState.Dragging;
            Debug.Log("[PreparationDeployment][relocate.started] unit=" + unit.UnitId + "; origin=" + unit.Formation.Value, this);
            return true;
        }

        private PreparationUnitView FindDeployedViewAtScreen(Vector3 screenPosition)
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCamera == null) return null;
            foreach (var hit in Physics.RaycastAll(worldCamera.ScreenPointToRay(screenPosition), 2000f).OrderBy(item => item.distance))
            {
                if (hit.collider.GetComponentInParent<PreparationRetreatHitbox>() != null) return null;
                var view = hit.collider.GetComponentInParent<PreparationUnitView>();
                if (view != null && !view.IsPreview) return view;
            }
            return null;
        }

        private static float GetDragThresholdSquared()
        {
            var threshold = EventSystem.current == null ? 5f : Mathf.Max(1, EventSystem.current.pixelDragThreshold);
            return threshold * threshold;
        }

        private void RebindSelectedView()
        {
            if (string.IsNullOrEmpty(selectedUnitId) || viewCoordinator == null || selectionIndicator == null) return;
            if (!viewCoordinator.TryGetView(selectedUnitId, out var view)) return;
            selectionIndicator.gameObject.SetActive(true);
            selectionIndicator.Bind(view, worldCamera);
        }

        private void ProcessWorldClick(Vector3 screenPosition)
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCamera == null) return;
            var ray = worldCamera.ScreenPointToRay(screenPosition);
            var hits = Physics.RaycastAll(ray, 2000f).OrderBy(hit => hit.distance).ToArray();
            foreach (var hit in hits)
            {
                var retreat = hit.collider.GetComponentInParent<PreparationRetreatHitbox>();
                if (retreat != null && selectionIndicator != null && retreat.Indicator == selectionIndicator)
                {
                    RetreatSelected();
                    return;
                }
                var view = hit.collider.GetComponentInParent<PreparationUnitView>();
                if (view != null && !view.IsPreview)
                {
                    SelectView(view);
                    return;
                }
            }
            ClearSelection();
        }

        private PlayerOperationResult RetreatSelected()
        {
            if (!interactionEnabled || string.IsNullOrEmpty(selectedUnitId)) return null;
            var before = CurrentSnapshot;
            if (externalMode)
            {
                if (pendingExternalUnitIds.Contains(selectedUnitId))
                    return null;
                pendingExternalUnitIds.Add(selectedUnitId);
                externalRetreat?.Invoke(selectedUnitId);
                return null;
            }
            var result = hud.PlayerState.TryRetreat(selectedUnitId);
            if (result.Success)
            {
                Debug.Log("[PreparationDeployment][retreat.success] unit=" + selectedUnitId + "; cost=" + before.DeploymentCost + "->" + result.Snapshot.DeploymentCost + "; slots=" + before.StagingSlots.Count + "->" + result.Snapshot.StagingSlots.Count, this);
                ClearSelection();
            }
            else
            {
                Debug.LogWarning("[PreparationDeployment][retreat.failed] code=" + result.Code + "; unit=" + selectedUnitId + "; stateUnchanged=" + (before.CanonicalSummary == result.Snapshot.CanonicalSummary), this);
            }
            return result;
        }

        private void SelectView(PreparationUnitView view)
        {
            if (!interactionEnabled
                || view == null
                || view.IsPreview
                || pendingExternalUnitIds.Contains(
                    view.PlayerUnitId))
            {
                return;
            }
            // The preparation-area and staging-area highlights represent one shared selection.
            hud.ClearStagingSelection();
            selectedUnitId = view.PlayerUnitId;
            DeployedSelectionChanged?.Invoke(selectedUnitId);
            if (selectionIndicator == null)
            {
                var indicatorObject = new GameObject("DeployedUnitSelectionIndicator");
                indicatorObject.transform.SetParent(transform, false);
                selectionIndicator = indicatorObject.AddComponent<PreparationSelectionIndicator>();
                selectionIndicator.Initialize(sprites, worldCamera);
            }
            selectionIndicator.Bind(view, worldCamera);
            State = PreparationInteractionState.SelectedDeployed;
        }

        private void HandleStagingSelectionChanged(string slotId)
        {
            // Clearing a staging highlight must not clear a newly selected deployed unit. Only a
            // non-empty slot selection is a request to transfer the shared selection to staging.
            if (!string.IsNullOrEmpty(slotId)) ClearSelection();
        }

        private void ClearSelection()
        {
            var hadSelection = !string.IsNullOrEmpty(selectedUnitId);
            selectedUnitId = null;
            if (hadSelection) DeployedSelectionChanged?.Invoke(null);
            if (selectionIndicator) Destroy(selectionIndicator.gameObject);
            selectionIndicator = null;
            if (State == PreparationInteractionState.SelectedDeployed) State = interactionEnabled ? PreparationInteractionState.Idle : PreparationInteractionState.Disabled;
        }

        private bool TryGetWorldFromScreen(Vector3 screenPosition, out Vector3 worldPosition)
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCamera == null) { worldPosition = default; return false; }
            var plane = new Plane(Vector3.up, new Vector3(0f, PreparationGridProjection.UnitWorldY, 0f));
            var ray = worldCamera.ScreenPointToRay(screenPosition);
            if (!plane.Raycast(ray, out var distance)) { worldPosition = default; return false; }
            worldPosition = ray.GetPoint(distance);
            return true;
        }

        private static bool IsPointerOverUi() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        private void ShutdownBindings()
        {
            CancelDrag("controller.shutdown", false);
            ClearSelection();
            if (hud != null)
            {
                hud.SetStagingDragStartedHandler(null);
                hud.SetStagingSelectionChangedHandler(null);
            }
            if (viewCoordinator != null) viewCoordinator.Dispose();
            viewCoordinator = null;
            foreach (var pending in
                     pendingExternalDeployViews.Values)
            {
                if (pending != null) Destroy(pending);
            }
            pendingExternalDeployViews.Clear();
            pendingExternalUnitIds.Clear();
            externalMode = false;
            externalSnapshot = null;
            externalDeploy = null;
            externalReplace = null;
            externalRelocate = null;
            externalRetreat = null;
            initialized = false;
        }
    }

    /// <summary>State-driven preparation projection. It owns the one-to-one Player unit ID to GameObject mapping.</summary>
    [DisallowMultipleComponent]
    public sealed class PreparationUnitViewCoordinator : MonoBehaviour
    {
        private readonly Dictionary<string, PreparationUnitView> viewsByUnitId = new Dictionary<string, PreparationUnitView>(StringComparer.Ordinal);
        private PlayerState playerState;

        public int ViewCount => viewsByUnitId.Count;

        public void Initialize(PlayerState state)
        {
            if (playerState == state) return;
            Dispose();
            playerState = state;
            if (playerState == null) return;
            playerState.Changed += Reconcile;
            Reconcile(playerState.Snapshot);
        }

        public void InitializeExternal(
            PlayerStateSnapshot snapshot)
        {
            Dispose();
            ReconcileExternal(snapshot, null);
        }

        public void ReconcileExternal(
            PlayerStateSnapshot snapshot,
            ISet<string> preservedUnitIds)
        {
            Reconcile(snapshot, preservedUnitIds);
        }

        public bool TryGetView(string unitId, out PreparationUnitView view) => viewsByUnitId.TryGetValue(unitId ?? string.Empty, out view) && view != null;

        public void Dispose()
        {
            if (playerState != null) playerState.Changed -= Reconcile;
            playerState = null;
            foreach (var view in viewsByUnitId.Values) if (view != null) Destroy(view.gameObject);
            viewsByUnitId.Clear();
        }

        private void OnDestroy() => Dispose();

        private void Reconcile(PlayerStateSnapshot snapshot)
        {
            Reconcile(snapshot, null);
        }

        private void Reconcile(
            PlayerStateSnapshot snapshot,
            ISet<string> preservedUnitIds)
        {
            if (snapshot == null) return;
            var deployed = snapshot.Units.Where(unit => unit.Zone == PlayerUnitZone.Deployed && unit.Formation.HasValue).ToDictionary(unit => unit.UnitId, StringComparer.Ordinal);
            foreach (var obsolete in viewsByUnitId.Keys.Where(id => !deployed.ContainsKey(id)).ToArray())
            {
                if (viewsByUnitId[obsolete] != null) Destroy(viewsByUnitId[obsolete].gameObject);
                viewsByUnitId.Remove(obsolete);
            }

            foreach (var unit in deployed.Values)
            {
                if (viewsByUnitId.TryGetValue(
                        unit.UnitId,
                        out var existing)
                    && existing != null
                    && (existing.EliteLevel != unit.EliteLevel
                        || !string.Equals(
                            existing.TypeId,
                            unit.TypeId,
                            StringComparison.Ordinal)))
                {
                    Destroy(existing.gameObject);
                    viewsByUnitId.Remove(unit.UnitId);
                }
                if (!viewsByUnitId.TryGetValue(unit.UnitId, out var view) || view == null)
                {
                    if (!PreparationUnitViewBuilder.TryCreate(unit.UnitId, unit.TypeId, unit.EliteLevel, transform, false, out var instance, out var diagnostic))
                    {
                        Debug.LogError("[PreparationViews][create.failed] unit=" + unit.UnitId + "; type=" + unit.TypeId + "; code=" + diagnostic, this);
                        continue;
                    }
                    view = instance.GetComponent<PreparationUnitView>();
                    viewsByUnitId[unit.UnitId] = view;
                }
                if (preservedUnitIds == null
                    || !preservedUnitIds.Contains(unit.UnitId))
                {
                    view.transform.position =
                        PreparationGridProjection.ToWorld(
                            unit.Formation.Value);
                }
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class PreparationUnitView : MonoBehaviour
    {
        public string PlayerUnitId { get; private set; }
        public string TypeId { get; private set; }
        public int EliteLevel { get; private set; }
        public bool IsPreview { get; private set; }

        public void Initialize(
            string playerUnitId,
            string typeId,
            int eliteLevel,
            bool isPreview)
        {
            PlayerUnitId = playerUnitId ?? string.Empty;
            TypeId = typeId ?? string.Empty;
            EliteLevel = eliteLevel;
            IsPreview = isPreview;
        }

        /// <summary>Uses the visible unit renderers rather than the root Transform pivot/anchor.</summary>
        public Vector3 GetWorldCenter()
        {
            var renderers = GetComponentsInChildren<Renderer>(false);
            var hasBounds = false;
            var bounds = default(Bounds);
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return hasBounds ? bounds.center : transform.position;
        }
    }

    internal static class PreparationUnitViewBuilder
    {
        private const string CatalogPath = "BattleData/unit-catalog-v1";

        public static bool TryCreate(string unitId, string typeId, int eliteLevel, Transform parent, bool isPreview, out GameObject instance, out string diagnostic)
        {
            instance = null;
            diagnostic = string.Empty;
            var catalogResult = UnitCatalogLoader.LoadFromResources(CatalogPath);
            if (!catalogResult.Success || !catalogResult.Catalog.TryGet(typeId, eliteLevel, out var entry)) { diagnostic = "resource.mapping.missing"; return false; }
            var prefab = Resources.Load<GameObject>(entry.PrefabResourcePath);
            if (!prefab) { diagnostic = "resource.prefab.missing"; return false; }

            instance = UnityEngine.Object.Instantiate(prefab, parent);
            var skeleton = instance.GetComponent<SkeletonAnimation>();
            var skeletonData = Resources.Load<SkeletonDataAsset>(entry.SkeletonDataResourcePath);
            if (!skeleton || !skeletonData) { UnityEngine.Object.Destroy(instance); instance = null; diagnostic = "resource.skeleton.missing"; return false; }
            try { skeleton.skeletonDataAsset = skeletonData; skeleton.Initialize(true); }
            catch (Exception exception) { UnityEngine.Object.Destroy(instance); instance = null; diagnostic = "resource.skeleton.initialize.failed:" + exception.Message; return false; }

            var identity = instance.GetComponent<UnitIdentity>() ?? instance.AddComponent<UnitIdentity>();
            identity.SetTypeOnce(entry.LegacyUnitTypeId);
            var runtimeIdentityKey = isPreview ? "preview:" + Guid.NewGuid().ToString("N") : unitId;
            identity.unitID = PreparationRuntimeUnitIds.GetOrAssign(runtimeIdentityKey);
            identity.SetPlayerUnitId(isPreview ? string.Empty : unitId);
            var unitSkel = instance.GetComponent<UnitSkelBase>();
            if (!unitSkel)
            {
                if (entry.UnitSkelType == 1) unitSkel = instance.AddComponent<UnitSkelType1>();
                else if (entry.UnitSkelType == 2) unitSkel = instance.AddComponent<UnitSkelType2>();
                else { UnityEngine.Object.Destroy(instance); instance = null; diagnostic = "resource.skeletonType.invalid"; return false; }
            }
            unitSkel.ConfigureCatalogPresentationData(identity, entry.Definition.MoveSpeedCentimetresPerSecond / 100f, entry.Definition.AttackIntervalTicks / (float)ArknoNights.Battle.Core.BattleInput.TicksPerSecond);
            var marker = instance.GetComponent<PreparationUnitView>() ?? instance.AddComponent<PreparationUnitView>();
            marker.Initialize(
                unitId,
                typeId,
                eliteLevel,
                isPreview);
            instance.name = (isPreview ? "PreparationPreview_" : "PreparationView_") + unitId;
            return true;
        }
    }

    /// <summary>Assigns unique runtime ints while UnitIdentity keeps the authoritative PlayerState string separately.</summary>
    internal static class PreparationRuntimeUnitIds
    {
        private static readonly Dictionary<string, int> idsByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        private static int nextId = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            idsByKey.Clear();
            nextId = 1;
        }

        public static int GetOrAssign(string key)
        {
            key = key ?? string.Empty;
            if (idsByKey.TryGetValue(key, out var assigned)) return assigned;
            assigned = nextId++;
            idsByKey.Add(key, assigned);
            return assigned;
        }
    }

    [DisallowMultipleComponent]
    public sealed class PreparationSelectionIndicator : MonoBehaviour
    {
        private const float DiamondWorldSize = 270f;
        private const float ReturnToStagingWorldSize = 63f;
        private const float UnitAnchorForwardOffset = 50f;
        private const float IndicatorWorldY = 200f;
        private PreparationUnitView target;
        private Camera cameraReference;

        public void Initialize(IReadOnlyDictionary<string, Sprite> sprites, Camera camera)
        {
            cameraReference = camera;
            var overlay = CreateSprite("Overlay", sprites, "DeployedUnitSelectionOverlay", 500, DiamondWorldSize);
            CenterSpriteGraphic(overlay);
            var retreat = CreateSprite("ReturnToStaging", sprites, "ReturnToStagingButtonIcon", 501, ReturnToStagingWorldSize);
            CenterSpriteGraphic(retreat);
            var retreatRoot = retreat.transform.parent;
            retreatRoot.localPosition = new Vector3(-60f, 60f, -1f);
            var collider = retreatRoot.gameObject.AddComponent<BoxCollider>();
            // The named ReturnToStaging root stays at its visual centre; its Graphic child carries sprite-pivot compensation.
            var localSize = retreat.sprite == null
                ? new Vector2(2f, 2f)
                : new Vector2(retreat.sprite.bounds.size.x * retreat.transform.localScale.x, retreat.sprite.bounds.size.y * retreat.transform.localScale.y);
            collider.size = new Vector3(localSize.x, localSize.y, 0.1f);
            var hitbox = retreatRoot.gameObject.AddComponent<PreparationRetreatHitbox>();
            hitbox.Indicator = this;
        }

        public void Bind(PreparationUnitView view, Camera camera)
        {
            target = view;
            cameraReference = camera ? camera : cameraReference;
            UpdatePlacement();
        }

        private void LateUpdate()
        {
            if (target == null) { Destroy(gameObject); return; }
            UpdatePlacement();
        }

        private void UpdatePlacement()
        {
            if (target == null) return;
            if (cameraReference == null) cameraReference = Camera.main;
            // Place the group on the unit-anchor-to-camera line. Overlay/ReturnToStaging then centre their own graphics.
            var unitAnchor = target.transform.position + Vector3.forward * UnitAnchorForwardOffset;
            if (cameraReference == null || Mathf.Approximately(cameraReference.transform.position.y, unitAnchor.y))
            {
                transform.position = new Vector3(unitAnchor.x, IndicatorWorldY, unitAnchor.z);
            }
            else
            {
                // Intersect the anchor-to-camera line with the horizontal y=200 plane. This preserves
                // the requested viewing direction while keeping the whole indicator 200 units above ground.
                var cameraPosition = cameraReference.transform.position;
                var t = (IndicatorWorldY - unitAnchor.y) / (cameraPosition.y - unitAnchor.y);
                transform.position = Vector3.LerpUnclamped(unitAnchor, cameraPosition, t);
            }
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private SpriteRenderer CreateSprite(string name, IReadOnlyDictionary<string, Sprite> sprites, string spriteName, int sortingOrder, float size)
        {
            // Root uses the public, named centre anchor. The nested Graphic may safely compensate atlas pivot.
            var root = new GameObject(name);
            root.transform.SetParent(transform, false);
            var child = new GameObject("Graphic", typeof(SpriteRenderer));
            child.transform.SetParent(root.transform, false);
            var renderer = child.GetComponent<SpriteRenderer>();
            sprites.TryGetValue(spriteName, out var sprite);
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            if (sprite != null && sprite.bounds.size.x > 0f) child.transform.localScale = Vector3.one * (size / sprite.bounds.size.x);
            else Debug.LogError("[PreparationSelection][sprite.missing] " + spriteName, this);
            return renderer;
        }

        // Atlas sprites can have a lower-left pivot. Compensate it after scaling so the visible diamond,
        // rather than the imported pivot, is centred on this indicator's unit-aligned transform.
        private static void CenterSpriteGraphic(SpriteRenderer renderer)
        {
            if (renderer == null || renderer.sprite == null) return;
            var spriteCenter = renderer.sprite.bounds.center;
            var scale = renderer.transform.localScale;
            renderer.transform.localPosition = new Vector3(-spriteCenter.x * scale.x, -spriteCenter.y * scale.y, -spriteCenter.z * scale.z);
        }
    }

    public sealed class PreparationRetreatHitbox : MonoBehaviour
    {
        public PreparationSelectionIndicator Indicator { get; set; }
    }
}
