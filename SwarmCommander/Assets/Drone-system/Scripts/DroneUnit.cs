using UnityEngine;
using Pathfinding;

[RequireComponent(typeof(Seeker))]
[RequireComponent(typeof(Collider))]
public class DroneUnit : MonoBehaviour
{
    public enum DroneState { Idle, Move, Attack, Return }

    [Header("Selection Visual")]
    public GameObject selectionIndicator;
    public Color selectedColor = new Color(0f, 1f, 0.5f, 1f);
    public Color hoverColor = Color.white;
    public Color normalColor = Color.white;

    [Header("Movement")]
    public float moveSpeed = 8f;
    public float nextWaypointDist = 1f;
    public float arrivalDist = 1.5f;

    [Header("Rotation")]
    public float rotationSpeed = 8f;

    [Header("Model Correction")]
    public Vector3 modelRotationOffset = Vector3.zero;

    [Header("Idle Hover")]
    public float hoverAmplitude = 0.15f;
    public float hoverFrequency = 1.2f;

    [Header("Move Tilt")]
    public float maxTiltAngle = 18f;
    public float tiltSpeed = 5f;

    [Header("Base / Return")]
    public Transform baseTransform;

    [Header("Visual Body")]
    public Transform bodyTransform;

    [Header("Boids - Separation & Alignment")]
    public float neighborRadius = 4f;
    public LayerMask droneLayer;
    public float separationWeight = 2.5f;
    public float alignmentWeight = 1f;
    public float separationDistance = 2.5f;
    public float seekWeight = 2f;

    [Header("Attack")]
    public float attackRange = 18f;
    public float attackDamage = 50f;
    public float attackInterval = 1.5f;
    public float missileSpeed = 40f;
    public GameObject missilePrefab;
    public Transform missileLaunchPoint;

    private DroneState _state = DroneState.Idle;
    private Seeker _seeker;
    private Path _path;
    private int _waypointIndex;
    private bool _reachedEnd;
    private Vector3 _hoverBasePosition;
    private float _hoverOffset;
    private Renderer[] _renderers;
    private TargetableObject _attackTarget;
    private Outline _outline;
    private bool _isHovered;
    private float _nextAttackTime;
    private Vector3 _currentMoveDir;

    private static readonly Collider[] _neighborBuffer = new Collider[32];

    public DroneState State => _state;
    public bool IsSelected { get; private set; }
    public Vector3 CurrentMoveDir => _currentMoveDir;

    void Awake()
    {
        _seeker = GetComponent<Seeker>();
        _renderers = GetComponentsInChildren<Renderer>();
        _hoverBasePosition = transform.position;
        _hoverOffset = Random.Range(0f, Mathf.PI * 2f);
        _outline = GetComponentInChildren<Outline>();

        if (bodyTransform == null)
            bodyTransform = transform;

        bodyTransform.localRotation = Quaternion.Euler(modelRotationOffset);

        RefreshOutline();
        SetSelectionIndicator(false);
        ApplyColor(normalColor);
    }

    void Update()
    {
        switch (_state)
        {
            case DroneState.Idle:
                UpdateIdle();
                break;
            case DroneState.Move:
            case DroneState.Return:
                UpdateMove();
                break;
            case DroneState.Attack:
                UpdateAttack();
                break;
        }
    }

    void UpdateIdle()
    {
        float newY = _hoverBasePosition.y +
            Mathf.Sin(Time.time * hoverFrequency * Mathf.PI * 2f + _hoverOffset) * hoverAmplitude;

        transform.position = new Vector3(_hoverBasePosition.x, newY, _hoverBasePosition.z);
        _currentMoveDir = Vector3.zero;
        SmoothTilt(Vector3.zero);
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
            ChangeState(DroneState.Idle);
            return;
        }

        if (_waypointIndex >= _path.vectorPath.Count)
            _waypointIndex = _path.vectorPath.Count - 1;

        Vector3 waypoint = _path.vectorPath[_waypointIndex];
        waypoint.y = transform.position.y;

        Vector3 seekDir = (waypoint - transform.position).normalized;
        float boidsFalloff = Mathf.Clamp01(distToEnd / separationDistance);

        Vector3 blendedDir = seekDir * seekWeight
            + ComputeSeparation() * separationWeight * boidsFalloff
            + ComputeAlignment() * alignmentWeight * boidsFalloff;

        if (blendedDir.sqrMagnitude < 0.0001f)
            blendedDir = seekDir;

        blendedDir.Normalize();
        _currentMoveDir = blendedDir;
        transform.position += blendedDir * moveSpeed * Time.deltaTime;

        FaceDirection(blendedDir);
        SmoothTilt(seekDir);

