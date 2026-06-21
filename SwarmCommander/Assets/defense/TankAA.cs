using UnityEngine;
using System.Collections.Generic;

// 防空坦克 — 現實對應 Flakpanzer Gepard（德製履帶式防空坦克）
// 特性：
// - 履帶車載，沿路徑點巡邏（比 MobileSAM 慢、比固定 AAA 靈活）
// - 純機砲武裝（雙管 35mm），高射速、近距離壓制無人機
// - 血量比 MobileSAM 高（坦克裝甲比輪式車厚），但移動更慢
// - 比固定 AAA 更耐打，是近距離防空的最後屏障
//
// 武器：拖入 WeaponData（建議使用跟 AAA 相同或稍強的資料檔）
// 巡邏邏輯：跟 MobileSAM 相同，含貼地修正
[RequireComponent(typeof(BuildingHealth))]
public class TankAA : MonoBehaviour
{
    public enum TankAAState { Patrol, Engage }

    [Header("巡邏路徑點 (依序移動，到最後一個後循環)")]
    public Transform[] patrolWaypoints;

    [Header("移動設定")]
    [Tooltip("巡邏速度（公尺/秒），Gepard 行進速度約 10m/s，遊戲建議 3~5，比 MobileSAM 慢）")]
    public float patrolSpeed = 3f;

    [Tooltip("抵達路徑點的判定距離")]
    public float waypointArrivalDist = 1.5f;

    [Tooltip("每個路徑點的停留時間範圍（秒）")]
    public Vector2 waypointPauseRange = new Vector2(1f, 3f);

    [Tooltip("車體轉向速度（度/秒），坦克轉向比輪式慢，建議 60~90）")]
    public float rotationSpeed = 90f;

    [Header("貼地設定")]
    [Tooltip("坦克 pivot 到底部距離，避免穿地")]
    public float groundOffset = 0.5f;

    [Header("砲塔旋轉")]
    [Tooltip("砲塔子物件（只旋轉砲塔，車體不跟著轉）")]
    public Transform turretPivot;

    [Tooltip("模型正面角度修正")]
    public float rotationOffsetY = 0f;

    [Header("機砲武器 (現實對應 Oerlikon KDA 35mm 雙管機砲)")]
    [Tooltip("拖入 WeaponData，建議跟 AAA 相同或稍強的資料檔")]
    public WeaponData gunData;

    [Tooltip("發射點，留空則從車體位置發射")]
    public Transform firePoint;

    [Header("偵測範圍")]
    public float detectionRange = 25f;

    [Header("威脅評估權重")]
    public ThreatEvaluator.ThreatWeights threatWeights = new ThreatEvaluator.ThreatWeights();

    [Header("狀態 (唯讀)")]
    public TankAAState currentState = TankAAState.Patrol;

    private BuildingHealth health;
    private int currentWaypointIndex = 0;
    private float waypointPauseTimer = 0f;
    private bool isPausing = false;
    private float fireTimer = 0f;

    private List<Transform> targetsInRange = new List<Transform>();
    private Transform currentTarget;

    private int groundRaycastMask;

    void Awake()
    {
        health = GetComponent<BuildingHealth>();
        groundRaycastMask = Physics.DefaultRaycastLayers & ~(1 << gameObject.layer);

        GameObject detectionZone = new GameObject("DetectionZone");
        detectionZone.transform.SetParent(transform, false);
        detectionZone.transform.localPosition = Vector3.zero;

        SphereCollider col = detectionZone.AddComponent<SphereCollider>();
        col.isTrigger = true;
        float maxScale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
        col.radius = maxScale > 0f ? detectionRange / maxScale : detectionRange;

        Rigidbody rb = detectionZone.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        TankAADetectionZone zone = detectionZone.AddComponent<TankAADetectionZone>();
        zone.Init(this);
    }

    void Update()
    {
        if (health.IsDestroyed) return;

        targetsInRange.RemoveAll(t => t == null || !t.gameObject.activeInHierarchy);
        fireTimer -= Time.deltaTime;

        if (currentState == TankAAState.Engage && targetsInRange.Count == 0)
        {
            currentState = TankAAState.Patrol;
            currentTarget = null;
        }

        switch (currentState)
        {
            case TankAAState.Patrol:
                UpdatePatrol();
                if (targetsInRange.Count > 0)
                {
                    currentTarget = ThreatEvaluator.FindHighestThreat(
                        transform.position, targetsInRange, threatWeights);
                    if (currentTarget != null)
                        currentState = TankAAState.Engage;
                }
                break;

            case TankAAState.Engage:
                if (currentTarget == null || !currentTarget.gameObject.activeInHierarchy)
                {
                    currentTarget = ThreatEvaluator.FindHighestThreat(
                        transform.position, targetsInRange, threatWeights);
                    if (currentTarget == null)
                        currentState = TankAAState.Patrol;
                    break;
                }

                currentTarget = ThreatEvaluator.FindHighestThreat(
                    transform.position, targetsInRange, threatWeights);

                RotateTurretTowards(currentTarget.position);
                FireAtTarget();
                break;
        }
    }

