using ArknoNights.Battle.Presentation;
using UnityEngine;

/// <summary>
/// Renders DefaultUnit's presentation-only HP/shield state in world space.
/// The owner supplies all values; this component has no combat, input, or physics role.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitWorldStatusBar : MonoBehaviour
{
    private const string InvalidMaximumDiagnostic = "[UnitWorldStatusBar][invalid.maxHitPoints]";

    [SerializeField] private Transform barRoot;
    [SerializeField] private Renderer backgroundRenderer;
    [SerializeField] private Transform maxHitPointsCapacity;
    [SerializeField] private Renderer currentHitPointsRenderer;
    [SerializeField] private Renderer shieldRenderer;
    [SerializeField] private Material overlayMaterial;
    [SerializeField] private Sprite backgroundSprite;
    [SerializeField] private Sprite homeHitPointsSprite;
    [SerializeField] private Texture2D enemyHitPointsTexture;
    [SerializeField, Min(0.01f)] private float barHeight = 3f;
    [Tooltip("Signed displacement on the unit's local Z axis. Negative moves toward local back/screen-down.")]
    [SerializeField] private float localZOffset = -25f;
    [Tooltip("Signed displacement on the unit's local Y axis.")]
    [SerializeField] private float localYOffset;

    private MaterialPropertyBlock backgroundProperties;
    private MaterialPropertyBlock currentHitPointsProperties;
    private MaterialPropertyBlock shieldProperties;
    private Mesh quadMesh;
    private string unitId = string.Empty;
    private int maxHitPoints;
    private int currentHitPoints;
    private int currentShield;
    private bool isEnemy;
    private bool hasState;
    private bool hasReportedInvalidMaximum;
    private bool lastFacingLeft;
    private bool hasAppliedFacing;
    private UnitWorldStatusBarLayout layout;

    public Transform BarRoot => barRoot;
    public Renderer BackgroundRenderer => backgroundRenderer;
    public Transform MaxHitPointsCapacity => maxHitPointsCapacity;
    public Renderer CurrentHitPointsRenderer => currentHitPointsRenderer;
    public Renderer ShieldRenderer => shieldRenderer;
    public UnitWorldStatusBarLayout Layout => layout;
    public string UnitId => unitId;
    public bool IsEnemy => isEnemy;

    /// <summary>Recalculates facing-dependent anchors immediately after the owning unit turns.</summary>
    public void RefreshForFacing()
    {
        if (hasState) ApplyState();
        else ApplyWorldAnchor();
    }

    private void Awake()
    {
        backgroundProperties = new MaterialPropertyBlock();
        currentHitPointsProperties = new MaterialPropertyBlock();
        shieldProperties = new MaterialPropertyBlock();
        EnsureRenderers();
        SetVisible(false);
        ApplyWorldAnchor();
    }

    private void Update()
    {
        RefreshLayoutForFacingIfNeeded();
    }

    private void LateUpdate()
    {
        if (!RefreshLayoutForFacingIfNeeded()) ApplyWorldAnchor();
    }

    private void OnDestroy()
    {
        if (quadMesh) Destroy(quadMesh);
    }

    /// <summary>Explicit presentation input. Non-zero shield is display-only; it never affects Battle Core.</summary>
    public void SetState(string id, bool valueIsEnemy, int valueMaxHitPoints, int valueCurrentHitPoints, int valueCurrentShield)
    {
        unitId = id ?? string.Empty;
        isEnemy = valueIsEnemy;
        maxHitPoints = valueMaxHitPoints;
        currentHitPoints = valueCurrentHitPoints;
        currentShield = valueCurrentShield;
        hasState = true;
        ApplyState();
    }

    public void SetCurrentHitPoints(int value)
    {
        if (!hasState) return;
        currentHitPoints = value;
        ApplyState();
    }

    public void SetObserverIsEnemy(bool value)
    {
        if (!hasState) return;
        isEnemy = value;
        ApplyState();
    }

    public void ClearState()
    {
        hasState = false;
        maxHitPoints = 0;
        currentHitPoints = 0;
        currentShield = 0;
        layout = default;
        SetVisible(false);
    }

    private void ApplyState()
    {
        EnsureRenderers();
        lastFacingLeft = IsFacingLeft();
        hasAppliedFacing = true;
        layout = UnitWorldStatusBarLayout.Calculate(maxHitPoints, currentHitPoints, currentShield);
        if (!layout.IsValid)
        {
            if (!hasReportedInvalidMaximum)
            {
                hasReportedInvalidMaximum = true;
                Debug.LogError(InvalidMaximumDiagnostic + " unitId=" + (string.IsNullOrEmpty(unitId) ? "<missing>" : unitId) + "; maxHitPoints=" + maxHitPoints + ".", this);
            }
            SetVisible(false);
            return;
        }

        SetVisible(layout.IsVisible);
        if (!layout.IsVisible) return;

        // Root matches the unit plane. Reverse local anchors when its X axis points left so HP and shield keep world-space semantics.
        var horizontalSign = lastFacingLeft ? -1f : 1f;
        maxHitPointsCapacity.localPosition = new Vector3((-UnitWorldStatusBarLayout.TotalWidth * 0.5f + layout.MaxHitPointsWidth * 0.5f) * horizontalSign, 0f, 0f);
        maxHitPointsCapacity.localScale = new Vector3(layout.MaxHitPointsWidth, 1f, 1f);

        var currentRatio = layout.MaxHitPointsWidth <= 0f ? 0f : layout.CurrentHitPointsWidth / layout.MaxHitPointsWidth;
        currentHitPointsRenderer.transform.localPosition = new Vector3((-0.5f + currentRatio * 0.5f) * horizontalSign, 0f, 0f);
        currentHitPointsRenderer.transform.localScale = new Vector3(currentRatio, barHeight, 1f);

        shieldRenderer.transform.localPosition = new Vector3((UnitWorldStatusBarLayout.TotalWidth * 0.5f - layout.ShieldWidth * 0.5f) * horizontalSign, 0f, 0f);
        shieldRenderer.transform.localScale = new Vector3(layout.ShieldWidth, barHeight, 1f);
        backgroundRenderer.transform.localPosition = Vector3.zero;
        backgroundRenderer.transform.localScale = new Vector3(UnitWorldStatusBarLayout.TotalWidth, barHeight, 1f);

        ApplyRendererProperties();
        ApplyWorldAnchor();
    }

    private void ApplyRendererProperties()
    {
        ApplyRendererProperties(backgroundRenderer, backgroundProperties, backgroundSprite, Color.white);
        if (isEnemy) ApplyRendererProperties(currentHitPointsRenderer, currentHitPointsProperties, enemyHitPointsTexture, Color.white);
        else ApplyRendererProperties(currentHitPointsRenderer, currentHitPointsProperties, homeHitPointsSprite, Color.white);
        ApplyRendererProperties(shieldRenderer, shieldProperties, homeHitPointsSprite, Color.white, true);
    }

    private static void ApplyRendererProperties(Renderer renderer, MaterialPropertyBlock properties, Texture texture, Color color, bool forceWhite = false)
    {
        if (!renderer) return;
        properties.Clear();
        properties.SetTexture("_MainTex", texture);
        properties.SetVector("_SpriteUvRect", new Vector4(1f, 1f, 0f, 0f));
        properties.SetColor("_Color", color);
        properties.SetFloat("_ForceWhite", forceWhite ? 1f : 0f);
        renderer.SetPropertyBlock(properties);
    }

    private static void ApplyRendererProperties(Renderer renderer, MaterialPropertyBlock properties, Sprite sprite, Color color, bool forceWhite = false)
    {
        if (!renderer) return;
        properties.Clear();
        var texture = sprite ? sprite.texture : null;
        var rect = sprite ? sprite.textureRect : default;
        var width = texture ? texture.width : 1f;
        var height = texture ? texture.height : 1f;
        properties.SetTexture("_MainTex", texture);
        properties.SetVector("_SpriteUvRect", new Vector4(rect.width / width, rect.height / height, rect.x / width, rect.y / height));
        properties.SetColor("_Color", color);
        properties.SetFloat("_ForceWhite", forceWhite ? 1f : 0f);
        renderer.SetPropertyBlock(properties);
    }

    private void SetVisible(bool visible)
    {
        if (barRoot && barRoot.gameObject.activeSelf != visible) barRoot.gameObject.SetActive(visible);
    }

    private bool RefreshLayoutForFacingIfNeeded()
    {
        var facingLeft = IsFacingLeft();
        if (!hasState || (hasAppliedFacing && facingLeft == lastFacingLeft)) return false;
        ApplyState();
        return true;
    }

    private void ApplyWorldAnchor()
    {
        if (!barRoot) return;
        // Keep the screen-down placement stable when left facing flips the unit's local Z axis.
        var effectiveLocalZOffset = IsFacingLeft() ? -localZOffset : localZOffset;
        var localOffset = new Vector3(0f, localYOffset, effectiveLocalZOffset);
        barRoot.position = transform.position + transform.TransformDirection(localOffset);
        barRoot.rotation = transform.rotation;
        var ownerScale = transform.lossyScale;
        barRoot.localScale = new Vector3(Reciprocal(ownerScale.x), Reciprocal(ownerScale.y), Reciprocal(ownerScale.z));
    }

    private void EnsureRenderers()
    {
        if (!quadMesh) quadMesh = CreateQuadMesh();
        ConfigureRenderer(backgroundRenderer, 300);
        ConfigureRenderer(currentHitPointsRenderer, 301);
        ConfigureRenderer(shieldRenderer, 302);
    }

    private void ConfigureRenderer(Renderer renderer, int sortingOrder)
    {
        if (!renderer) return;
        var filter = renderer.GetComponent<MeshFilter>();
        if (filter && !filter.sharedMesh) filter.sharedMesh = quadMesh;
        if (overlayMaterial && renderer.sharedMaterial != overlayMaterial) renderer.sharedMaterial = overlayMaterial;
        renderer.sortingOrder = sortingOrder;
    }

    private static float Reciprocal(float value) => Mathf.Abs(value) < 0.0001f ? 1f : 1f / value;

    private bool IsFacingLeft() => transform.right.x < 0f;

    private static Mesh CreateQuadMesh()
    {
        var mesh = new Mesh { name = "UnitWorldStatusBarQuad" };
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        return mesh;
    }
}