        if (Vector3.Distance(transform.position, waypoint) < nextWaypointDist)
            _waypointIndex++;
    }

    void UpdateAttack()
    {
        if (_attackTarget == null || !_attackTarget.IsAlive)
        {
            _hoverBasePosition = transform.position;
            _attackTarget = null;
            ChangeState(DroneState.Idle);
            return;
        }

        Vector3 dir = _attackTarget.AimPosition - transform.position;
        Vector3 flatDir = new Vector3(dir.x, 0f, dir.z);
        float distance = dir.magnitude;

        if (distance > attackRange)
        {
            Vector3 moveDir = dir.normalized;
            _currentMoveDir = moveDir;
            transform.position += moveDir * moveSpeed * Time.deltaTime;
            FaceDirection(moveDir);
            SmoothTilt(moveDir);
            return;
        }

        FaceDirection(flatDir);
        _currentMoveDir = Vector3.zero;
        SmoothTilt(Vector3.zero);

        if (Time.time < _nextAttackTime) return;

        _nextAttackTime = Time.time + attackInterval;
        LaunchMissile(_attackTarget);
    }

    void LaunchMissile(TargetableObject target)
    {
        if (missilePrefab == null)
        {
            Debug.LogWarning($"{name}: missilePrefab is not assigned.");
            return;
        }

        Vector3 launchPos = missileLaunchPoint != null ? missileLaunchPoint.position : transform.position;
        GameObject missileObject = Instantiate(missilePrefab, launchPos, transform.rotation);
        Missile missile = missileObject.GetComponent<Missile>();
        if (missile == null) return;

        missile.target = target;
        missile.damage = attackDamage;
        missile.speed = missileSpeed;
    }

    Vector3 ComputeSeparation()
    {
        Vector3 result = Vector3.zero;
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, neighborRadius, _neighborBuffer, droneLayer);

        for (int i = 0; i < count; i++)
        {
            Collider other = _neighborBuffer[i];
            if (other.gameObject == gameObject) continue;

            Vector3 toSelf = transform.position - other.transform.position;
            float dist = toSelf.magnitude;

            if (dist > 0f && dist < separationDistance)
                result += toSelf.normalized * (separationDistance - dist) / separationDistance;
        }

        result.y = 0f;
        return result;
    }

    Vector3 ComputeAlignment()
    {
        Vector3 avgDir = Vector3.zero;
        int validCount = 0;

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, neighborRadius, _neighborBuffer, droneLayer);

        for (int i = 0; i < count; i++)
        {
            Collider other = _neighborBuffer[i];
            if (other.gameObject == gameObject) continue;

            DroneUnit otherDrone = other.GetComponentInParent<DroneUnit>();
            if (otherDrone == null) continue;
            if (otherDrone.State != DroneState.Move && otherDrone.State != DroneState.Return) continue;
            if (otherDrone.CurrentMoveDir.sqrMagnitude < 0.0001f) continue;

            avgDir += otherDrone.CurrentMoveDir;
            validCount++;
        }

        if (validCount == 0) return Vector3.zero;

        avgDir /= validCount;
        avgDir.y = 0f;
        return avgDir.normalized;
    }

    public void MoveTo(Vector3 worldPosition)
    {
        _attackTarget = null;
        _nextAttackTime = 0f;

        Vector3 startPos = transform.position;
        Vector3 endPos = new Vector3(worldPosition.x, transform.position.y, worldPosition.z);

        ChangeState(DroneState.Move);
        _seeker.StartPath(startPos, endPos, OnPathComplete);
    }

    public void AttackTarget(TargetableObject target)
    {
        if (target == null || !target.IsAlive) return;

        _attackTarget = target;
        _nextAttackTime = 0f;
        ChangeState(DroneState.Attack);
    }

    public void AttackTarget(Transform target)
    {
        if (target == null) return;

        TargetableObject targetable = target.GetComponentInParent<TargetableObject>();
        if (targetable == null) return;

        AttackTarget(targetable);
    }

    public void ReturnToBase()
    {
        if (baseTransform == null)
        {
            Debug.LogWarning($"{name}: baseTransform is not assigned.");
            return;
        }

        _attackTarget = null;
        _nextAttackTime = 0f;
        ChangeState(DroneState.Return);

        Vector3 endPos = new Vector3(baseTransform.position.x, transform.position.y, baseTransform.position.z);
        _seeker.StartPath(transform.position, endPos, OnPathComplete);
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        RefreshOutline();
        SetSelectionIndicator(selected);
    }

    public void SetHovered(bool hovered)
    {
        _isHovered = hovered;
        RefreshOutline();
    }

    void ChangeState(DroneState newState)
    {
        _state = newState;
        if (newState == DroneState.Move || newState == DroneState.Return) return;

        _path = null;
        _reachedEnd = false;
    }

    void OnPathComplete(Path path)
    {
        if (path.error)
        {
            Debug.LogWarning($"{name}: A* path failed. {path.errorLog}");
            ChangeState(DroneState.Idle);
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

        Quaternion targetRot = Quaternion.LookRotation(flatDir, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRot,
            rotationSpeed * 100f * Time.deltaTime);
    }

    void SmoothTilt(Vector3 moveDirection)
    {
        if (bodyTransform == null) return;

        float bank = -moveDirection.x * maxTiltAngle;
        Quaternion correction = Quaternion.Euler(modelRotationOffset);
        Quaternion tilt = Quaternion.Euler(0f, 0f, bank);

        bodyTransform.localRotation = Quaternion.Slerp(
            bodyTransform.localRotation,
            correction * tilt,
            tiltSpeed * Time.deltaTime);
    }

    void ApplyColor(Color color)
    {
        foreach (Renderer renderer in _renderers)
        {
            Material mat = renderer.material;
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
                mat.color = color;
            }
            else if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }
        }
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
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, neighborRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, separationDistance);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
