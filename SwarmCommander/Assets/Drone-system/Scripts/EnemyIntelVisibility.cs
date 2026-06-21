using TMPro;
using UnityEngine;

public enum EnemyIntelState
{
    Unknown,
    Confirmed,
    StaleIntel,
    LostContact
}

public class EnemyIntelVisibility : MonoBehaviour
{
    [Header("Intel Confidence")]
    public bool permanentlyConfirmed;
    public EnemyIntelState initialState = EnemyIntelState.Unknown;
    public float confirmedDuration = 5f;
    public float staleDuration = 10f;

    [Header("Visuals")]
    public bool controlRenderers = true;
    [Range(0f, 1f)] public float confirmedAlpha = 1f;
    [Range(0f, 1f)] public float staleEndAlpha = 0.4f;
    public Color confirmedLabelColor = Color.green;
    public Color staleLabelColor = Color.yellow;
    public Color lostContactColor = Color.gray;
    public GameObject[] extraVisuals;
    public TextMeshPro worldLabel;
    public GameObject lostContactMarkerPrefab;
    public float markerHeight = 2f;

    [Header("Legacy")]
    [Tooltip("Use permanentlyConfirmed instead. Kept so older prefab settings do not break.")]
    public bool permanentlyVisible;

    private Renderer[] _renderers;
    private bool[] _initialRendererEnabled;
    private Material[][] _rendererMaterials;
    private MaterialState[][] _initialMaterialStates;
    private Color[][] _initialMaterialColors;
    private bool[] _initialExtraVisualActive;
    private TargetableObject[] _targetables;
    private EnemyIntelState _state;
    private float _confirmedUntil;
    private float _staleUntil;
    private float _lastConfirmedTime = -1f;
    private Vector3 _lastKnownPosition;
    private GameObject _lostContactMarker;
    private TextMeshPro _lostContactLabel;

    public EnemyIntelState State => _state;
    public float SecondsSinceLastConfirmed => _lastConfirmedTime < 0f ? 0f : Time.time - _lastConfirmedTime;
    public float Confidence01 => GetConfidence01();
    public bool HasIntelDisplay => permanentlyConfirmed || _state != EnemyIntelState.Unknown;
    public bool HasTargetableIntel => permanentlyConfirmed || _state == EnemyIntelState.Confirmed || _state == EnemyIntelState.StaleIntel;

    // Older scripts still ask this. It now means "has actionable target intel", not "is visually shown".
    public bool IsVisibleToPlayer => HasTargetableIntel;

    void Awake()
    {
        if (extraVisuals == null)
            extraVisuals = new GameObject[0];

        permanentlyConfirmed = permanentlyConfirmed || permanentlyVisible;
        _lastKnownPosition = transform.position;
        _renderers = GetComponentsInChildren<Renderer>(true);
        _rendererMaterials = new Material[_renderers.Length][];
        _initialMaterialStates = new MaterialState[_renderers.Length][];
        _initialMaterialColors = new Color[_renderers.Length][];
        _initialRendererEnabled = new bool[_renderers.Length];

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer renderer = _renderers[i];
            if (renderer == null) continue;

            _initialRendererEnabled[i] = renderer.enabled;
            _rendererMaterials[i] = renderer.materials;
            _initialMaterialStates[i] = new MaterialState[_rendererMaterials[i].Length];
            _initialMaterialColors[i] = new Color[_rendererMaterials[i].Length];

            for (int j = 0; j < _rendererMaterials[i].Length; j++)
            {
                _initialMaterialStates[i][j] = MaterialState.Capture(_rendererMaterials[i][j]);
                _initialMaterialColors[i][j] = GetMaterialColor(_rendererMaterials[i][j]);
            }
        }

        _initialExtraVisualActive = new bool[extraVisuals.Length];
        for (int i = 0; i < extraVisuals.Length; i++)
            _initialExtraVisualActive[i] = extraVisuals[i] != null && extraVisuals[i].activeSelf;

        _targetables = GetComponentsInChildren<TargetableObject>(true);
        _state = permanentlyConfirmed ? EnemyIntelState.Confirmed : initialState;

        if (_state == EnemyIntelState.Confirmed || _state == EnemyIntelState.StaleIntel)
        {
            _lastConfirmedTime = Time.time;
            _lastKnownPosition = transform.position;
            _confirmedUntil = _state == EnemyIntelState.Confirmed ? Time.time + confirmedDuration : Time.time;
            _staleUntil = _confirmedUntil + staleDuration;
        }

