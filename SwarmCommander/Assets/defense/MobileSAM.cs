using UnityEngine;
using System.Collections.Generic;

// Mobile SAM — 現實對應 Pantsir-S1（俄製近中程防空系統）
// 特性：
// - 車載，會在場景內沿路徑點巡邏移動
// - 雙武器系統：近距離用機砲(AAA)，中距離用飛彈(SAM)，自動依距離切換
// - 血量比固定 SAM 低（車輛比建築脆），但位置會變，難預測
// - 偵測到目標後停車開火，開火完畢繼續巡邏
//
// FSM：Patrol → Engage（停車開火）→ Patrol
// 武器切換：dist <= missileRange 都能打；dist <= gunRange 優先用機砲
//
// 第二關開始使用威脅評估選目標（ThreatEvaluator）
[RequireComponent(typeof(BuildingHealth))]
public class MobileSAM : MonoBehaviour
{
    public enum MobileSAMState { Patrol, Engage }

    [Header("巡邏路徑點 (依序移動，到最後一個後循環)")]
    public Transform[] patrolWaypoints;

    [Header("移動設定")]
    [Tooltip("巡邏速度（公尺/秒），現實 Pantsir 行進速度約 20m/s，遊戲建議 5~10）")]
    public float patrolSpeed = 6f;

    [Tooltip("抵達路徑點的判定距離")]
    public float waypointArrivalDist = 1.5f;

    [Tooltip("每個路徑點的停留時間隨機範圍（秒），模擬不完全規律的巡邏行為")]
    public Vector2 waypointPauseRange = new Vector2(1f, 4f);

    [Header("砲塔旋轉")]
    [Tooltip("砲塔子物件（只旋轉這個，車體不轉）")]
    public Transform turretPivot;

    [Tooltip("模型正面角度修正")]
    public float rotationOffsetY = 0f;

    [Header("飛彈武器 (中距離，現實對應 57E6 飛彈)")]
    [Tooltip("飛彈最大射程")]
    public float missileRange = 30f;
    [Tooltip("飛彈傷害")]
    public int missileDamage = 80;
    [Tooltip("飛彈射速（發/秒的冷卻，現實 Pantsir 約 2 發/秒）")]
    public float missileCooldown = 2f;
    public GameObject missilePrefab;
    public Transform missileFirePoint;

    [Header("機砲武器 (近距離，現實對應 2A38M 30mm 雙管機砲)")]
    [Tooltip("機砲最大射程（比飛彈近）")]
    public float gunRange = 12f;
    [Tooltip("機砲傷害（單發低，靠射速補）")]
    public int gunDamage = 20;
    [Tooltip("機砲射速冷卻（現實約 0.05 秒/發，遊戲建議 0.15~0.3）")]
    public float gunCooldown = 0.2f;
    public GameObject gunProjectilePrefab;
    public Transform gunFirePoint;

    [Header("偵測範圍")]
    public float detectionRange = 40f;

    [Header("威脅評估權重 (第二關開始生效)")]
    public ThreatEvaluator.ThreatWeights threatWeights = new ThreatEvaluator.ThreatWeights();

    [Header("狀態 (唯讀)")]
    public MobileSAMState currentState = MobileSAMState.Patrol;

    private BuildingHealth health;
    private int currentWaypointIndex = 0;
    private float waypointPauseTimer = 0f;
    private bool isPausing = false;

    private List<Transform> targetsInRange = new List<Transform>();
    private Transform currentTarget;

    private float missileTimer = 0f;
    private float gunTimer = 0f;

    void Awake()
    {
        health = GetComponent<BuildingHealth>();

        // 偵測 Trigger 放在子物件（避免 Scene 視窗誤選）
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

        MobileSAMDetectionZone zone = detectionZone.AddComponent<MobileSAMDetectionZone>();
        zone.Init(this);
    }

