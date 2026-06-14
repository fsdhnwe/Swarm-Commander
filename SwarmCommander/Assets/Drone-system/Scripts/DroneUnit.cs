using UnityEngine;
using Pathfinding;

/// <summary>
/// 無人機主控腳本。
/// 狀態：Idle（原地浮動）、Move（A* 移動 + 機頭朝向 + Boids）、Attack（朝向目標）、Return（返回基地 + Boids）
/// 模型機頭朝 Z 軸正方向，bodyTransform 負責模型修正旋轉與 banking 動畫。
/// </summary>
[RequireComponent(typeof(Seeker))]
[RequireComponent(typeof(Collider))]
public class DroneUnit : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════════
    //  狀態列舉
    // ══════════════════════════════════════════════════════════════
    public enum DroneState { Idle, Move, Attack, Return }

    // ══════════════════════════════════════════════════════════════
    //  Inspector 設定
    // ══════════════════════════════════════════════════════════════
    [Header("Selection Visual")]
    public GameObject selectionIndicator;
    public Color selectedColor = new Color(0f, 1f, 0.5f, 1f);
    public Color hoverColor = Color.white;
    public Color normalColor   = Color.white;

    [Header("Movement")]
    public float moveSpeed        = 8f;
    public float nextWaypointDist = 1f;
    public float arrivalDist      = 1.5f;

    [Header("Rotation")]
    public float rotationSpeed = 8f;

    [Header("Model Correction")]
    [Tooltip("如果模型面朝地面，調整這裡修正。常見值：X=-90（朝地）或 X=0（正常）")]
    public Vector3 modelRotationOffset = Vector3.zero;

    [Header("Idle Hover")]
    public float hoverAmplitude = 0.15f;
    public float hoverFrequency = 1.2f;

    [Header("Move Tilt")]
    public float maxTiltAngle = 18f;
    public float tiltSpeed    = 5f;

    [Header("Base / Return")]
    public Transform baseTransform;

    [Header("Visual Body")]
    [Tooltip("做傾斜動畫的子物件（若模型直接在根物件上則拖入自己）")]
    public Transform bodyTransform;

    [Header("Boids - Separation & Alignment")]
    [Tooltip("偵測周圍無人機的範圍")]
    public float neighborRadius = 4f;

    [Tooltip("Layer Mask：所有無人機都要設在這個 Layer 上才會互相偵測")]
    public LayerMask droneLayer;

    [Tooltip("分離力強度：越大越會互相推開")]
    public float separationWeight = 2.5f;

    [Tooltip("對齊力強度：越大越會同步飛行方向")]
    public float alignmentWeight = 1f;

    [Tooltip("分離力生效的最小距離（小於這個距離才會推開）")]
    public float separationDistance = 2.5f;

    [Tooltip("目標方向（seek）相對 Boids 的權重，越大越優先朝目標前進")]
    public float seekWeight = 2f;

    [Header("Attack")]
    public float attackRange = 18f;
    public float attackDamage = 50f;
    public float attackInterval = 1.5f;
    public float missileSpeed = 40f;
    public GameObject missilePrefab;
    public Transform missileLaunchPoint;


    // ══════════════════════════════════════════════════════════════
    //  內部狀態
    // ══════════════════════════════════════════════════════════════
    private DroneState _state = DroneState.Idle;
    private Seeker     _seeker;
    private Path       _path;
    private int        _waypointIndex;
    private bool       _reachedEnd;

    private Vector3  _hoverBasePosition; // Idle 浮動的基準點（每次到達目的地更新）
    private float    _hoverOffset;       // 隨機相位，避免所有無人機同步晃動
    private Renderer[] _renderers;
    private TargetableObject _attackTarget;
    private Outline _outline;
    private bool _isHovered;
    private float _nextAttackTime;

    // 給 Alignment 用：記錄自己上一幀的移動方向
    private Vector3 _currentMoveDir;

    // 重複利用的緩衝區，避免 GC
    private static readonly Collider[] _neighborBuffer = new Collider[32];

    public DroneState State    => _state;
    public bool       IsSelected { get; private set; }
    public Vector3    CurrentMoveDir => _currentMoveDir;

    // ══════════════════════════════════════════════════════════════
    //  Unity 生命週期
    // ══════════════════════════════════════════════════════════════
    void Awake()
    {
        _seeker            = GetComponent<Seeker>();
        _renderers         = GetComponentsInChildren<Renderer>();
        _hoverBasePosition = transform.position;
        _hoverOffset       = Random.Range(0f, Mathf.PI * 2f);
        _outline = GetComponentInChildren<Outline>();

        RefreshOutline();

        if (bodyTransform == null) bodyTransform = transform;

        // 套用模型初始旋轉修正
        if (bodyTransform != null)
        {
            bodyTransform.localRotation = Quaternion.Euler(modelRotationOffset);
        }

        SetSelectionIndicator(false);
        ApplyColor(normalColor);
    }

    void Update()
    {
        switch (_state)
        {
            case DroneState.Idle:   UpdateIdle();   break;
            case DroneState.Move:   UpdateMove();   break;
            case DroneState.Attack: UpdateAttack(); break;
            case DroneState.Return: UpdateMove();   break;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  狀態更新
    // ══════════════════════════════════════════════════════════════

    void UpdateIdle()
    {
        float newY = _hoverBasePosition.y +
                    Mathf.Sin(Time.time * hoverFrequency * Mathf.PI * 2f + _hoverOffset)
                    * hoverAmplitude;

        transform.position = new Vector3(
            _hoverBasePosition.x,
            newY,
            _hoverBasePosition.z);

        _currentMoveDir = Vector3.zero;
        SmoothTilt(Vector3.zero);
    }

    void UpdateMove()
    {
        if (_path == null || _reachedEnd) return;

        Vector3 destination = _path.vectorPath[_path.vectorPath.Count - 1];
        float distToEnd = Vector3.Distance(
            new Vector3(transform.position.x, 0, transform.position.z),
            new Vector3(destination.x, 0, destination.z));

        if (distToEnd <= arrivalDist)
        {
            _reachedEnd = true;
            // 更新浮動基準點為當前實際位置
            _hoverBasePosition = transform.position;
            _currentMoveDir = Vector3.zero;
            ChangeState(DroneState.Idle);
            return;
        }

        if (_waypointIndex >= _path.vectorPath.Count)
            _waypointIndex = _path.vectorPath.Count - 1;

        // 目標路徑點：保持無人機自己的 Y 高度，不跟著 graph 的 Y 走
        Vector3 waypoint = _path.vectorPath[_waypointIndex];
        waypoint.y = transform.position.y;

        Vector3 seekDir = (waypoint - transform.position).normalized;

        // ── Boids: Separation + Alignment ──
        // Falloff: as the drone nears its own final destination, reduce
        // Boids influence so Seek can dominate and the drone doesn't
        // oscillate/deadlock with neighbors fighting over the same spot.
        float boidsFalloff = Mathf.Clamp01(distToEnd / separationDistance);

        Vector3 separation = ComputeSeparation() * boidsFalloff;
        Vector3 alignment  = ComputeAlignment()  * boidsFalloff;

        Vector3 blendedDir = seekDir * seekWeight
                            + separation * separationWeight
                            + alignment  * alignmentWeight;

        if (blendedDir.sqrMagnitude < 0.0001f)
            blendedDir = seekDir;

        blendedDir.Normalize();
        _currentMoveDir = blendedDir;

        // 移動
        transform.position += blendedDir * moveSpeed * Time.deltaTime;

        // 機頭朝向（XZ 平面，朝實際移動方向，比較自然）
        Vector3 flatDir = new Vector3(blendedDir.x, 0f, blendedDir.z);

        if (flatDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(flatDir, Vector3.up);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRot,
                rotationSpeed * 100f * Time.deltaTime
            );
        }

        // 傾斜（用 seekDir，避免 Boids 偏移造成抖動）
        SmoothTilt(seekDir);

        // 切換到下一個路徑點（用原本的 waypoint 判斷，不受 Boids 偏移影響）
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

        Vector3 dir     = _attackTarget.transform.position - transform.position;
        Vector3 flatDir = new Vector3(dir.x, 0f, dir.z);
        float distance  = dir.magnitude;

        if (distance > attackRange)
        {
            Vector3 moveDir = dir.normalized;
            _currentMoveDir = moveDir;
            transform.position += moveDir * moveSpeed * Time.deltaTime;

            if (flatDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(flatDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }

            SmoothTilt(moveDir);
            return;
        }

        if (flatDir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(flatDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
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
            Debug.LogWarning($"{name}: missilePrefab 未設定，無法發射飛彈。");
            return;
        }

        Vector3 launchPos = missileLaunchPoint != null
            ? missileLaunchPoint.position
            : transform.position;

        GameObject m = Instantiate(missilePrefab, launchPos, transform.rotation);
        Missile missile = m.GetComponent<Missile>();
        if (missile != null)
        {
            missile.target = target;
            missile.damage = attackDamage;
            missile.speed  = missileSpeed;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Boids 計算
    // ══════════════════════════════════════════════════════════════

    /// <summary>分離力：遠離太近的鄰居，距離越近推力越強。</summary>
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
            {
                // 距離越近，推力越大（反比）
                result += toSelf.normalized * (separationDistance - dist) / separationDistance;
            }
        }

        result.y = 0f; // 只在水平面分離，避免影響高度
        return result;
    }

    /// <summary>對齊力：朝向附近友軍的平均移動方向。</summary>
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

            DroneUnit otherDrone = other.GetComponent<DroneUnit>();
            if (otherDrone == null) continue;
            if (otherDrone.State != DroneState.Move && otherDrone.State != DroneState.Return) continue;

            Vector3 otherDir = otherDrone.CurrentMoveDir;
            if (otherDir.sqrMagnitude < 0.0001f) continue;

            avgDir += otherDir;
            validCount++;
        }

        if (validCount == 0) return Vector3.zero;

        avgDir /= validCount;
        avgDir.y = 0f;
        return avgDir.normalized;
    }

    // ══════════════════════════════════════════════════════════════
    //  Public API
    // ══════════════════════════════════════════════════════════════

    public void MoveTo(Vector3 worldPosition)
    {
        _attackTarget = null;
        _nextAttackTime = 0f;

        // 強制讓 A* 在無人機當前高度尋路，避免 graph 高度不符
        Vector3 startPos = transform.position;
        Vector3 endPos   = new Vector3(worldPosition.x, transform.position.y, worldPosition.z);

        ChangeState(DroneState.Move);
        _seeker.StartPath(startPos, endPos, OnPathComplete);
    }

    public void AttackTarget(TargetableObject target)
    {
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
            Debug.LogWarning($"{name}: baseTransform 未設定，無法 Return。");
            return;
        }
        _attackTarget = null;
        _nextAttackTime = 0f;
        ChangeState(DroneState.Return);
        Vector3 endPos = new Vector3(
            baseTransform.position.x,
            transform.position.y,
            baseTransform.position.z);
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

    // ══════════════════════════════════════════════════════════════
    //  內部工具
    // ══════════════════════════════════════════════════════════════

    void ChangeState(DroneState newState)
    {
        _state = newState;
        if (newState != DroneState.Move && newState != DroneState.Return)
        {
            _path       = null;
            _reachedEnd = false;
        }
    }

    void OnPathComplete(Path p)
    {
        if (p.error)
        {
            Debug.LogWarning($"{name}: A* 路徑錯誤 → {p.errorLog}");

            // 路徑失敗時用直線移動作為 fallback
            ChangeState(DroneState.Idle);
            return;
        }
        _path          = p;
        _waypointIndex = 0;
        _reachedEnd    = false;
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
            tiltSpeed * Time.deltaTime
        );
    }

    void ApplyColor(Color color)
    {
        foreach (var r in _renderers)
        {
            Material mat = r.material;
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

    // 視覺化偵測範圍（Scene 視窗）
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
