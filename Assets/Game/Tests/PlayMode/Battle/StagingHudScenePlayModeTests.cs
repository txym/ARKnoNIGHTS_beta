using System.Collections;
using System.Linq;
using System.Reflection;
using ArknoNights.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ArknoNights.Battle.Tests
{
    public sealed class StagingHudScenePlayModeTests
    {
        [UnityTest]
        public IEnumerator SampleScene_UI005UsesExistingHudAndShowsOnlyDeclaredResourcePlaceholders()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            for (var frame = 0; frame < 16; frame++) yield return null;

            var hud = Object.FindObjectOfType<StagingHudController>();
            Assert.NotNull(hud);
            var numericFont = Resources.Load<Font>("Fonts/Novecento wide Normal Regular.woff2");
            Assert.NotNull(numericFont, "The supplied Novecento Wide Normal Regular resource must be loadable by the Player.");
            var extension = hud.GetComponent("FormalBattleHudUi005");
            Assert.NotNull(extension, "UI-005 must extend the existing formal HUD instead of creating another PlayerState root.");
            var canvas = hud.transform.Find("FormalBattleHudCanvas");
            Assert.NotNull(canvas);
            Assert.AreEqual(1, hud.GetComponentsInChildren<Canvas>().Length);
            Assert.AreEqual("--", canvas.Find("FormalHudUi005/GoldCurrencyPanel/Value").GetComponent<Text>().text);
            Assert.AreEqual("--", canvas.Find("FormalHudUi005/BattleStatusPanel/PlayerHealth").GetComponent<Text>().text);
            Assert.That(canvas.Find("DeploymentCostPanel/Cost").GetComponent<RectTransform>().anchoredPosition.y, Is.EqualTo(40f).Within(0.01f));
            Assert.AreSame(numericFont, canvas.Find("DeploymentCostPanel/Cost").GetComponent<Text>().font);
            Assert.AreSame(numericFont, canvas.Find("StagingArea/StagingSlot/Header/Cost").GetComponent<Text>().font);
            Assert.AreSame(numericFont, canvas.Find("StagingArea/StagingSlot/StackCount").GetComponent<Text>().font);
            Assert.AreSame(numericFont, canvas.Find("FormalHudUi005/BattleStatusPanel/Middle").GetComponent<Text>().font);
            var firstSlot = canvas.Find("StagingArea/StagingSlot");
            var portraitSize = firstSlot.Find("PortraitClip/Portrait").GetComponent<RectTransform>().rect.width;
            Assert.That(firstSlot.Find("EliteIcon").GetComponent<RectTransform>().anchoredPosition, Is.EqualTo(new Vector2(portraitSize / 30f, portraitSize / 30f)));
            var stagingRarity = firstSlot.Find("PortraitClip/Portrait/RarityIcon").GetComponent<RectTransform>();
            Assert.That(stagingRarity.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(stagingRarity.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.NotNull(stagingRarity.GetComponent<Image>().sprite, "Every staging stack must render the catalog-provided rarity icon.");

            hud.ToggleSelection(StagingHudController.BuildSlotId(hud.Snapshot.StagingSlots[0]));
            yield return null;
            var information = canvas.Find("FormalHudUi005/UnitInformationPanel");
            Assert.IsTrue(information.gameObject.activeSelf);
            Assert.That(information.Find("HealthBackground/HealthFill").GetComponent<RectTransform>().sizeDelta.x, Is.EqualTo(556f).Within(0.01f));
            var healthValue = information.Find("HealthValue").GetComponent<RectTransform>();
            Assert.That(healthValue.pivot, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(healthValue.anchoredPosition, Is.EqualTo(new Vector2(556f, -490f)));
            Assert.AreSame(numericFont, information.Find("HealthValue/Text").GetComponent<Text>().font);
            Assert.AreEqual(24, information.Find("HealthValue/Text").GetComponent<Text>().fontSize);
            Assert.That(information.Find("HealthValue/Text").GetComponent<RectTransform>().offsetMax.y, Is.EqualTo(-5f).Within(0.01f));
            Assert.IsFalse(information.Find("HealthValue/Text").GetComponent<Text>().text.Contains(" "));
            Assert.AreEqual("--", information.Find("UnitName").GetComponent<Text>().text, "An empty Chinese display name must not fall back to a resource key.");
            Assert.NotNull(information.Find("Portrait/Rarity").GetComponent<Image>().sprite);
            Assert.NotNull(information.Find("Portrait/Elite").GetComponent<Image>().sprite);
            Assert.That(information.Find("Portrait/Rarity").GetComponent<RectTransform>().anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(information.Find("Portrait/Elite").GetComponent<RectTransform>().anchorMin, Is.EqualTo(Vector2.zero));
            Assert.AreEqual(8, information.Cast<Transform>().Count(child => child.name.StartsWith("Stat_")));
            Assert.That(canvas.Find("FormalHudUi005/BattleStatusPanel/PlayerHealth").GetComponent<Text>().color, Is.EqualTo(new Color(1f, .47058824f, .47058824f)));
            Assert.IsTrue(information.Find("Tab_技能").GetComponent<Text>().text.Contains("未接入"));
        }

        [UnityTest]
        public IEnumerator SampleScene_UI005MakesBattleAndStagingInformationSelectionsMutuallyExclusive()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            for (var frame = 0; frame < 20; frame++) yield return null;

            var hud = Object.FindObjectOfType<StagingHudController>();
            var formalHud = hud.GetComponent("FormalBattleHudUi005");
            var loop = hud.GetComponent("PreparationBattleLoopController");
            Assert.NotNull(formalHud);
            Assert.NotNull(loop);

            loop.GetType().GetMethod("AdvanceForTests").Invoke(loop, new object[] { 30f });
            yield return null;
            Assert.AreSame(Resources.Load<Font>("Fonts/Novecento wide Normal Regular.woff2"), hud.transform.Find("FormalBattleHudCanvas/FormalHudUi005/BattleStatusPanel/Left").GetComponent<Text>().font);
            Assert.IsFalse(hud.transform.Find("FormalBattleHudCanvas/FormalHudUi005/BattleStatusPanel/Left").GetComponent<Text>().text.Contains(" "));
            var demo = GameObject.Find("BattleDemoRoot").GetComponent("BattleDemoController");
            var coordinator = demo.GetType().GetProperty("Coordinator").GetValue(demo);
            var states = (IEnumerable)coordinator.GetType().GetProperty("PresentationViewStates").GetValue(coordinator);
            string enemyId = null;
            foreach (var state in states)
            {
                if (state.GetType().GetProperty("Side").GetValue(state).ToString() != "Away") continue;
                enemyId = (string)state.GetType().GetProperty("UnitId").GetValue(state);
                break;
            }
            Assert.IsNotNull(enemyId);
            formalHud.GetType().GetMethod("SelectBattleUnitForHud").Invoke(formalHud, new object[] { enemyId });
            Assert.IsTrue((bool)formalHud.GetType().GetProperty("SelectedBattleEnemy").GetValue(formalHud));
            Assert.IsNull(hud.SelectedSlotId);

            var stagingSlot = hud.Snapshot.StagingSlots[0];
            hud.ToggleSelection(StagingHudController.BuildSlotId(stagingSlot));
            yield return null;
            Assert.IsFalse((bool)formalHud.GetType().GetProperty("SelectedBattleEnemy").GetValue(formalHud));
            Assert.AreEqual(stagingSlot.UnitIds[0], (string)formalHud.GetType().GetProperty("SelectedUnitId").GetValue(formalHud));
        }

        [UnityTest]
        public IEnumerator SampleScene_AutoLoadsFormalHudWithoutInitButton()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var hud = Object.FindObjectOfType<StagingHudController>();
            Assert.NotNull(hud);
            Assert.AreEqual(1, Object.FindObjectsOfType<StagingHudController>().Length);
            Assert.IsTrue(hud.InitializationSucceeded);
            Assert.AreEqual(99, hud.PlayerState.DeploymentCost);
            Assert.AreEqual(2, hud.SlotCount);
            Assert.AreEqual("1000", hud.Snapshot.StagingSlots[0].TypeId);
            Assert.AreEqual("5503", hud.Snapshot.StagingSlots[1].TypeId);
            var costPanel = hud.transform.Find("FormalBattleHudCanvas/DeploymentCostPanel");
            Assert.NotNull(costPanel);
            Assert.IsFalse(costPanel.Find("Background").GetComponent<Image>().preserveAspect);
            Assert.AreEqual(new Vector2(40f, 40f), costPanel.Find("Icon").GetComponent<RectTransform>().anchoredPosition);
            var firstSlot = hud.transform.Find("FormalBattleHudCanvas/StagingArea/StagingSlot");
            Assert.NotNull(firstSlot);
            Assert.NotNull(firstSlot.GetComponent<IDragHandler>(), "A staging slot must implement IDragHandler or EventSystem will never send OnBeginDrag.");
            Assert.IsFalse(firstSlot.Find("Background").GetComponent<Image>().preserveAspect);
            Assert.IsFalse(firstSlot.Find("PortraitOverlay").GetComponent<Image>().preserveAspect);
            Assert.IsFalse(firstSlot.Find("Elite1Decoration").GetComponent<Image>().preserveAspect);
            Assert.IsFalse(firstSlot.Find("Elite2PlusHighlight").GetComponent<Image>().preserveAspect);
            Assert.IsFalse(firstSlot.Find("SelectionOverlay").GetComponent<Image>().preserveAspect);
            Assert.IsNull(GameObject.Find("InitButton"));
            Assert.IsNull(GameObject.Find("ShopPanel"));
            Assert.IsNull(GameObject.Find("FoldButton"));
            Assert.IsNull(GameObject.Find("ButtonTest"));
            var demoRoot = GameObject.Find("BattleDemoRoot");
            Assert.IsTrue(demoRoot.activeInHierarchy);
            var debugToggle = Object.FindObjectOfType<BattleDemoUiVisibilityToggle>();
            Assert.NotNull(debugToggle);
            Assert.IsFalse(debugToggle.Visible);
            debugToggle.SetVisible(true);
            Assert.IsTrue(debugToggle.Visible);
            debugToggle.SetVisible(false);
            Assert.IsFalse(debugToggle.Visible);
            Assert.IsTrue(demoRoot.activeInHierarchy);

            var selected = StagingHudController.BuildSlotId(hud.Snapshot.StagingSlots[0]);
            hud.ToggleSelection(selected);
            Assert.AreEqual(selected, hud.SelectedSlotId);
            Assert.IsTrue(hud.PlayerState.TryDeploy("local-1000-alpha", 5, 2).Success);
            Assert.IsNull(hud.SelectedSlotId);
        }

        [UnityTest]
        public IEnumerator SampleScene_StateDrivenDeploymentCreatesAndRetreatsOneRealPreparationView()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return null;
            yield return null;

            var hud = Object.FindObjectOfType<StagingHudController>();
            Assert.NotNull(hud);
            var controller = hud.GetComponent("ArknoNights.Deployment.StateDrivenDeploymentController");
            Assert.NotNull(controller, "The runtime bootstrap must attach exactly one state-driven deployment controller.");
            var type = controller.GetType();
            var firstSlotId = StagingHudController.BuildSlotId(hud.Snapshot.StagingSlots[0]);
            Assert.IsTrue((bool)type.GetMethod("BeginDragFromSlot").Invoke(controller, new object[] { firstSlotId }));
            type.GetMethod("SetDragWorldPositionForTests").Invoke(controller, new object[] { new Vector3(500f, 0f, 200f) });
            type.GetMethod("CommitCurrentDragForTests").Invoke(controller, null);

            Assert.AreEqual(97, hud.PlayerState.DeploymentCost);
            Assert.AreEqual(1, hud.PlayerState.GetUnits(ArknoNights.Player.PlayerUnitZone.Deployed).Count);
            Assert.AreEqual("local-1000-alpha", hud.PlayerState.GetUnits(ArknoNights.Player.PlayerUnitZone.Deployed)[0].UnitId);
            Assert.AreEqual(1, (int)type.GetProperty("PreparationViewCount").GetValue(controller));
            var deployedView = GameObject.Find("PreparationUnitViews").transform.Find("PreparationView_local-1000-alpha");
            Assert.NotNull(deployedView);
            Assert.AreEqual(new Vector3(500f, 0f, 200f), deployedView.position);
            var identity = deployedView.GetComponent("UnitIdentity");
            Assert.NotNull(identity);
            Assert.AreEqual("local-1000-alpha", identity.GetType().GetProperty("PlayerUnitId").GetValue(identity));
            Assert.Greater((int)identity.GetType().GetField("unitID").GetValue(identity), 0);

            Assert.IsTrue((bool)type.GetMethod("SelectDeployedForTests").Invoke(controller, new object[] { "local-1000-alpha" }));
            var remainingSlotId = StagingHudController.BuildSlotId(hud.Snapshot.StagingSlots[0]);
            hud.ToggleSelection(remainingSlotId);
            Assert.IsNull(type.GetProperty("SelectedUnitId").GetValue(controller), "Selecting staging must clear the deployed selection.");
            yield return null;
            Assert.IsNull(GameObject.Find("DeployedUnitSelectionIndicator"));
            Assert.IsTrue((bool)type.GetMethod("SelectDeployedForTests").Invoke(controller, new object[] { "local-1000-alpha" }));
            Assert.IsNull(hud.SelectedSlotId, "Selecting a deployed unit must clear the staging selection.");
            var indicator = GameObject.Find("DeployedUnitSelectionIndicator");
            Assert.NotNull(indicator);
            var indicatorAnchor = deployedView.position + Vector3.forward * 50f;
            var cameraPosition = Camera.main.transform.position;
            var cameraLineT = (200f - indicatorAnchor.y) / (cameraPosition.y - indicatorAnchor.y);
            var expectedIndicatorPosition = Vector3.LerpUnclamped(indicatorAnchor, cameraPosition, cameraLineT);
            Assert.Less(Vector3.Distance(expectedIndicatorPosition, indicator.transform.position), 0.01f);
            Assert.AreEqual(200f, indicator.transform.position.y, 0.01f);
            Assert.AreEqual(90f, Mathf.Repeat(indicator.transform.eulerAngles.x, 360f), 0.01f);
            var overlayAnchor = indicator.transform.Find("Overlay");
            var overlay = overlayAnchor.Find("Graphic").GetComponent<SpriteRenderer>();
            Assert.Less(Vector3.Distance(indicator.transform.position, overlayAnchor.position), 0.01f, "Overlay's named Transform must be the centre anchor.");
            Assert.Less(Vector3.Distance(indicator.transform.position, overlay.bounds.center), 0.01f, "The visible diamond centre must align with the selected unit centre.");
            Assert.AreEqual(270f, overlay.bounds.size.x, 0.1f, "The selection diamond must render at 1.5x its prior 180-world-unit size.");
            var retreatAnchor = indicator.transform.Find("ReturnToStaging");
            var retreatRenderer = retreatAnchor.Find("Graphic").GetComponent<SpriteRenderer>();
            var expectedRetreatCentre = indicator.transform.TransformPoint(new Vector3(-60f, 60f, -1f));
            Assert.Less(Vector3.Distance(expectedRetreatCentre, retreatAnchor.position), 0.01f, "ReturnToStaging's named Transform must be its centre anchor.");
            Assert.Less(Vector3.Distance(expectedRetreatCentre, retreatRenderer.bounds.center), 0.01f, "ReturnToStaging must be centred on its configured local coordinate.");
            Assert.AreEqual(63f, retreatRenderer.bounds.size.x, 0.1f, "ReturnToStaging must use the same 1.5x scale factor as Overlay.");
            var retreatCollider = retreatAnchor.GetComponent<BoxCollider>();
            Assert.Less(retreatCollider.bounds.size.magnitude, 100f, "The retreat hitbox must match the enlarged icon rather than the half-board.");
            type.GetMethod("RetreatSelectedForTests").Invoke(controller, null);
            yield return null;

            Assert.AreEqual(99, hud.PlayerState.DeploymentCost);
            Assert.AreEqual(0, hud.PlayerState.GetUnits(ArknoNights.Player.PlayerUnitZone.Deployed).Count);
            Assert.AreEqual(2, hud.Snapshot.StagingSlots[0].Count);
            Assert.AreEqual(0, (int)type.GetProperty("PreparationViewCount").GetValue(controller));
        }

        [UnityTest]
        public IEnumerator SampleScene_DragPreviewFailureAndInteractionLockLeavePlayerStateUntouched()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return null;
            yield return null;

            var hud = Object.FindObjectOfType<StagingHudController>();
            var controller = hud.GetComponent("ArknoNights.Deployment.StateDrivenDeploymentController");
            Assert.NotNull(controller);
            var type = controller.GetType();
            var before = hud.Snapshot.CanonicalSummary;
            var firstSlotId = StagingHudController.BuildSlotId(hud.Snapshot.StagingSlots[0]);

            Assert.IsTrue((bool)type.GetMethod("BeginDragFromSlot").Invoke(controller, new object[] { firstSlotId }));
            type.GetMethod("SetDragWorldPositionForTests").Invoke(controller, new object[] { new Vector3(555f, 0f, 456f) });
            var preview = GameObject.Find("PreparationPreview_local-1000-alpha");
            Assert.NotNull(preview);
            Assert.AreEqual(new Vector3(555f, 0f, 456f), preview.transform.position, "The dragged preview must follow free world input before release.");
            type.GetMethod("SetDragWorldPositionForTests").Invoke(controller, new object[] { new Vector3(500f, 0f, 100f) });
            var gateResult = (ArknoNights.Player.PlayerOperationResult)type.GetMethod("CommitCurrentDragForTests").Invoke(controller, null);
            Assert.AreEqual(ArknoNights.Player.PlayerOperationCode.CoordinateIsGate, gateResult.Code);
            Assert.AreEqual(before, hud.Snapshot.CanonicalSummary);

            Assert.IsTrue((bool)type.GetMethod("BeginDragFromSlot").Invoke(controller, new object[] { firstSlotId }));
            type.GetMethod("SetInteractionEnabled").Invoke(controller, new object[] { false });
            yield return null;
            Assert.AreEqual(before, hud.Snapshot.CanonicalSummary);
            Assert.AreEqual("Disabled", type.GetProperty("State").GetValue(controller).ToString());
            Assert.IsNull(GameObject.Find("PreparationPreview_local-1000-alpha"));
            type.GetMethod("SetInteractionEnabled").Invoke(controller, new object[] { true });
        }

        [UnityTest]
        public IEnumerator SampleScene_SelectedDeployedRelocationSwapsViewsAndKeepsOriginalSelection()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            for (var frame = 0; frame < 3; frame++) yield return null;

            var hud = Object.FindObjectOfType<StagingHudController>();
            var controller = hud.GetComponent("ArknoNights.Deployment.StateDrivenDeploymentController");
            var type = controller.GetType();
            Assert.IsTrue(hud.PlayerState.TryDeploy("local-1000-alpha", 4, 2).Success);
            Assert.IsTrue(hud.PlayerState.TryDeploy("local-1000-bravo", 6, 2).Success);
            yield return null;

            Assert.IsFalse((bool)type.GetMethod("BeginRelocateSelectedForTests").Invoke(controller, new object[] { "local-1000-alpha" }), "An unselected deployed unit must not start relocation.");
            Assert.IsTrue((bool)type.GetMethod("SelectDeployedForTests").Invoke(controller, new object[] { "local-1000-alpha" }));
            var indicator = GameObject.Find("DeployedUnitSelectionIndicator");
            Assert.NotNull(indicator);
            Assert.IsTrue(indicator.activeSelf);
            var beforeCost = hud.PlayerState.DeploymentCost;
            Assert.IsTrue((bool)type.GetMethod("BeginRelocateSelectedForTests").Invoke(controller, new object[] { "local-1000-alpha" }));
            Assert.IsFalse(indicator.activeSelf, "The selected-unit frame must be hidden while relocation is dragging.");
            type.GetMethod("SetDragWorldPositionForTests").Invoke(controller, new object[] { new Vector3(600f, 0f, 200f) });
            var result = (ArknoNights.Player.PlayerOperationResult)type.GetMethod("CommitCurrentDragForTests").Invoke(controller, null);
            yield return null;

            Assert.IsTrue(result.Success);
            Assert.IsTrue(indicator.activeSelf, "The selected-unit frame must return after relocation completes.");
            Assert.AreEqual(beforeCost, hud.PlayerState.DeploymentCost);
            Assert.AreEqual("local-1000-alpha", (string)type.GetProperty("SelectedUnitId").GetValue(controller));
            Assert.AreEqual(new Vector3(600f, 0f, 200f), GameObject.Find("PreparationUnitViews").transform.Find("PreparationView_local-1000-alpha").position);
            Assert.AreEqual(new Vector3(400f, 0f, 200f), GameObject.Find("PreparationUnitViews").transform.Find("PreparationView_local-1000-bravo").position);
            Assert.AreEqual(2, (int)type.GetProperty("PreparationViewCount").GetValue(controller));

            Assert.IsTrue((bool)type.GetMethod("BeginRelocateSelectedForTests").Invoke(controller, new object[] { "local-1000-alpha" }));
            Assert.IsFalse(indicator.activeSelf);
            var noOpResult = (ArknoNights.Player.PlayerOperationResult)type.GetMethod("CommitCurrentDragForTests").Invoke(controller, null);
            yield return null;
            Assert.IsTrue(noOpResult.Success);
            Assert.IsTrue(indicator.activeSelf, "A same-cell no-op must restore the frame for the original selection.");
        }

        [UnityTest]
        public IEnumerator SampleScene_SelectedDeployedRelocationFailureAndLockRestoreViewWithoutChangingState()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            for (var frame = 0; frame < 3; frame++) yield return null;

            var hud = Object.FindObjectOfType<StagingHudController>();
            var controller = hud.GetComponent("ArknoNights.Deployment.StateDrivenDeploymentController");
            var type = controller.GetType();
            Assert.IsTrue(hud.PlayerState.TryDeploy("local-1000-alpha", 4, 2).Success);
            yield return null;
            Assert.IsTrue((bool)type.GetMethod("SelectDeployedForTests").Invoke(controller, new object[] { "local-1000-alpha" }));
            var indicator = GameObject.Find("DeployedUnitSelectionIndicator");
            Assert.NotNull(indicator);
            var before = hud.Snapshot.CanonicalSummary;
            var view = GameObject.Find("PreparationUnitViews").transform.Find("PreparationView_local-1000-alpha");
            var origin = view.position;

            Assert.IsTrue((bool)type.GetMethod("BeginRelocateSelectedForTests").Invoke(controller, new object[] { "local-1000-alpha" }));
            Assert.IsFalse(indicator.activeSelf);
            type.GetMethod("SetDragWorldPositionForTests").Invoke(controller, new object[] { new Vector3(500f, 0f, 100f) });
            var gateResult = (ArknoNights.Player.PlayerOperationResult)type.GetMethod("CommitCurrentDragForTests").Invoke(controller, null);
            yield return null;
            Assert.AreEqual(ArknoNights.Player.PlayerOperationCode.CoordinateIsGate, gateResult.Code);
            Assert.AreEqual(before, hud.Snapshot.CanonicalSummary);
            Assert.AreEqual(origin, view.position);
            Assert.AreEqual("local-1000-alpha", (string)type.GetProperty("SelectedUnitId").GetValue(controller));
            Assert.IsTrue(indicator.activeSelf, "A failed relocation must restore the frame for the original selection.");

            Assert.IsTrue((bool)type.GetMethod("BeginRelocateSelectedForTests").Invoke(controller, new object[] { "local-1000-alpha" }));
            type.GetMethod("SetDragWorldPositionForTests").Invoke(controller, new object[] { new Vector3(600f, 0f, 200f) });
            type.GetMethod("SetInteractionEnabled").Invoke(controller, new object[] { false });
            yield return null;
            Assert.AreEqual(before, hud.Snapshot.CanonicalSummary);
            Assert.AreEqual(origin, view.position);
            Assert.IsNull(type.GetProperty("SelectedUnitId").GetValue(controller));
            Assert.IsNull(GameObject.Find("DeployedUnitSelectionIndicator"), "Phase lock must retain its existing selection-clear behavior.");
        }

        [UnityTest]
        public IEnumerator SampleScene_SelectedDeployedRelocationReleaseOverUiCancelsWithoutSubmitting()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            for (var frame = 0; frame < 3; frame++) yield return null;

            var hud = Object.FindObjectOfType<StagingHudController>();
            var controller = hud.GetComponent("ArknoNights.Deployment.StateDrivenDeploymentController");
            var type = controller.GetType();
            Assert.IsTrue(hud.PlayerState.TryDeploy("local-1000-alpha", 4, 2).Success);
            yield return null;
            Assert.IsTrue((bool)type.GetMethod("SelectDeployedForTests").Invoke(controller, new object[] { "local-1000-alpha" }));
            var indicator = GameObject.Find("DeployedUnitSelectionIndicator");
            Assert.NotNull(indicator);
            var before = hud.Snapshot.CanonicalSummary;
            var view = GameObject.Find("PreparationUnitViews").transform.Find("PreparationView_local-1000-alpha");
            var origin = view.position;

            Assert.IsTrue((bool)type.GetMethod("BeginRelocateSelectedForTests").Invoke(controller, new object[] { "local-1000-alpha" }));
            Assert.IsFalse(indicator.activeSelf);
            type.GetMethod("SetDragWorldPositionForTests").Invoke(controller, new object[] { new Vector3(600f, 0f, 200f) });
            Assert.IsNull(type.GetMethod("ReleaseCurrentDragForTests").Invoke(controller, new object[] { true }));
            yield return null;

            Assert.AreEqual(before, hud.Snapshot.CanonicalSummary);
            Assert.AreEqual(origin, view.position);
            Assert.AreEqual("local-1000-alpha", (string)type.GetProperty("SelectedUnitId").GetValue(controller));
            Assert.AreEqual("SelectedDeployed", type.GetProperty("State").GetValue(controller).ToString());
            Assert.IsTrue(indicator.activeSelf, "Cancelling release over UI must restore the original selection frame.");

            Assert.IsTrue((bool)type.GetMethod("BeginRelocateSelectedForTests").Invoke(controller, new object[] { "local-1000-alpha" }));
            Assert.IsFalse(indicator.activeSelf);
            type.GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, new object[] { false });
            yield return null;
            Assert.AreEqual("local-1000-alpha", (string)type.GetProperty("SelectedUnitId").GetValue(controller));
            Assert.AreEqual("SelectedDeployed", type.GetProperty("State").GetValue(controller).ToString());
            Assert.IsTrue(indicator.activeSelf, "Focus-loss cancellation must restore the frame for the original selection.");
        }

        [UnityTest]
        public IEnumerator SampleScene_RetreatHitboxIsNotARelocationDragStart()
        {
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            for (var frame = 0; frame < 3; frame++) yield return null;

            var hud = Object.FindObjectOfType<StagingHudController>();
            var controller = hud.GetComponent("ArknoNights.Deployment.StateDrivenDeploymentController");
            var type = controller.GetType();
            Assert.IsTrue(hud.PlayerState.TryDeploy("local-1000-alpha", 4, 2).Success);
            yield return null;
            Assert.IsTrue((bool)type.GetMethod("SelectDeployedForTests").Invoke(controller, new object[] { "local-1000-alpha" }));
            yield return null;
            var retreatCollider = GameObject.Find("DeployedUnitSelectionIndicator").transform.Find("ReturnToStaging").GetComponent<BoxCollider>();
            var screenPosition = Camera.main.WorldToScreenPoint(retreatCollider.bounds.center);

            Assert.IsNull(type.GetMethod("FindSelectedViewAtScreen", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, new object[] { screenPosition }));
            Assert.AreEqual("SelectedDeployed", type.GetProperty("State").GetValue(controller).ToString());
        }
    }
}