    private void UpdatePatrol()
    {
        if (patrolWaypoints == null || patrolWaypoints.Length == 0) return;

        if (isPausing)
        {
            waypointPauseTimer -= Time.deltaTime;
            if (waypointPauseTimer <= 0f)
            {
                isPausing = false;
                currentWaypointIndex = (currentWaypointIndex + 1) % patrolWaypoints.Length;
            }
            return;
        }

        Transform waypoint = patrolWaypoints[currentWaypointIndex];
        if (waypoint == null) return;

        Vector3 targetPos = new Vector3(waypoint.position.x, transform.position.y, waypoint.position.z);
        float dist = Vector3.Distance(
            new Vector3(transform.position.x, 0, transform.position.z),
            new Vector3(targetPos.x, 0, targetPos.z));

        if (dist <= waypointArrivalDist)
        {
            isPausing = true;
            waypointPauseTimer = Random.Range(waypointPauseRange.x, waypointPauseRange.y);
            return;
        }

        Vector3 direction = (targetPos - transform.position).normalized;
        transform.position += direction * patrolSpeed * Time.deltaTime;

        // 貼地
        RaycastHit groundHit;
        Vector3 rayOrigin = new Vector3(transform.position.x, transform.position.y + 50f, transform.position.z);
        if (Physics.Raycast(rayOrigin, Vector3.down, out groundHit, 200f,
            groundRaycastMask, QueryTriggerInteraction.Ignore))
        {
            if (!groundHit.collider.transform.IsChildOf(transform))
            {
                transform.position = new Vector3(
                    transform.position.x,
                    groundHit.point.y + groundOffset,
                    transform.position.z);
            }
        }

        // 車體朝移動方向轉（坦克轉向較慢）
        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
    }

    private void FireAtTarget()
    {
        if (gunData == null || gunData.projectilePrefab == null) return;
        if (fireTimer > 0f) return;

        float dist = Vector3.Distance(transform.position, currentTarget.position);
        if (dist > gunData.fireRange) return;

        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
        GameObject proj = Instantiate(gunData.projectilePrefab, spawnPos, Quaternion.identity);
        SimpleProjectile sp = proj.GetComponent<SimpleProjectile>();
        if (sp != null)
        {
            sp.target = currentTarget;
            sp.speed = gunData.projectileSpeed;
            sp.damage = gunData.damage;
            sp.isGuided = gunData.isGuided;
            sp.aimError = GetAimError();
        }

        fireTimer = gunData.fireRate;
        Debug.Log($"[防空坦克] {gameObject.name} ({gunData.weaponName}) 對 {currentTarget.name} 開火");
    }

    private float GetAimError()
    {
        float comms = CommandNetwork.Instance != null ? CommandNetwork.Instance.commsIntegrity : 1f;
        float worstCase = gunData.baseAimError * 5f;
        float baseError = Mathf.Lerp(worstCase, gunData.baseAimError, comms);
        if (currentTarget != null && RadarStation2.IsDroneDetected(currentTarget))
            baseError *= RadarStation2.GetAimErrorMultiplier();
        return baseError;
    }

    private void RotateTurretTowards(Vector3 targetPos)
    {
        Transform pivot = turretPivot != null ? turretPivot : transform;
        Vector3 direction = targetPos - pivot.position;
        if (direction.sqrMagnitude < 0.001f) return;

        Quaternion targetRot = Quaternion.LookRotation(direction);
        targetRot *= Quaternion.Euler(0, rotationOffsetY, 0);
        pivot.rotation = Quaternion.RotateTowards(pivot.rotation, targetRot, 200f * Time.deltaTime);
    }

    public void OnDetectionTriggerEnter(Collider other)
    {
        if (!IsPlayerDrone(other)) return;
        Transform t = GetDroneTransform(other);
        if (t != null && !targetsInRange.Contains(t))
            targetsInRange.Add(t);
    }

    public void OnDetectionTriggerExit(Collider other)
    {
        if (!IsPlayerDrone(other)) return;
        Transform t = GetDroneTransform(other);
        if (t != null) targetsInRange.Remove(t);
    }

    private bool IsPlayerDrone(Collider other)
    {
        return other.CompareTag("PlayerDrone") ||
               (other.transform.root != null && other.transform.root.CompareTag("PlayerDrone")) ||
               other.GetComponentInParent<DroneHealth>() != null;
    }

    private Transform GetDroneTransform(Collider other)
    {
        DroneHealth dh = other.GetComponentInParent<DroneHealth>();
        return dh != null ? dh.transform : other.transform;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        if (gunData != null)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(transform.position, gunData.fireRange);
        }

        if (patrolWaypoints == null || patrolWaypoints.Length < 2) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < patrolWaypoints.Length; i++)
        {
            if (patrolWaypoints[i] == null) continue;
            int next = (i + 1) % patrolWaypoints.Length;
            if (patrolWaypoints[next] == null) continue;
            Gizmos.DrawLine(patrolWaypoints[i].position, patrolWaypoints[next].position);
        }
    }
}

// 子物件轉發腳本
public class TankAADetectionZone : MonoBehaviour
{
    private TankAA tank;
    public void Init(TankAA parent) { tank = parent; }
    void OnTriggerEnter(Collider other) { if (tank != null) tank.OnDetectionTriggerEnter(other); }
    void OnTriggerExit(Collider other) { if (tank != null) tank.OnDetectionTriggerExit(other); }
}