    void Update()
    {
        if (health.IsDestroyed) return;

        targetsInRange.RemoveAll(t => t == null || !t.gameObject.activeInHierarchy);
        missileTimer -= Time.deltaTime;
        gunTimer -= Time.deltaTime;

        switch (currentState)
        {
            case MobileSAMState.Patrol:
                UpdatePatrol();
                // 偵測到目標就切換到 Engage
                if (targetsInRange.Count > 0)
                {
                    currentTarget = ThreatEvaluator.FindHighestThreat(
                        transform.position, targetsInRange, threatWeights);
                    if (currentTarget != null)
                        currentState = MobileSAMState.Engage;
                }
                break;

            case MobileSAMState.Engage:
                if (currentTarget == null || !currentTarget.gameObject.activeInHierarchy)
                {
                    // 目標消失，重新評估或回到巡邏
                    currentTarget = ThreatEvaluator.FindHighestThreat(
                        transform.position, targetsInRange, threatWeights);
                    if (currentTarget == null)
                        currentState = MobileSAMState.Patrol;
                    break;
                }

                // 每秒重新評估是否有更高威脅目標
                currentTarget = ThreatEvaluator.FindHighestThreat(
                    transform.position, targetsInRange, threatWeights);

                RotateTurretTowards(currentTarget.position);
                UpdateWeapons();
                break;
        }
    }

    private void UpdatePatrol()
    {
        if (patrolWaypoints == null || patrolWaypoints.Length == 0) return;

        // 停留計時
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
        Vector3 direction = (targetPos - transform.position).normalized;
        float dist = Vector3.Distance(transform.position, targetPos);

        if (dist <= waypointArrivalDist)
        {
            // 抵達路徑點，隨機停留一段時間（擬真）
            isPausing = true;
            waypointPauseTimer = Random.Range(waypointPauseRange.x, waypointPauseRange.y);
            return;
        }

        transform.position += direction * patrolSpeed * Time.deltaTime;

        // 車體朝移動方向轉
        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot, 180f * Time.deltaTime);
        }
    }

    private void UpdateWeapons()
    {
        if (currentTarget == null) return;

        float dist = Vector3.Distance(transform.position, currentTarget.position);

        // 近距離優先用機砲
        if (dist <= gunRange && gunTimer <= 0f && gunProjectilePrefab != null)
        {
            FireGun();
            gunTimer = gunCooldown;
        }
        // 中距離用飛彈（機砲射程外）
        else if (dist <= missileRange && dist > gunRange && missileTimer <= 0f && missilePrefab != null)
        {
            FireMissile();
            missileTimer = missileCooldown;
        }
    }

    private void FireMissile()
    {
        Vector3 spawnPos = missileFirePoint != null ? missileFirePoint.position : transform.position;
        GameObject proj = Instantiate(missilePrefab, spawnPos, Quaternion.identity);
        SimpleProjectile sp = proj.GetComponent<SimpleProjectile>();
        if (sp != null)
        {
            sp.target = currentTarget;
            sp.speed = 25f;
            sp.damage = missileDamage;
            sp.isGuided = !RadarStation2.AllRadarsDestroyed(); // 雷達全滅時飛彈也失去導引
            sp.aimError = 0.5f;
        }
        Debug.Log($"[MobileSAM] {gameObject.name} 發射飛彈 → {currentTarget.name}");
    }

    private void FireGun()
    {
        Vector3 spawnPos = gunFirePoint != null ? gunFirePoint.position : transform.position;
        GameObject proj = Instantiate(gunProjectilePrefab, spawnPos, Quaternion.identity);
        SimpleProjectile sp = proj.GetComponent<SimpleProjectile>();
        if (sp != null)
        {
            sp.target = currentTarget;
            sp.speed = 60f;
            sp.damage = gunDamage;
            sp.isGuided = false;
            sp.aimError = 0.8f;
        }
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

    // 由子物件 MobileSAMDetectionZone 轉發
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
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, missileRange);
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(transform.position, gunRange);

        // 畫出巡邏路徑
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
public class MobileSAMDetectionZone : MonoBehaviour
{
    private MobileSAM sam;
    public void Init(MobileSAM parent) { sam = parent; }
    void OnTriggerEnter(Collider other) { if (sam != null) sam.OnDetectionTriggerEnter(other); }
    void OnTriggerExit(Collider other) { if (sam != null) sam.OnDetectionTriggerExit(other); }
}