using UnityEngine;
using System.Collections.Generic;

// SAM 和 AAA 共用這一份腳本，差異只來自 weaponData (WeaponData.cs) 裡的數值
// FSM 流程：Idle -> Detect -> Track -> (反應延遲) -> Fire -> Cooldown
//
// 通訊完整度 (CommandNetwork.commsIntegrity) 會影響：
// - 反應延遲：通訊越差，從鎖定到開火要等更久
// - 瞄準誤差：通訊越差，子彈/飛彈會偏離目標越多
[RequireComponent(typeof(BuildingHealth))]
public class DefenseInterceptor : MonoBehaviour
{
    public enum InterceptorState { Idle, Detect, Track, Fire, Cooldown }

    [Header("武器參數 (把 SAM 或 AAA 的 WeaponData 拖到這裡)")]
    public WeaponData weaponData;

    [Header("威脅評估 (第二關起啟用)")]
    [Tooltip("勾選後改用威脅評估選目標，不勾則維持原本選最近目標的邏輯")]
    public bool useThreatEvaluation = false;
    public ThreatEvaluator.ThreatWeights threatWeights = new ThreatEvaluator.ThreatWeights();

    [Header("砲塔旋轉物件 (把會轉動的子物件拖進來；留空則轉動整個建築)")]
    public Transform turretPivot;

    [Header("模型正面角度修正 (Y軸偏移，先試 0，歪了再調 90 或 -90 或 180)")]
    public float rotationOffsetY = 90f;

    [Header("發射點 (可留空，留空則從本體位置發射)")]
    public Transform firePoint;

    [Header("狀態 (FSM，唯讀，Play 時可即時觀察)")]
    public InterceptorState currentState = InterceptorState.Idle;

    private BuildingHealth health;
    private float cooldownTimer = 0f;
    private float reactionTimer = -1f;
    private List<Transform> targetsInRange = new List<Transform>();
    private Transform currentTarget;

    void Awake()
    {
        health = GetComponent<BuildingHealth>();

        SphereCollider detectionTrigger = gameObject.AddComponent<SphereCollider>();
        detectionTrigger.isTrigger = true;
        detectionTrigger.radius = weaponData != null ? weaponData.detectionRange : 30f;
    }

