using UnityEngine;
using UnityEngine.UI;

namespace ArknoNights.UI
{
    /// <summary>Hides only the BattleDemo uGUI surface; the BattleDemoRoot/controller stays enabled.</summary>
    [DisallowMultipleComponent]
    public sealed class BattleDemoUiVisibilityToggle : MonoBehaviour
    {
        [SerializeField] private GameObject battleDemoUiRoot;
        [SerializeField] private KeyCode triggerKey = KeyCode.F10;
        [SerializeField] private bool requireControl = true;
        [SerializeField] private bool requireShift = true;

        private Canvas canvas;
        private GraphicRaycaster raycaster;
        private CanvasGroup canvasGroup;
        private bool visible;

        public bool Visible => visible;

        private void Awake()
        {
            if (!battleDemoUiRoot)
            {
                Debug.LogError("[StagingHud][battleDemoUi.reference.missing] UI surface was not assigned; BattleDemoRoot was left untouched.", this);
                return;
            }
            canvas = battleDemoUiRoot.GetComponent<Canvas>();
            raycaster = battleDemoUiRoot.GetComponent<GraphicRaycaster>();
            canvasGroup = battleDemoUiRoot.GetComponent<CanvasGroup>() ?? battleDemoUiRoot.AddComponent<CanvasGroup>();
            SetVisible(false);
        }

        private void Update()
        {
            if (canvas == null || !Input.GetKeyDown(triggerKey)) return;
            if (requireControl && !(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) return;
            if (requireShift && !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) return;
            SetVisible(!visible);
        }

        public void SetVisible(bool value)
        {
            visible = value;
            if (canvas) canvas.enabled = value;
            if (raycaster) raycaster.enabled = value;
            if (canvasGroup)
            {
                canvasGroup.alpha = value ? 1f : 0f;
                canvasGroup.interactable = value;
                canvasGroup.blocksRaycasts = value;
            }
            Debug.Log("[StagingHud][battleDemoUi.visibility] visible=" + value, this);
        }
    }
}
