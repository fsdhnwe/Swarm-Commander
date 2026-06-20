using System;
using UnityEngine;
using Pathfinding;

[RequireComponent(typeof(Seeker))]
[RequireComponent(typeof(Collider))]
public class ReconDroneUnit : MonoBehaviour
{
    public enum ReconState { Idle, Move }

    [Header("Selection Visual")]
    public GameObject selectionIndicator;
    public Color selectedColor = new Color(0f, 1f, 0.5f, 1f);
    public Color hoverColor = Color.white;

    [Header("Movement")]
    public float moveSpeed = 8f;
    public float nextWaypointDist = 1f;
    public float arrivalDist = 1.5f;
    public float turnSpeed = 240f;
    public float fullSpeedTurnAngle = 25f;
    public float turnInPlaceAngle = 120f;

    [Header("Model Correction")]
    public Vector3 modelRotationOffset = Vector3.zero;

    [Header("Idle Hover")]
    public float hoverAmplitude = 0.15f;
    public float hoverFrequency = 1.2f;

    [Header("Visual Body")]
    public Transform bodyTransform;

    [Header("Boids - Separation")]
    public float neighborRadius = 8f;
    public LayerMask droneLayer = 1 << 6;
    public float separationWeight = 2.5f;
    public float separationDistance = 6f;
    public float seekWeight = 2f;

    private ReconState _state = ReconState.Idle;
    private Seeker _seeker;
    private Path _path;
    private int _waypointIndex;
    private bool _reachedEnd;
    private Vector3 _hoverBasePosition;
    private float _hoverOffset;
    private Outline _outline;
    private Renderer[] _renderers;
    private bool _isHovered;
    private Vector3 _currentMoveDir;

    private static readonly Collider[] _neighborBuffer = new Collider[32];

    public ReconState State => _state;
    public bool IsSelected { get; private set; }
    public Vector3 CurrentMoveDir => _currentMoveDir;
    public event Action<ReconDroneUnit> MoveArrived;

    void Awake()
    {
        _seeker = GetComponent<Seeker>();
        _hoverBasePosition = transform.position;
        _hoverOffset = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        _outline = GetComponentInChildren<Outline>();
        _renderers = GetComponentsInChildren<Renderer>();

        if (bodyTransform == null)
            bodyTransform = transform;

        bodyTransform.localRotation = Quaternion.Euler(modelRotationOffset);

        SetSelectionIndicator(false);
        RefreshOutline();
    }

    void Update()
    {
        switch (_state)
        {
            case ReconState.Idle:
                UpdateIdle();
                break;
            case ReconState.Move:
                UpdateMove();
                break;
        }
    }

    void UpdateIdle()
    {
        float newY = _hoverBasePosition.y +
            Mathf.Sin(Time.time * hoverFrequency * Mathf.PI * 2f + _hoverOffset) * hoverAmplitude;

        transform.position = new Vector3(_hoverBasePosition.x, newY, _hoverBasePosition.z);
        _currentMoveDir = Vector3.zero;
    }

    void UpdateMove()
    {
        if (_path == null || _reachedEnd) return;

        Vector3 destination = _path.vectorPath[_path.vectorPath.Count - 1];
        float distToEnd = Vector3.Distance(
            new Vector3(transform.position.x, 0f, transform.position.z),
            new Vector3(destination.x, 0f, destination.z));

        if (distToEnd <= arrivalDist)
        {
            _reachedEnd = true;
            _hoverBasePosition = transform.position;
            _currentMoveDir = Vector3.zero;
            ChangeState(ReconState.Idle);
            MoveArrived?.Invoke(this);
            return;
        }

        if (_waypointIndex >= _path.vectorPath.Count)
            _waypointIndex = _path.vectorPath.Count - 1;

        Vector3 waypoint = _path.vectorPath[_waypointIndex];
        waypoint.y = transform.position.y;

        Vector3 seekDir = (waypoint - transform.position).normalized;
        Vector3 blendedDir = seekDir * seekWeight + ComputeSeparation() * separationWeight;

        if (blendedDir.sqrMagnitude < 0.0001f)
            blendedDir = seekDir;

        blendedDir.Normalize();
        FaceDirection(blendedDir);

        float speedFactor = GetTurnSpeedFactor(blendedDir);
        _currentMoveDir = speedFactor > 0.01f ? blendedDir : Vector3.zero;
        transform.position += blendedDir * moveSpeed * speedFactor * Time.deltaTime;

        if (Vector3.Distance(transform.position, waypoint) < nextWaypointDist)
            _waypointIndex++;
    }

    Vector3 ComputeSeparation()
    {
        Vector3 result = Vector3.zero;
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, neighborRadius, _neighborBuffer, droneLayer);

        for (int i = 0; i < count; i++)
        {
            Collider other = _neighborBuffer[i];
            if (other == null) continue;
            if (other.gameObject == gameObject) continue;

            Vector3 toSelf = transform.position - other.transform.position;
            float dist = toSelf.magnitude;

            if (dist > 0f && dist < separationDistance)
                result += toSelf.normalized * (separationDistance - dist) / separationDistance;
        }

        result.y = 0f;
        return result;
    }

    public void MoveTo(Vector3 worldPosition)
    {
        Vector3 startPos = transform.position;
        Vector3 endPos = new Vector3(worldPosition.x, transform.position.y, worldPosition.z);

        ChangeState(ReconState.Move);
        _seeker.StartPath(startPos, endPos, OnPathComplete);
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

        foreach (Renderer renderer in _renderers)
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

    void ChangeState(ReconState newState)
    {
        _state = newState;
        if (newState == ReconState.Move) return;

        _path = null;
        _reachedEnd = false;
    }

    void OnPathComplete(Path path)
    {
        if (path.error)
        {
            Debug.LogWarning($"{name}: A* path failed. {path.errorLog}");
            ChangeState(ReconState.Idle);
            return;
        }

        _path = path;
        _waypointIndex = 0;
        _reachedEnd = false;
    }

    void FaceDirection(Vector3 direction)
    {
        Vector3 flatDir = new Vector3(direction.x, 0f, direction.z);
        if (flatDir.sqrMagnitude <= 0.001f) return;

        Quaternion targetRot = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRot,
            turnSpeed * Time.deltaTime);
    }

    float GetTurnSpeedFactor(Vector3 desiredDirection)
    {
        Vector3 flatDir = new Vector3(desiredDirection.x, 0f, desiredDirection.z);
        if (flatDir.sqrMagnitude <= 0.001f) return 0f;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.001f) return 1f;

        float angle = Vector3.Angle(forward.normalized, flatDir.normalized);
        float stopAngle = Mathf.Max(fullSpeedTurnAngle + 0.01f, turnInPlaceAngle);
        return Mathf.InverseLerp(stopAngle, fullSpeedTurnAngle, angle);
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

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, neighborRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, separationDistance);
    }
}