        ApplyIntelVisuals();
    }

    void Start()
    {
        ApplyIntelVisuals();
    }

    void Update()
    {
        if (permanentlyConfirmed || permanentlyVisible)
        {
            _state = EnemyIntelState.Confirmed;
            _lastKnownPosition = transform.position;
            ApplyIntelVisuals();
            return;
        }

        if (_state == EnemyIntelState.Confirmed && Time.time >= _confirmedUntil)
            _state = EnemyIntelState.StaleIntel;

        if (_state == EnemyIntelState.StaleIntel && Time.time >= _staleUntil)
            _state = EnemyIntelState.LostContact;

        ApplyIntelVisuals();
    }

    public void Confirm(float confirmedSeconds, float staleSeconds)
    {
        confirmedDuration = Mathf.Max(0f, confirmedSeconds);
        staleDuration = Mathf.Max(0f, staleSeconds);
        ConfirmNow();
    }

    public void ConfirmNow()
    {
        _state = EnemyIntelState.Confirmed;
        _lastConfirmedTime = Time.time;
        _lastKnownPosition = transform.position;
        _confirmedUntil = Time.time + confirmedDuration;
        _staleUntil = _confirmedUntil + staleDuration;
        ApplyIntelVisuals();
    }

    public void RevealFor(float duration)
    {
        float totalDuration = Mathf.Max(0f, duration);
        confirmedDuration = Mathf.Min(5f, totalDuration);
        staleDuration = Mathf.Max(0f, totalDuration - confirmedDuration);
        ConfirmNow();
    }

    public void SetPermanentlyVisible(bool visible)
    {
        permanentlyVisible = visible;
        permanentlyConfirmed = visible;
        _state = visible ? EnemyIntelState.Confirmed : initialState;
        ApplyIntelVisuals();
    }

    void ApplyIntelVisuals()
    {
        bool showRealObject = _state == EnemyIntelState.Confirmed || _state == EnemyIntelState.StaleIntel;

        if (controlRenderers && _renderers != null)
        {
            float alpha = GetDisplayAlpha();

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null) continue;

                renderer.enabled = showRealObject && _initialRendererEnabled[i];
                if (renderer.enabled)
                    ApplyRendererAlpha(i, alpha);
                else
                    RestoreRendererMaterials(i);
            }
        }

        for (int i = 0; i < extraVisuals.Length; i++)
        {
            GameObject visual = extraVisuals[i];
            if (visual != null)
                visual.SetActive(showRealObject && _initialExtraVisualActive[i]);
        }

        UpdateWorldLabel();
        UpdateLostContactMarker();
        RefreshTargetables();
    }

    float GetConfidence01()
    {
        if (_state == EnemyIntelState.Unknown) return 0f;
        if (_state == EnemyIntelState.Confirmed) return 1f;
        if (_state == EnemyIntelState.LostContact) return 0f;

        float staleStart = _confirmedUntil;
        float staleLength = Mathf.Max(0.001f, _staleUntil - staleStart);
        float staleProgress = Mathf.Clamp01((Time.time - staleStart) / staleLength);
        return Mathf.Lerp(0.8f, 0.4f, staleProgress);
    }

    float GetDisplayAlpha()
    {
        if (_state == EnemyIntelState.Confirmed)
            return confirmedAlpha;

        if (_state == EnemyIntelState.StaleIntel)
            return Mathf.Lerp(confirmedAlpha, staleEndAlpha, 1f - Mathf.InverseLerp(0.4f, 0.8f, Confidence01));

        return 0f;
    }

    void ApplyRendererAlpha(int rendererIndex, float alpha)
    {
        Material[] materials = _rendererMaterials[rendererIndex];
        Color[] initialColors = _initialMaterialColors[rendererIndex];
        if (materials == null || initialColors == null) return;

        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material == null) continue;

            if (alpha < 0.999f)
                ConfigureMaterialForTransparency(material);
            else
                RestoreMaterialState(rendererIndex, i);

            Color color = i < initialColors.Length ? initialColors[i] : Color.white;
            color.a *= alpha;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }
    }

    void RestoreRendererMaterials(int rendererIndex)
    {
        Material[] materials = _rendererMaterials[rendererIndex];
        if (materials == null) return;

        for (int i = 0; i < materials.Length; i++)
            RestoreMaterialState(rendererIndex, i);
    }

    void RestoreMaterialState(int rendererIndex, int materialIndex)
    {
        if (_initialMaterialStates == null || rendererIndex >= _initialMaterialStates.Length) return;

        MaterialState[] states = _initialMaterialStates[rendererIndex];
        Material[] materials = _rendererMaterials[rendererIndex];
        if (states == null || materials == null || materialIndex >= states.Length || materialIndex >= materials.Length) return;

        states[materialIndex].Restore(materials[materialIndex]);
    }

    static void ConfigureMaterialForTransparency(Material material)
    {
        if (material == null) return;

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);

        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);

        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);

        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    static Color GetMaterialColor(Material material)
    {
        if (material == null) return Color.white;
        if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color")) return material.GetColor("_Color");
        return Color.white;
    }

    struct MaterialState
    {
        public int renderQueue;
        public bool surfaceTransparent;
        public bool alphaBlend;
        public bool alphaTest;
        public bool hasSurface;
        public bool hasBlend;
        public bool hasSrcBlend;
        public bool hasDstBlend;
        public bool hasZWrite;
        public float surface;
        public float blend;
        public float srcBlend;
        public float dstBlend;
        public float zWrite;

        public static MaterialState Capture(Material material)
        {
            if (material == null)
                return default;

            return new MaterialState
            {
                renderQueue = material.renderQueue,
                surfaceTransparent = material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"),
                alphaBlend = material.IsKeywordEnabled("_ALPHABLEND_ON"),
                alphaTest = material.IsKeywordEnabled("_ALPHATEST_ON"),
                hasSurface = material.HasProperty("_Surface"),
                hasBlend = material.HasProperty("_Blend"),
                hasSrcBlend = material.HasProperty("_SrcBlend"),
                hasDstBlend = material.HasProperty("_DstBlend"),
                hasZWrite = material.HasProperty("_ZWrite"),
                surface = material.HasProperty("_Surface") ? material.GetFloat("_Surface") : 0f,
                blend = material.HasProperty("_Blend") ? material.GetFloat("_Blend") : 0f,
                srcBlend = material.HasProperty("_SrcBlend") ? material.GetFloat("_SrcBlend") : 0f,
                dstBlend = material.HasProperty("_DstBlend") ? material.GetFloat("_DstBlend") : 0f,
                zWrite = material.HasProperty("_ZWrite") ? material.GetFloat("_ZWrite") : 0f,
            };
        }

        public void Restore(Material material)
        {
            if (material == null) return;

            if (hasSurface) material.SetFloat("_Surface", surface);
            if (hasBlend) material.SetFloat("_Blend", blend);
            if (hasSrcBlend) material.SetFloat("_SrcBlend", srcBlend);
            if (hasDstBlend) material.SetFloat("_DstBlend", dstBlend);
            if (hasZWrite) material.SetFloat("_ZWrite", zWrite);

            SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", surfaceTransparent);
            SetKeyword(material, "_ALPHABLEND_ON", alphaBlend);
            SetKeyword(material, "_ALPHATEST_ON", alphaTest);
            material.renderQueue = renderQueue;
        }

        static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }
    }

    void UpdateWorldLabel()
    {
        if (worldLabel == null) return;

        worldLabel.gameObject.SetActive(_state == EnemyIntelState.Confirmed || _state == EnemyIntelState.StaleIntel);

        if (_state == EnemyIntelState.Confirmed)
        {
            worldLabel.text = "Confirmed";
            worldLabel.color = confirmedLabelColor;
        }
        else if (_state == EnemyIntelState.StaleIntel)
        {
            worldLabel.text = $"Last Seen {Mathf.FloorToInt(SecondsSinceLastConfirmed)}s ago";
            worldLabel.color = staleLabelColor;
        }

        FaceCamera(worldLabel.transform);
    }

    void UpdateLostContactMarker()
    {
        bool showMarker = _state == EnemyIntelState.LostContact;

        if (!showMarker)
        {
            if (_lostContactMarker != null)
                _lostContactMarker.SetActive(false);
            return;
        }

        if (_lostContactMarker == null)
            CreateLostContactMarker();

        if (_lostContactMarker == null) return;

        _lostContactMarker.SetActive(true);
        _lostContactMarker.transform.position = _lastKnownPosition + Vector3.up * markerHeight;

        if (_lostContactLabel != null)
        {
            _lostContactLabel.text = "?";
            _lostContactLabel.color = lostContactColor;
            FaceCamera(_lostContactLabel.transform);
        }
    }

    void CreateLostContactMarker()
    {
        if (lostContactMarkerPrefab != null)
        {
            _lostContactMarker = Instantiate(lostContactMarkerPrefab, _lastKnownPosition, Quaternion.identity);
            _lostContactLabel = _lostContactMarker.GetComponentInChildren<TextMeshPro>();
            return;
        }

        _lostContactMarker = new GameObject($"{name} Lost Contact Marker");
        _lostContactMarker.transform.SetParent(transform.parent);

        _lostContactLabel = _lostContactMarker.AddComponent<TextMeshPro>();
        _lostContactLabel.alignment = TextAlignmentOptions.Center;
        _lostContactLabel.fontSize = 4f;
        _lostContactLabel.text = "?";
        _lostContactLabel.color = lostContactColor;
    }

    void RefreshTargetables()
    {
        if (_targetables == null) return;

        foreach (TargetableObject targetable in _targetables)
        {
            if (targetable != null)
                targetable.RefreshIntelState();
        }
    }

    static void FaceCamera(Transform labelTransform)
    {
        Camera camera = Camera.main;
        if (camera == null || labelTransform == null) return;

        Vector3 direction = labelTransform.position - camera.transform.position;
        if (direction.sqrMagnitude <= 0.001f) return;

        labelTransform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = _state switch
        {
            EnemyIntelState.Confirmed => Color.green,
            EnemyIntelState.StaleIntel => Color.yellow,
            EnemyIntelState.LostContact => Color.gray,
            _ => Color.black
        };

        Gizmos.DrawWireSphere(transform.position, 1.5f);
        if (_state == EnemyIntelState.LostContact)
            Gizmos.DrawWireCube(_lastKnownPosition, Vector3.one * 2f);
    }
}
