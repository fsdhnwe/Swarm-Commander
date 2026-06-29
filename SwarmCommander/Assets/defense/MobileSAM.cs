using System.Collections.Generic;
using UnityEngine;
using static ThreatEvaluator;

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
//
// 2026/06 修正：巡邏移動時吃地板 bug
// 原因：UpdatePatrol() 裡用來「排除打到自己」的判斷式
//     groundHit.collider.transform.root != transform
// 只有在本腳本掛在整個物件階層「最上層」時才正確。只要車輛 Collider 掛在子物件、
// 而本腳本所在物件本身又不是階層最上層（外面多包一層容器/父物件），這個判斷式
// 永遠是 true，等於完全沒排除自己，導致 Raycast 偶爾打到車輛自己的底盤/履帶
// Collider，把錯誤的高度當地面貼上去，造成移動時吃地板。
// 解法：改用 Transform.IsChildOf()，不管階層怎麼包都能正確排除「自己與自己的子物件」。
//
// 2026/06 修正：壁障判斷原本誤用 Tag=Enemy，實際專案是用 Layer=Enemy 來標記防守物件，
// 已改成用 obstacleLayer（LayerMask）配合 Physics.Raycast 的 layerMask 參數判斷，
// 不再檢查 CompareTag("Enemy")。其餘邏輯（含貼地修正）完全未變動。
//
// 2026/06 修正：飛彈性能改成跟固定式 SAM 共用同一份 WeaponData。
// 原本 missileRange/missileDamage/missileCooldown/missilePrefab 是本腳本自己獨立的欄位，
// FireMissile() 內 speed/aimError 還是寫死數字(25f / 0.5f)，導致 Mobile SAM 的飛彈
// 跟固定式 SAM(DefenseInterceptor + WeaponData)性能對不起來。
// 改成 public WeaponData missileData，直接把固定式 SAM 用的 WeaponData 資產
// (例如 SAM_Data) 拖進來，所有飛彈相關數值(detectionRange/fireRange/fireRate/damage/
// projectileSpeed/projectilePrefab/isGuided/reactionDelayBase/baseAimError)
// 完全共用同一份資料，瞄準誤差的通訊完整度/雷達加成計算方式
// (GetMissileAimError())也跟 DefenseInterceptor.GetAimError() 邏輯一致。
// 注意：missileData.detectionRange 和 reactionDelayBase 目前 Mobile SAM 沒有使用
// （Mobile SAM 用自己的 detectionRange 欄位、且 Engage 時沒有反應延遲機制），
// 純粹是因為共用同一份 WeaponData 資產而存在於資料裡，不影響行為。
//
// 2026/06 修正：機砲性能同樣改成跟固定式 AAA 共用同一份 WeaponData。
// 原本 gunRange/gunDamage/gunCooldown/gunProjectilePrefab 是獨立欄位，
// FireGun() 內 speed/aimError/isGuided 也是寫死數字。
// 改成 public WeaponData gunData，把固定式 AAA 用的 WeaponData 資產
// (例如 AAA_Data) 拖進來，機砲所有數值完全共用同一份資料。
// 至此飛彈(missileData)和機砲(gunData)都改為共用 WeaponData，
// Mobile SAM 不再有任何自己獨立寫死的武器數值欄位。
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

    [Tooltip("貼地高度偏移：車輛 pivot 到底部的距離（公尺），避免穿地，依模型調整")]
    public float groundOffset = 3f;

    [Header("壁障 (避開其他 Layer=Enemy 的防守物件)")]
    [Tooltip("避障偵測距離（公尺）：往移動方向前方偵測是否有障礙物擋路")]
    public float obstacleAvoidDistance = 6f;

    [Tooltip("避障偏轉強度：越大閃避時轉得越急，太大會繞圈、太小會撞不太到效果")]
    public float obstacleAvoidStrength = 1.5f;

    [Tooltip("障礙物所在的 Layer（防守物件的 Layer，請在這裡指定專案實際設定的 Enemy Layer）")]
    public LayerMask obstacleLayer;

    [Header("砲塔旋轉")]
    [Tooltip("砲塔子物件（只旋轉這個，車體不轉）")]
    public Transform turretPivot;

    [Tooltip("模型正面角度修正")]
    public float rotationOffsetY = 0f;

    [Header("飛彈武器 (中距離，現實對應 57E6 飛彈)")]
    [Tooltip("把固定式 SAM 用的同一份 WeaponData 拖進來，飛彈性能(射程/傷害/冷卻/導引/" +
             "反應延遲/瞄準誤差/外觀Prefab等全部欄位)會跟固定式 SAM 完全一致，" +
             "不再使用本腳本自己的射程/傷害/冷卻寫死值")]
    public WeaponData missileData;

    [Tooltip("飛彈發射點，留空則從車體本身位置發射")]
    public Transform missileFirePoint;

    [Header("機砲武器 (近距離，現實對應 2A38M 30mm 雙管機砲)")]
    [Tooltip("把固定式 AAA 用的同一份 WeaponData 拖進來，機砲性能(射程/傷害/冷卻/" +
             "非導引/瞄準誤差/外觀Prefab等全部欄位)會跟固定式 AAA 完全一致")]
    public WeaponData gunData;

    [Tooltip("機砲發射點，留空則從車體本身位置發射")]
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

    // 貼地 Raycast 專用的 LayerMask：在 Physics.DefaultRaycastLayers 的基礎上，
    // 再排除「自己所在的 Layer」。
    // 2026/06 修正：原本靠事後比對 Transform 排除自己，但車體本身的 Box Collider
    // （Scale 6 時頂面在 pivot 上方 3 公尺）會直接擋在 Raycast 路徑最前面，先被打中。
    // 程式判斷出「這是自己」之後就直接放棄那一幀，完全沒有繼續往下找地形，
    // 導致貼地校正形同虛設、車輛 Y 軸完全沒被修正過，巡邏經過地形隆起處就吃進去。
    // 改成在查詢階段直接用 LayerMask 排除自己的 Layer，物理引擎根本不會回傳
    // 自己的 Collider，從源頭解決，不再需要事後判斷。
    private int groundRaycastMask;

    private List<Transform> targetsInRange = new List<Transform>();
    private Transform currentTarget;

    private float missileTimer = 0f;
    private float gunTimer = 0f;

    void Awake()
    {
        health = GetComponent<BuildingHealth>();

        groundRaycastMask = Physics.DefaultRaycastLayers & ~(1 << gameObject.layer);

        // 偵測 Trigger 放在子物件（避免 Scene 視窗誤選）
        GameObject detectionZone = new GameObject("DetectionZone");
        int detectionLayer = LayerMask.NameToLayer("DetectionZone");
        if (detectionLayer >= 0)
            detectionZone.layer = detectionLayer;
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

        // 貼地修正：跟著巡邏移動一起處理（見 UpdatePatrol() 內的 Raycast 校正）

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

                // ← 修正：FindHighestThreat 可能回傳 null（範圍內目標全消失），
                //   加 null 檢查避免 NullReferenceException
                if (currentTarget == null)
                {
                    currentState = MobileSAMState.Patrol;
                    break;
                }

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

        // 壁障：偵測移動方向前方是否有其他 Layer=Enemy 的防守物件擋路，閃避用
        direction = ApplyObstacleAvoidance(direction);

        transform.position += direction * patrolSpeed * Time.deltaTime;

        // 貼地：從固定高空往下 Raycast，不依賴 transform.position.y 避免車子已在地下時射線打不到地
        RaycastHit groundHit;
        Vector3 rayOrigin = new Vector3(transform.position.x, transform.position.y + 50f, transform.position.z);
        if (Physics.Raycast(rayOrigin, Vector3.down, out groundHit, 200f,
            groundRaycastMask, QueryTriggerInteraction.Ignore))
        {
            // 保留這層判斷當作雙重保險（例如場景裡剛好有別的物件跟自己同一個
            // Layer 又疊在自己正下方的極端情況），但主要的排除工作已經由
            // groundRaycastMask 在查詢階段就做掉了。
            if (!groundHit.collider.transform.IsChildOf(transform))
            {
                transform.position = new Vector3(
                    transform.position.x,
                    groundHit.point.y + groundOffset,
                    transform.position.z);
            }
        }
        else
        {
            // 診斷用：如果這行一直出現，代表 Raycast 根本沒打到任何東西，
            // 最常見原因：地形沒掛 Collider、Collider 被關掉，或地形剛好跟自己同一個 Layer
            // （這種情況下會被 groundRaycastMask 一起排除掉，需把地形換一個 Layer）。
            Debug.LogWarning($"[MobileSAM] {gameObject.name} 貼地 Raycast 沒打到任何東西！" +
                $"請檢查地形是否有掛 Collider，以及地形 Layer 是否跟本物件（{LayerMask.LayerToName(gameObject.layer)}）相同（位置：{transform.position}）");
        }

        // 車體朝移動方向轉
        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot, 180f * Time.deltaTime);
        }
    }

    // 壁障：往移動方向前方丟一條 Raycast，撞到 obstacleLayer 裡的物件就往側邊閃開。
    // 自己也可能在同一個 Layer，所以額外用 IsChildOf 排除自己（含自己的子物件，例如 DetectionZone）。
    private Vector3 ApplyObstacleAvoidance(Vector3 moveDir)
    {
        if (moveDir.sqrMagnitude < 0.0001f) return moveDir;

        RaycastHit hit;
        if (Physics.Raycast(transform.position, moveDir, out hit, obstacleAvoidDistance,
            obstacleLayer, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider.transform.IsChildOf(transform))
            {
                // 用障礙物表面法線在水平面上的分量算出一個側向閃避方向
                Vector3 avoidDir = Vector3.Cross(Vector3.up, hit.normal).normalized;

                // 確保閃避方向跟原本前進方向同一側（避免每幀左右抖動換邊）
                if (Vector3.Dot(avoidDir, Vector3.Cross(Vector3.up, moveDir)) < 0f)
                    avoidDir = -avoidDir;

                // 障礙物越近，閃避權重越大
                float closeness = 1f - Mathf.Clamp01(hit.distance / obstacleAvoidDistance);
                moveDir = (moveDir + avoidDir * obstacleAvoidStrength * closeness).normalized;
            }
        }

        return moveDir;
    }

    private void UpdateWeapons()
    {
        if (currentTarget == null) return;
        if (missileData == null && gunData == null)
        {
            Debug.LogWarning($"[MobileSAM] {gameObject.name} 沒有設定 Missile Data 或 Gun Data，無法開火");
            return;
        }

        float dist = Vector3.Distance(transform.position, currentTarget.position);

        // 近距離優先用機砲（射程取自 gunData.fireRange，跟固定式 AAA 一致）
        if (gunData != null && dist <= gunData.fireRange && gunTimer <= 0f && gunData.projectilePrefab != null)
        {
            FireGun();
            gunTimer = gunData.fireRate;
        }
        // 中距離用飛彈（機砲射程外，射程取自 missileData.fireRange，跟固定式 SAM 一致）
        else if (missileData != null && dist <= missileData.fireRange &&
                 (gunData == null || dist > gunData.fireRange) &&
                 missileTimer <= 0f && missileData.projectilePrefab != null)
        {
            FireMissile();
            missileTimer = missileData.fireRate;
        }
    }

    // 飛彈性能完全取自 missileData（跟固定式 SAM 共用同一份 WeaponData），
    // 包含 isGuided、projectileSpeed、damage、baseAimError 等欄位都直接套用，
    // 瞄準誤差的通訊完整度加成計算方式也跟 DefenseInterceptor.GetAimError() 一致
    private void FireMissile()
    {
        Vector3 spawnPos = missileFirePoint != null ? missileFirePoint.position : transform.position;
        GameObject proj = Instantiate(missileData.projectilePrefab, spawnPos, Quaternion.identity);
        SimpleProjectile sp = proj.GetComponent<SimpleProjectile>();
        if (sp != null)
        {
            sp.target = currentTarget;
            sp.speed = missileData.projectileSpeed;
            sp.damage = missileData.damage;
            // isGuided 跟固定式 SAM 一樣的邏輯：本身設定 + 雷達全滅時強制失去導引
            sp.isGuided = missileData.isGuided && !RadarStation2.AllRadarsDestroyed();
            sp.aimError = GetMissileAimError();
        }
        Debug.Log($"[MobileSAM] {gameObject.name} ({missileData.weaponName}) 發射飛彈 → {currentTarget.name}");
    }

    // 跟 DefenseInterceptor.GetAimError() 同樣的計算方式：
    // 通訊完整度越低，誤差越大(最多5倍)；目標被雷達鎖定時，誤差套用降低倍率
    private float GetMissileAimError()
    {
        float comms = CommandNetwork.Instance != null ? CommandNetwork.Instance.commsIntegrity : 1f;
        float worstCase = missileData.baseAimError * 5f;
        float baseError = Mathf.Lerp(worstCase, missileData.baseAimError, comms);
        if (currentTarget != null && RadarStation2.IsDroneDetected(currentTarget))
            baseError *= RadarStation2.GetAimErrorMultiplier();
        return baseError;
    }

    // 機砲性能完全取自 gunData（跟固定式 AAA 共用同一份 WeaponData）
    private void FireGun()
    {
        Vector3 spawnPos = gunFirePoint != null ? gunFirePoint.position : transform.position;
        GameObject proj = Instantiate(gunData.projectilePrefab, spawnPos, Quaternion.identity);
        SimpleProjectile sp = proj.GetComponent<SimpleProjectile>();
        if (sp != null)
        {
            sp.target = currentTarget;
            sp.speed = gunData.projectileSpeed;
            sp.damage = gunData.damage;
            sp.isGuided = gunData.isGuided; // AAA 通常是 false，沿用 gunData 設定，不寫死
            sp.aimError = GetGunAimError();
        }
        Debug.Log($"[MobileSAM] {gameObject.name} ({gunData.weaponName}) 機砲開火 → {currentTarget.name}");
    }

    // 跟 DefenseInterceptor.GetAimError() 同樣的計算方式，用在機砲上
    private float GetGunAimError()
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

        if (missileData != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, missileData.fireRange);
        }

        if (gunData != null)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(transform.position, gunData.fireRange);
        }

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