using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DecoyDroneUnit : MonoBehaviour
{
    [Header("Selection Visual")]
    public GameObject selectionIndicator;
    public Color selectedColor = new Color(0f, 1f, 0.5f, 1f);
    public Color hoverColor = Color.white;

    [Header("Model Correction")]
    public Vector3 modelRotationOffset = Vector3.zero;

    [Header("Idle Hover")]
    public float hoverAmplitude = 0.15f;
    public float hoverFrequency = 1.2f;

    [Header("MALD Deployment")]
    public GameObject maldPrefab;
    public Transform maldLaunchPoint;
    public float launchRange = 80f;
    public float maldSpeed = 25f;
    public float waypointReachDistance = 1f;
    public Vector3 maldModelRotationOffset = Vector3.zero;
    public float maldRotationSpeed = 720f;
    public float maldFallSpeed = 20f;
    public float maldFallDuration = 2f;
    public float maldFallRotationSpeed = 240f;

    [Header("MALD Defense Targeting")]
    public bool makeMaldTargetableByDefense = true;
    public bool addMaldColliderIfMissing = true;
    public bool addMaldHealthIfMissing = true;
    public bool addMaldRigidbodyIfMissing = true;

    [Header("Visual Body")]
    public Transform bodyTransform;

    private Vector3 _hoverBasePosition;
    private float _hoverOffset;
    private Outline _outline;
    private Renderer[] _renderers;
    private bool _isHovered;

    public bool IsSelected { get; private set; }
    public Vector3 LaunchPosition => maldLaunchPoint != null ? maldLaunchPoint.position : transform.position;

    void Awake()
    {
        _hoverBasePosition = transform.position;
        _hoverOffset = Random.Range(0f, Mathf.PI * 2f);
        _outline = GetComponentInChildren<Outline>();
        _renderers = GetComponentsInChildren<Renderer>();

        if (bodyTransform == null) bodyTransform = transform;

        if (bodyTransform != null)
            bodyTransform.localRotation = Quaternion.Euler(modelRotationOffset);

        SetSelectionIndicator(false);
        RefreshOutline();
    }

    void Update()
    {
        UpdateIdleHover();
    }

    void UpdateIdleHover()
    {
        float newY = _hoverBasePosition.y +
                    Mathf.Sin(Time.time * hoverFrequency * Mathf.PI * 2f + _hoverOffset)
                    * hoverAmplitude;

        transform.position = new Vector3(
            _hoverBasePosition.x,
            newY,
            _hoverBasePosition.z);
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        SetSelectionIndicator(selected);
        RefreshOutline();
    }

    public void SetHovered(bool hovered)
    {
        _isHovered = hovered;
        RefreshOutline();
    }

    public bool ContainsScreenPoint(Camera camera, Vector2 screenPoint, float padding = 8f)
    {
        if (camera == null || _renderers == null || _renderers.Length == 0)
            return false;

        bool hasVisiblePoint = false;
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        foreach (var renderer in _renderers)
        {
            if (renderer == null || !renderer.enabled) continue;

            Bounds bounds = renderer.bounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        Vector3 screen = camera.WorldToScreenPoint(corner);
                        if (screen.z <= 0f) continue;

                        hasVisiblePoint = true;
                        min = Vector2.Min(min, screen);
                        max = Vector2.Max(max, screen);
                    }
                }
            }
        }

        if (!hasVisiblePoint) return false;

        Rect screenRect = Rect.MinMaxRect(
            min.x - padding,
            min.y - padding,
            max.x + padding,
            max.y + padding);

        return screenRect.Contains(screenPoint, true);
    }

    public bool CanDeployPath(System.Collections.Generic.IReadOnlyList<Vector3> waypoints)
    {
        return waypoints != null && waypoints.Count > 0 && GetPathLength(waypoints) <= launchRange;
    }

    public float GetPathLength(System.Collections.Generic.IReadOnlyList<Vector3> waypoints)
    {
        if (waypoints == null || waypoints.Count == 0) return 0f;

        float total = 0f;
        Vector3 previous = LaunchPosition;

        for (int i = 0; i < waypoints.Count; i++)
        {
            total += Vector3.Distance(previous, waypoints[i]);
            previous = waypoints[i];
        }

        return total;
    }

    public Vector3 GetFlightPoint(Vector3 worldPosition)
    {
        return new Vector3(worldPosition.x, LaunchPosition.y, worldPosition.z);
    }

    public void DeployMald(System.Collections.Generic.IReadOnlyList<Vector3> waypoints)
    {
        if (!CanDeployPath(waypoints))
        {
            Debug.LogWarning($"{name}: MALD path is empty or exceeds launch range.");
            return;
        }

        if (maldPrefab == null)
        {
            Debug.LogWarning($"{name}: maldPrefab is not assigned.");
            return;
        }

        GameObject mald = Instantiate(maldPrefab, LaunchPosition, transform.rotation);
        ConfigureMaldDefenseTarget(mald);

        MALDPathFollower follower = mald.GetComponent<MALDPathFollower>();
        if (follower == null)
            follower = mald.AddComponent<MALDPathFollower>();

        follower.speed = maldSpeed;
        follower.waypointReachDistance = waypointReachDistance;
        follower.modelRotationOffset = maldModelRotationOffset;
        follower.rotationSpeed = maldRotationSpeed;
        follower.fallSpeed = maldFallSpeed;
        follower.fallDuration = maldFallDuration;
        follower.fallRotationSpeed = maldFallRotationSpeed;
        follower.SetPath(waypoints);
    }

    void ConfigureMaldDefenseTarget(GameObject mald)
    {
        if (!makeMaldTargetableByDefense || mald == null) return;

        mald.tag = "PlayerDrone";

        if (mald.layer == 0)
            mald.layer = gameObject.layer;

        if (addMaldColliderIfMissing && mald.GetComponentInChildren<Collider>() == null)
            AddColliderFromVisualBounds(mald);

        if (addMaldRigidbodyIfMissing && mald.GetComponent<Rigidbody>() == null)
        {
            Rigidbody rb = mald.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;
        }

        if (addMaldHealthIfMissing && mald.GetComponentInParent<DroneHealth>() == null)
            mald.AddComponent<DroneHealth>();
    }

    void AddColliderFromVisualBounds(GameObject mald)
    {
        BoxCollider box = mald.AddComponent<BoxCollider>();
        MeshFilter meshFilter = mald.GetComponentInChildren<MeshFilter>();

        if (meshFilter != null && meshFilter.sharedMesh != null && meshFilter.transform == mald.transform)
        {
            Bounds meshBounds = meshFilter.sharedMesh.bounds;
            box.center = meshBounds.center;
            box.size = meshBounds.size;
            return;
        }

        Renderer[] visualRenderers = mald.GetComponentsInChildren<Renderer>();
        if (visualRenderers == null || visualRenderers.Length == 0) return;

        Bounds worldBounds = visualRenderers[0].bounds;
        for (int i = 1; i < visualRenderers.Length; i++)
            worldBounds.Encapsulate(visualRenderers[i].bounds);

        Vector3 scale = mald.transform.lossyScale;
        box.center = mald.transform.InverseTransformPoint(worldBounds.center);
        box.size = new Vector3(
            SafeDivide(worldBounds.size.x, scale.x),
            SafeDivide(worldBounds.size.y, scale.y),
            SafeDivide(worldBounds.size.z, scale.z));
    }

    float SafeDivide(float value, float divisor)
    {
        if (Mathf.Abs(divisor) < 0.0001f) return value;
        return Mathf.Abs(value / divisor);
    }

    void SetSelectionIndicator(bool show)
    {
        if (selectionIndicator != null)
            selectionIndicator.SetActive(show);
    }

    void RefreshOutline()
    {
        if (_outline == null) return;

        bool showOutline = _isHovered || IsSelected;
        _outline.enabled = showOutline;

        if (showOutline)
            _outline.OutlineColor = _isHovered ? hoverColor : selectedColor;
    }
}