    void Update()
    {
        if (weaponData == null) return;

        if (health.IsDestroyed)
        {
            currentState = InterceptorState.Idle;
            return;
        }

        targetsInRange.RemoveAll(t => t == null || !t.gameObject.activeInHierarchy);

        switch (currentState)
        {
            case InterceptorState.Idle:
                if (targetsInRange.Count > 0)
                    currentState = InterceptorState.Detect;
                break;

            case InterceptorState.Detect:
                currentTarget = useThreatEvaluation
                    ? ThreatEvaluator.FindHighestThreat(transform.position, targetsInRange, threatWeights)
                    : FindClosestTarget();
                if (currentTarget != null)
                {
                    currentState = InterceptorState.Track;
                    reactionTimer = -1f;
                }
                else
                {
                    currentState = InterceptorState.Idle;
                }
                break;

            case InterceptorState.Track:
                if (currentTarget == null)
                {
                    currentState = InterceptorState.Idle;
                    reactionTimer = -1f;
                    break;
                }

                if (Vector3.Distance(transform.position, currentTarget.position) > weaponData.detectionRange)
                {
                    currentTarget = null;
                    reactionTimer = -1f;
                    currentState = InterceptorState.Idle;
                    break;
                }

                float dist = Vector3.Distance(transform.position, currentTarget.position);
                // 雷達鎖定目標時，射程套用加成倍率
                float effectiveFireRange = weaponData.fireRange *
                    (RadarStation2.IsDroneDetected(currentTarget) ? RadarStation2.GetFireRangeBonus() : 1f);
                if (dist <= effectiveFireRange)
                {
                    RotateTowards(currentTarget.position);

                    if (reactionTimer < 0f)
                        reactionTimer = GetReactionDelay();

                    reactionTimer -= Time.deltaTime;
                    if (reactionTimer <= 0f)
                    {
                        currentState = InterceptorState.Fire;
                        reactionTimer = -1f;
                    }
                }
                else
                {
                    reactionTimer = -1f;
                }
                break;

            case InterceptorState.Fire:
                FireAtTarget();
                cooldownTimer = weaponData.fireRate;
                currentState = InterceptorState.Cooldown;
                break;

            case InterceptorState.Cooldown:
                cooldownTimer -= Time.deltaTime;
                if (cooldownTimer <= 0f)
                {
                    currentState = (targetsInRange.Count > 0)
                        ? InterceptorState.Detect
                        : InterceptorState.Idle;
                }
                break;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsPlayerDrone(other))
        {
            Transform target = GetDroneTargetTransform(other);
            if (target != null && !targetsInRange.Contains(target))
                targetsInRange.Add(target);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (IsPlayerDrone(other))
        {
            Transform target = GetDroneTargetTransform(other);
            if (target != null)
                targetsInRange.Remove(target);
        }
    }

    private Transform GetDroneTargetTransform(Collider other)
    {
        DroneHealth droneHealth = other.GetComponentInParent<DroneHealth>();
        return droneHealth != null ? droneHealth.transform : other.transform;
    }

    private bool IsPlayerDrone(Collider other)
    {
        return other.CompareTag("PlayerDrone") ||
               (other.transform.root != null && other.transform.root.CompareTag("PlayerDrone")) ||
               other.GetComponentInParent<DroneHealth>() != null;
    }

    private Transform FindClosestTarget()
    {
        Transform closest = null;
        float closestDist = Mathf.Infinity;

        foreach (Transform t in targetsInRange)
        {
            if (t == null) continue;
            float dist = Vector3.Distance(transform.position, t.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = t;
            }
        }
        return closest;
    }

    private void RotateTowards(Vector3 targetPos)
    {
        Transform pivot = (turretPivot != null) ? turretPivot : transform;

        Vector3 direction = targetPos - pivot.position;
        if (direction.sqrMagnitude < 0.001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        targetRotation *= Quaternion.Euler(0, rotationOffsetY, 0);
        pivot.rotation = Quaternion.RotateTowards(pivot.rotation, targetRotation, 200f * Time.deltaTime);
    }

    private float GetReactionDelay()
    {
        float comms = CommandNetwork.Instance != null ? CommandNetwork.Instance.commsIntegrity : 1f;
        float multiplier = Mathf.Lerp(3f, 1f, comms);
        return weaponData.reactionDelayBase * multiplier;
    }

    // 通訊完整度越低，瞄準誤差越大 (最多放大到約5倍)
    // 雷達鎖定目標時，瞄準誤差套用降低倍率
    private float GetAimError()
    {
        float comms = CommandNetwork.Instance != null ? CommandNetwork.Instance.commsIntegrity : 1f;
        float worstCase = weaponData.baseAimError * 5f;
        float baseError = Mathf.Lerp(worstCase, weaponData.baseAimError, comms);
        if (currentTarget != null && RadarStation2.IsDroneDetected(currentTarget))
            baseError *= RadarStation2.GetAimErrorMultiplier();
        return baseError;
    }

    private void FireAtTarget()
    {
        if (currentTarget == null) return;

        Debug.Log($"{gameObject.name} ({weaponData.weaponName}) 對 {currentTarget.name} 開火！" +
                  $" [通訊完整度: {(CommandNetwork.Instance != null ? CommandNetwork.Instance.commsIntegrity : 1f):F2}]");

        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;

        if (weaponData.projectilePrefab != null)
        {
            GameObject proj = Instantiate(weaponData.projectilePrefab, spawnPos, Quaternion.identity);
            SimpleProjectile sp = proj.GetComponent<SimpleProjectile>();
            if (sp != null)
            {
                sp.target = currentTarget;
                sp.speed = weaponData.projectileSpeed;
                sp.damage = weaponData.damage;
                sp.isGuided = weaponData.isGuided;
                sp.aimError = GetAimError();
            }
        }
    }

    void OnDrawGizmos()
    {
        if (weaponData == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, weaponData.detectionRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, weaponData.fireRange);
    }
}
