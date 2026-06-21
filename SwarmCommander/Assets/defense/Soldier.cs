using UnityEngine;
using System.Collections.Generic;

// 士兵單位 — 防守方地面步兵
// 現實對應：一般步兵（步槍）或 MANPADS 兵（Stinger/Igla）
//
// 閒晃行為：以出生點為中心，在半徑內隨機選目標點走過去，
// 抵達後隨機停留一段時間，再選下一個點，模擬自然散步
// 速度和停留時間都有隨機範圍，讓多個士兵看起來各自不同
//
// 發現無人機後：停下來原地射擊，目標消失後繼續閒晃
//
// 武器：拖入 WeaponData，步槍或 MANPADS 靠資料檔切換
// 注意：士兵的 WeaponData 數值應比 SAM/AAA 弱，請自行設定
[RequireComponent(typeof(BuildingHealth))]
public class Soldier : MonoBehaviour
{
    public enum SoldierState { Wander, Engage }

    [Header("閒晃設定")]
    [Tooltip("閒晃半徑（公尺），以出生點為中心")]
    public float wanderRadius = 10f;

    [Tooltip("移動速度範圍（公尺/秒），每次選新目標時隨機取一個速度，模擬步伐快慢不一")]
    public Vector2 moveSpeedRange = new Vector2(1.5f, 3f);

    [Tooltip("抵達目標點後的停留時間範圍（秒），模擬士兵停下來張望/休息")]
    public Vector2 pauseTimeRange = new Vector2(2f, 6f);

    [Tooltip("抵達判定距離")]
    public float arrivalDist = 0.8f;

    [Header("貼地設定")]
    [Tooltip("士兵 pivot 到腳底的距離，避免穿地。注意：如果模型 pivot 在中心（例如測試用 Capsule/Cube），" +
             "這裡要填『實際 Scale × 0.5』才會對，不是隨便填一個小數字（MobileSAM 之前就是栽在這裡）")]
    public float groundOffset = 0f;

    [Header("壁障 (避開其他 Layer=Enemy 的防守物件)")]
    [Tooltip("避障偵測距離（公尺）：往移動方向前方偵測是否有障礙物擋路")]
    public float obstacleAvoidDistance = 3f;

    [Tooltip("避障偏轉強度：越大閃避時轉得越急，太大會繞圈、太小會撞不太到效果")]
    public float obstacleAvoidStrength = 1.5f;

    [Tooltip("障礙物所在的 Layer（防守物件的 Layer，請在這裡指定專案實際設定的 Enemy Layer）")]
    public LayerMask obstacleLayer;

    [Header("武器設定")]
    [Tooltip("拖入步槍或 MANPADS 的 WeaponData，數值自行設定（應比 SAM/AAA 弱）")]
    public WeaponData weaponData;

    [Tooltip("發射點，留空則從本體位置發射")]
    public Transform firePoint;

    [Header("偵測範圍")]
    public float detectionRange = 20f;

    [Header("威脅評估權重")]
    public ThreatEvaluator.ThreatWeights threatWeights = new ThreatEvaluator.ThreatWeights();

    [Header("狀態 (唯讀)")]
    public SoldierState currentState = SoldierState.Wander;

    private BuildingHealth health;
    private Vector3 spawnPoint;
    private Vector3 wanderTarget;
    private float currentMoveSpeed;
    private float pauseTimer = 0f;
    private bool isPausing = false;
    private float fireTimer = 0f;

    private List<Transform> targetsInRange = new List<Transform>();
    private Transform currentTarget;

    private int groundRaycastMask;

    void Awake()
    {
        health = GetComponent<BuildingHealth>();
        spawnPoint = transform.position;
        groundRaycastMask = Physics.DefaultRaycastLayers & ~(1 << gameObject.layer);

        // 偵測子物件
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

        SoldierDetectionZone zone = detectionZone.AddComponent<SoldierDetectionZone>();
        zone.Init(this);

        // 第一個閒晃目標
        PickNewWanderTarget();
    }

    void Update()
    {
        if (health.IsDestroyed) return;

        targetsInRange.RemoveAll(t => t == null || !t.gameObject.activeInHierarchy);
        fireTimer -= Time.deltaTime;

        // 有目標就切到 Engage，沒有就閒晃
        if (targetsInRange.Count > 0 && currentState == SoldierState.Wander)
        {
            currentTarget = ThreatEvaluator.FindHighestThreat(
                transform.position, targetsInRange, threatWeights);
            if (currentTarget != null)
                currentState = SoldierState.Engage;
        }
        else if (targetsInRange.Count == 0 && currentState == SoldierState.Engage)
        {
            currentTarget = null;
            currentState = SoldierState.Wander;
            PickNewWanderTarget();
        }

        switch (currentState)
        {
            case SoldierState.Wander:
                UpdateWander();
                break;

            case SoldierState.Engage:
                UpdateEngage();
                break;
        }

        SnapToGround();
    }

    private void UpdateWander()
    {
        if (isPausing)
        {
            pauseTimer -= Time.deltaTime;
            if (pauseTimer <= 0f)
            {
                isPausing = false;
                PickNewWanderTarget();
            }
            return;
        }

        Vector3 flatTarget = new Vector3(wanderTarget.x, transform.position.y, wanderTarget.z);
        float dist = Vector3.Distance(
            new Vector3(transform.position.x, 0, transform.position.z),
            new Vector3(wanderTarget.x, 0, wanderTarget.z));

        if (dist <= arrivalDist)
        {
            // 抵達，隨機停留
            isPausing = true;
            pauseTimer = Random.Range(pauseTimeRange.x, pauseTimeRange.y);
            return;
        }

        Vector3 dir = (flatTarget - transform.position).normalized;

        // 壁障：偵測移動方向前方是否有其他 Layer=Enemy 的防守物件擋路，閃避用
        dir = ApplyObstacleAvoidance(dir);

        transform.position += dir * currentMoveSpeed * Time.deltaTime;

        // 朝移動方向轉
        if (dir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot, 360f * Time.deltaTime);
        }
    }

    // 壁障：往移動方向前方丟一條 Raycast，撞到 obstacleLayer 裡的物件就往側邊閃開。
    // 自己也可能在同一個 Layer，所以額外用 IsChildOf 排除自己。
    private Vector3 ApplyObstacleAvoidance(Vector3 moveDir)
    {
        if (moveDir.sqrMagnitude < 0.0001f) return moveDir;

        RaycastHit hit;
        if (Physics.Raycast(transform.position, moveDir, out hit, obstacleAvoidDistance,
            obstacleLayer, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider.transform.IsChildOf(transform))
            {
                Vector3 avoidDir = Vector3.Cross(Vector3.up, hit.normal).normalized;

                if (Vector3.Dot(avoidDir, Vector3.Cross(Vector3.up, moveDir)) < 0f)
                    avoidDir = -avoidDir;

                float closeness = 1f - Mathf.Clamp01(hit.distance / obstacleAvoidDistance);
                moveDir = (moveDir + avoidDir * obstacleAvoidStrength * closeness).normalized;
            }
        }

        return moveDir;
    }

    private void UpdateEngage()
    {
        if (currentTarget == null || !currentTarget.gameObject.activeInHierarchy)
        {
            currentTarget = ThreatEvaluator.FindHighestThreat(
                transform.position, targetsInRange, threatWeights);
            if (currentTarget == null)
            {
                currentState = SoldierState.Wander;
                PickNewWanderTarget();
            }
            return;
        }

        // 面向目標
        Vector3 dir = (currentTarget.position - transform.position).normalized;
        if (dir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot, 360f * Time.deltaTime);
        }

        // 開火
        if (fireTimer <= 0f && weaponData != null)
        {
            FireAtTarget();
            fireTimer = weaponData.fireRate;
        }
    }

    private void FireAtTarget()
    {
        if (weaponData == null || weaponData.projectilePrefab == null || currentTarget == null) return;

        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
        GameObject proj = Instantiate(weaponData.projectilePrefab, spawnPos, Quaternion.identity);
        SimpleProjectile sp = proj.GetComponent<SimpleProjectile>();
        if (sp != null)
        {
            sp.target = currentTarget;
            sp.speed = weaponData.projectileSpeed;
            sp.damage = weaponData.damage;
            sp.isGuided = weaponData.isGuided && !RadarStation2.AllRadarsDestroyed();
            sp.aimError = GetAimError();
        }

        Debug.Log($"[士兵] {gameObject.name} ({weaponData.weaponName}) 對 {currentTarget.name} 開火");
    }

    private float GetAimError()
    {
        float comms = CommandNetwork.Instance != null ? CommandNetwork.Instance.commsIntegrity : 1f;
        float worstCase = weaponData.baseAimError * 5f;
        float baseError = Mathf.Lerp(worstCase, weaponData.baseAimError, comms);
        if (currentTarget != null && RadarStation2.IsDroneDetected(currentTarget))
            baseError *= RadarStation2.GetAimErrorMultiplier();
        return baseError;
    }

    private void PickNewWanderTarget()
    {
        // 在出生點半徑內隨機選一個點
        Vector2 randomCircle = Random.insideUnitCircle * wanderRadius;
        wanderTarget = new Vector3(
            spawnPoint.x + randomCircle.x,
            transform.position.y,
            spawnPoint.z + randomCircle.y);

        // 每次選新目標也隨機換一個步行速度
        currentMoveSpeed = Random.Range(moveSpeedRange.x, moveSpeedRange.y);
    }

    private void SnapToGround()
    {
        RaycastHit hit;
        Vector3 rayOrigin = new Vector3(transform.position.x, transform.position.y + 50f, transform.position.z);
        if (Physics.Raycast(rayOrigin, Vector3.down, out hit, 200f,
            groundRaycastMask, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider.transform.IsChildOf(transform))
            {
                transform.position = new Vector3(
                    transform.position.x,
                    hit.point.y + groundOffset,
                    transform.position.z);
            }
        }
        else
        {
            // 診斷用：跟 MobileSAM 同樣的邏輯。如果這行一直出現，代表 Raycast
            // 根本沒打到任何東西，最常見原因：地形沒掛 Collider、Collider 被關掉，
            // 或地形剛好跟本物件同一個 Layer（會被 groundRaycastMask 一起排除掉）。
            Debug.LogWarning($"[Soldier] {gameObject.name} 貼地 Raycast 沒打到任何東西！" +
                $"請檢查地形是否有掛 Collider，以及地形 Layer 是否跟本物件（{LayerMask.LayerToName(gameObject.layer)}）相同（位置：{transform.position}）");
        }
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
        // 閒晃範圍（青色）
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        Gizmos.DrawWireSphere(Application.isPlaying ? spawnPoint : transform.position, wanderRadius);

        // 偵測範圍（黃色）
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}

// 子物件轉發腳本
public class SoldierDetectionZone : MonoBehaviour
{
    private Soldier soldier;
    public void Init(Soldier parent) { soldier = parent; }
    void OnTriggerEnter(Collider other) { if (soldier != null) soldier.OnDetectionTriggerEnter(other); }
    void OnTriggerExit(Collider other) { if (soldier != null) soldier.OnDetectionTriggerExit(other); }
}